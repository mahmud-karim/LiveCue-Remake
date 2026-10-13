import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// needsPC, speechReady, answersReady, systemReady and the provider lock.
final class SystemReadinessTests: XCTestCase {
    private let readyNemotron = PCModelStatus(state: "ready", model: "nemotron", message: "Model loaded on the PC GPU. Ready for live transcription.")
    /// Not a key of any service.
    private let syntheticKey = "synthetic-unit-key"
    private let stubReason = "OnDeviceModelManager is still the stub of the foundation package. This test runs once WP-07 has replaced it."

    /// True while `OnDeviceModelManager` is still the stub of the foundation package: the stub loads
    /// nothing, whatever it is given.
    @MainActor
    private func modelManagerIsStillAStub() -> Bool {
        let probe = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: FakeBatchEngine(), parakeet: FakeStreamingEngine()),
            activity: SessionActivity(),
            clock: ManualClock()
        )
        probe.installPreloaded(variant: "parakeet", voz: nil, parakeet: FakeStreamingEngine())
        return !probe.isLoaded(for: .parakeet)
    }

    /// Waits, for at most five seconds of real time, until `condition` holds.
    @MainActor
    private func waitUntil(
        _ what: String,
        file: StaticString = #filePath,
        line: UInt = #line,
        _ condition: @MainActor () -> Bool
    ) async {
        for _ in 0..<5_000 {
            if condition() { return }
            try? await Task.sleep(nanoseconds: 1_000_000)
        }
        XCTFail("Timed out waiting until \(what).", file: file, line: line)
    }

    // MARK: - Is the PC needed

    @MainActor
    func testThePCIsNeededForPCSpeechOrCodexAnswers() async {
        let expected: [(speech: SpeechProvider, answers: AssistantProvider, needsPC: Bool)] = [
            (.meta, .codex, true),
            (.meta, .openrouter, false),
            (.nemotron, .codex, true),
            (.nemotron, .openrouter, true),
            (.qwen3, .codex, true),
            (.qwen3, .openrouter, true),
            (.voz, .codex, true),
            (.voz, .openrouter, false),
            (.parakeet, .codex, true),
            (.parakeet, .openrouter, false),
            (.apple, .codex, true),
            (.apple, .openrouter, false)
        ]
        for row in expected {
            let owners = TestOwners(provider: row.speech, answers: row.answers)
            XCTAssertEqual(owners.readiness.needsPC, row.needsPC, "\(row.speech.rawValue) with \(row.answers.rawValue)")
            XCTAssertEqual(owners.relay.needsPC, row.needsPC, "\(row.speech.rawValue) with \(row.answers.rawValue)")
        }
    }

    @MainActor
    func testNeedsPCFollowsTheSelections() async {
        let owners = TestOwners(provider: .meta, answers: .openrouter)
        XCTAssertFalse(owners.readiness.needsPC)

        owners.speech.select(.qwen3)
        XCTAssertTrue(owners.readiness.needsPC)
        owners.speech.select(.voz)
        XCTAssertFalse(owners.readiness.needsPC)
        owners.assistant.switchProvider(.codex)
        XCTAssertTrue(owners.readiness.needsPC)
    }

    // MARK: - Speech (AC-IOS-008)

    @MainActor
    func testMetaSpeechIsReadyWithASavedKey() async {
        let without = TestOwners(provider: .meta, metaKey: nil)
        XCTAssertFalse(without.readiness.speechReady)

        XCTAssertTrue(without.speech.saveMetaKey(syntheticKey))
        XCTAssertTrue(without.readiness.speechReady)
        without.speech.removeMetaKey()
        XCTAssertFalse(without.readiness.speechReady)

        let with = TestOwners(provider: .meta)
        XCTAssertTrue(with.readiness.speechReady, "A key saved before the launch counts.")
    }

    @MainActor
    func testPCSpeechIsReadyWhenThePCModelCanStartASession() async {
        let owners = TestOwners(provider: .nemotron)
        let readiness = owners.readiness
        XCTAssertFalse(readiness.speechReady, "Nothing is known about the PC yet.")

        // Online, and the selected model is loaded and idle.
        await owners.gateway.setStatus(.success(readyNemotron))
        await owners.relay.checkHealthNow()
        XCTAssertTrue(readiness.speechReady)

        // In use.
        await owners.gateway.setStatus(.success(PCModelStatus(state: "ready", model: "nemotron", busy: true, message: "In use")))
        await owners.relay.refreshPCModel()
        XCTAssertFalse(readiness.speechReady)

        // The other model is loaded.
        await owners.gateway.setStatus(.success(PCModelStatus(state: "ready", model: "qwen3", message: "Ready")))
        await owners.relay.refreshPCModel()
        XCTAssertFalse(readiness.speechReady)

        // Not loaded.
        for state in ["stopped", "starting", "stopping", "error"] {
            await owners.gateway.setStatus(.success(PCModelStatus(state: state, model: state == "stopped" ? nil : "nemotron", message: state)))
            await owners.relay.refreshPCModel()
            XCTAssertFalse(readiness.speechReady, state)
        }

        // Loaded, and the relay goes offline.
        await owners.gateway.setStatus(.success(readyNemotron))
        await owners.relay.refreshPCModel()
        XCTAssertTrue(readiness.speechReady)
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await owners.relay.checkHealthNow()
        XCTAssertFalse(readiness.speechReady)

        // A Meta key does not make a PC speech provider ready.
        XCTAssertTrue(owners.speech.hasMetaKey)
    }

    @MainActor
    func testOnDeviceSpeechIsNotReadyBeforeItsModelIsLoaded() async {
        for provider in [SpeechProvider.voz, .parakeet] {
            let owners = TestOwners(provider: provider, answers: .openrouter)
            XCTAssertFalse(owners.readiness.speechReady, provider.rawValue)
            XCTAssertFalse(owners.readiness.systemReady, provider.rawValue)
            XCTAssertTrue(owners.speech.hasMetaKey, "A Meta key does not make an on-device provider ready.")
        }
    }

    /// Needs the real model manager (WP-07).
    @MainActor
    func testOnDeviceSpeechIsReadyOnceItsModelIsLoaded() async throws {
        try XCTSkipIf(modelManagerIsStillAStub(), stubReason)

        let cases: [(provider: SpeechProvider, model: TranscriptionModel)] = [
            (.voz, TranscriptionModel.voz),
            (.parakeet, TranscriptionModel.parakeet)
        ]
        for row in cases {
            let owners = TestOwners(provider: row.provider, answers: .openrouter)
            XCTAssertFalse(owners.readiness.speechReady, row.provider.rawValue)

            try await owners.models.prepare(row.model)
            XCTAssertTrue(owners.models.isLoaded(for: row.provider), row.provider.rawValue)
            XCTAssertTrue(owners.readiness.speechReady, row.provider.rawValue)

            owners.models.unload()
            XCTAssertFalse(owners.readiness.speechReady, row.provider.rawValue)
        }
    }

    // MARK: - Answers

    @MainActor
    func testCodexAnswersAreReadyWhenTheRelayIsOnline() async {
        let owners = TestOwners()
        XCTAssertFalse(owners.readiness.answersReady, "No check has succeeded yet.")

        await owners.relay.checkHealthNow()
        XCTAssertTrue(owners.readiness.answersReady)

        await owners.gateway.setHealth(.failure(RelayFailure.wire(.unauthorized)))
        await owners.relay.checkHealthNow()
        XCTAssertFalse(owners.readiness.answersReady)

        // An OpenRouter key does not make Codex ready.
        XCTAssertTrue(owners.assistant.saveOpenRouterKey(syntheticKey))
        XCTAssertFalse(owners.readiness.answersReady)

        let unpaired = TestOwners(paired: false)
        XCTAssertFalse(unpaired.readiness.answersReady)
    }

    @MainActor
    func testOpenRouterAnswersAreReadyWithAKeyAndAChosenModel() async {
        let owners = TestOwners(answers: .openrouter)
        let readiness = owners.readiness
        XCTAssertFalse(readiness.answersReady, "No key and no model.")

        XCTAssertTrue(owners.assistant.saveOpenRouterKey(syntheticKey))
        XCTAssertFalse(readiness.answersReady, "A key, and no model chosen.")

        owners.assistant.selectOpenRouterModel(AssistantModelOption(id: "test/direct", name: "Test Direct", reasoningEfforts: ["default"]))
        XCTAssertTrue(readiness.answersReady)

        owners.assistant.removeOpenRouterKey()
        XCTAssertFalse(readiness.answersReady, "A model, and no key.")

        // The relay does not matter for OpenRouter.
        XCTAssertTrue(owners.assistant.saveOpenRouterKey(syntheticKey))
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await owners.relay.checkHealthNow()
        XCTAssertTrue(readiness.answersReady)
    }

    // MARK: - Both (AC-IOS-008)

    @MainActor
    func testTheSystemIsReadyWhenAndOnlyWhenSpeechAndAnswersAre() async {
        // Meta with Codex.
        let owners = TestOwners(provider: .meta, metaKey: nil)
        let readiness = owners.readiness
        XCTAssertFalse(readiness.systemReady, "Neither is ready.")

        await owners.relay.checkHealthNow()
        XCTAssertTrue(readiness.answersReady)
        XCTAssertFalse(readiness.systemReady, "Answers alone.")

        XCTAssertTrue(owners.speech.saveMetaKey(syntheticKey))
        XCTAssertTrue(readiness.speechReady)
        XCTAssertTrue(readiness.systemReady)

        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await owners.relay.checkHealthNow()
        XCTAssertTrue(readiness.speechReady)
        XCTAssertFalse(readiness.systemReady, "Speech alone.")

        // Meta with OpenRouter: the cloud-only pair needs no PC at all.
        let cloud = TestOwners(provider: .meta, answers: .openrouter)
        XCTAssertFalse(cloud.readiness.systemReady)
        XCTAssertTrue(cloud.assistant.saveOpenRouterKey(syntheticKey))
        cloud.assistant.selectOpenRouterModel(AssistantModelOption(id: "test/direct", name: "Test Direct", reasoningEfforts: ["default"]))
        XCTAssertTrue(cloud.readiness.systemReady)
        let cloudCalls = await cloud.gateway.calls
        XCTAssertEqual(cloudCalls, [])

        // PC speech with Codex: one check makes both ready.
        let pc = TestOwners(provider: .nemotron)
        await pc.gateway.setStatus(.success(readyNemotron))
        XCTAssertFalse(pc.readiness.systemReady)
        await pc.relay.checkHealthNow()
        XCTAssertTrue(pc.readiness.speechReady)
        XCTAssertTrue(pc.readiness.answersReady)
        XCTAssertTrue(pc.readiness.systemReady)
    }

    // MARK: - Why the provider cannot be changed

    @MainActor
    func testTheCaptionsOfTheLockReasons() async {
        XCTAssertEqual(ProviderLockReason.preparingModel.caption, "Available when the model has finished preparing.")
        XCTAssertEqual(ProviderLockReason.pcModelChanging.caption, "Available when the PC model has finished changing.")
    }

    @MainActor
    func testTheProviderIsLockedWhileThePCModelIsChanging() async {
        let owners = TestOwners(provider: .nemotron)
        let readiness = owners.readiness
        XCTAssertNil(readiness.providerLockReason)

        // The PC reports starting, then stopping.
        for state in ["starting", "stopping"] {
            await owners.gateway.setStatus(.success(PCModelStatus(state: state, model: "nemotron", elapsedSeconds: 4, message: state)))
            await owners.relay.refreshPCModel()
            XCTAssertEqual(readiness.providerLockReason, .pcModelChanging, state)
        }

        // Every state at rest, in use or not, leaves the provider free.
        let atRest = [
            PCModelStatus(state: "stopped", message: "Stopped"),
            PCModelStatus(state: "error", model: "nemotron", message: "Error"),
            readyNemotron,
            PCModelStatus(state: "ready", model: "nemotron", busy: true, message: "In use")
        ]
        for status in atRest {
            await owners.gateway.setStatus(.success(status))
            await owners.relay.refreshPCModel()
            XCTAssertNil(readiness.providerLockReason, status.state)
        }

        // A command in flight.
        await owners.gateway.setStatus(.success(readyNemotron))
        await owners.relay.refreshPCModel()
        let sent = await owners.gateway.calls.count
        await owners.gateway.setHold(true, for: "stopLocalModel")
        await owners.gateway.setStop(.success(PCModelStatus(state: "stopped", message: "Stopped")))
        let stop = Task { await owners.relay.stopPCModel() }
        await owners.gateway.waitForCalls(count: sent + 1)
        XCTAssertEqual(readiness.providerLockReason, .pcModelChanging)
        await owners.gateway.setHold(false, for: "stopLocalModel")
        await owners.gateway.release("stopLocalModel")
        await stop.value
        XCTAssertNil(readiness.providerLockReason)
    }

    @MainActor
    func testARunningSessionIsNotALockReason() async {
        let owners = TestOwners()
        owners.activity.isActive = true
        owners.activity.isAssisting = true

        XCTAssertNil(owners.readiness.providerLockReason, "The screens that show a lock caption cannot be open during a session.")
        // The settings logic still refuses the change by itself.
        owners.speech.select(.parakeet)
        XCTAssertEqual(owners.speech.provider, .meta)
    }

    /// Needs the real model manager (WP-07).
    @MainActor
    func testTheProviderIsLockedWhileAnOnDeviceModelIsPrepared() async throws {
        try XCTSkipIf(modelManagerIsStillAStub(), stubReason)

        let owners = TestOwners(provider: .voz)
        let gate = FakeGate()
        let models = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: owners.vozEngine, parakeet: owners.parakeetEngine, gate: gate),
            activity: owners.activity,
            clock: owners.clock
        )
        let readiness = SystemReadiness(speech: owners.speech, assistant: owners.assistant, relay: owners.relay, models: models)
        XCTAssertNil(readiness.providerLockReason)

        let preparation = Task { try? await models.prepare(TranscriptionModel.voz) }
        await waitUntil("the preparation began") { models.isPreparing }
        XCTAssertEqual(readiness.providerLockReason, .preparingModel)
        XCTAssertFalse(readiness.speechReady)

        await gate.open()
        _ = await preparation.value
        XCTAssertFalse(models.isPreparing)
        XCTAssertNil(readiness.providerLockReason)
        XCTAssertTrue(readiness.speechReady)
    }
}
