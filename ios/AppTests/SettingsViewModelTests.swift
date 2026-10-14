import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Provider rows by provider (AC-IOS-095 to 097), and what Settings reads from the owners.
final class SettingsViewModelTests: XCTestCase {
    private let metaText = "Audio streams directly from this iPhone to Meta. No PC or model download. Cloud usage is billed by Meta. Recording stops if the connection fails; resume to reconnect."
    private let pcText = "Audio streams to the model on your PC's GPU. No speech API charges. Keep Docker Desktop and LiveCue Desktop running. Switching models may take a minute; recording starts only when ready."
    private let onDeviceCodexText = "Audio is transcribed on this iPhone. Only text is sent to your PC."
    private let onDeviceOpenRouterText = "Audio is transcribed on this iPhone. Only text is sent to OpenRouter when you tap Assist."

    @MainActor
    private func makeModel(_ owners: TestOwners) -> SettingsViewModel {
        SettingsViewModel(
            speech: owners.speech,
            assistant: owners.assistant,
            relay: owners.relay,
            models: owners.models,
            readiness: owners.readiness
        )
    }

    // MARK: - The setup rows (AC-IOS-095, 096, 097)

    func testTheSetupRowsOfEveryProvider() {
        func rows(_ provider: SpeechProvider, key: Bool = false, loaded: Bool = false) -> [ProviderSetupState.Row] {
            ProviderSetupState(provider: provider, hasMetaKey: key, pcModelLabel: "Ready", isModelLoaded: loaded).rows
        }

        // Meta, and only Meta, has the key row.
        XCTAssertEqual(rows(.meta, key: true), [.metaKey(value: "Saved on iPhone", needsAttention: false)])
        XCTAssertEqual(rows(.meta), [.metaKey(value: "Required", needsAttention: true)])

        // The PC-local providers, and only they, have the PC speech model row with the home label.
        XCTAssertEqual(rows(.nemotron), [.pcSpeechModel(value: "Ready")])
        XCTAssertEqual(rows(.qwen3), [.pcSpeechModel(value: "Ready")])

        // The on-device providers, and only they, have Model Library and Speech benchmark.
        XCTAssertEqual(rows(.voz), [.modelLibrary(value: "Not loaded"), .benchmark])
        XCTAssertEqual(rows(.parakeet), [.modelLibrary(value: "Not loaded"), .benchmark])
        XCTAssertEqual(rows(.voz, loaded: true), [.modelLibrary(value: "Ready"), .benchmark])
        XCTAssertEqual(rows(.parakeet, loaded: true), [.modelLibrary(value: "Ready"), .benchmark])
        XCTAssertEqual(rows(.fluid), [.modelLibrary(value: "Not loaded"), .benchmark])
        XCTAssertEqual(rows(.fluid, loaded: true), [.modelLibrary(value: "Ready"), .benchmark])

        // Apple has the same two rows; its Model Library row also says why it cannot be used (D-28).
        XCTAssertEqual(rows(.apple), [.modelLibrary(value: "Not downloaded"), .benchmark])
        XCTAssertEqual(rows(.apple, loaded: true), [.modelLibrary(value: "Ready"), .benchmark])
        let older = ProviderSetupState(
            provider: .apple,
            hasMetaKey: false,
            pcModelLabel: "Ready",
            isModelLoaded: false,
            modelNote: "Requires iOS 26 or later"
        )
        XCTAssertEqual(older.rows, [.modelLibrary(value: "Requires iOS 26 or later"), .benchmark])
        // The note is Apple's only.
        let voz = ProviderSetupState(provider: .voz, hasMetaKey: false, pcModelLabel: "Ready", isModelLoaded: false, modelNote: "Requires iOS 26 or later")
        XCTAssertEqual(voz.rows, [.modelLibrary(value: "Not loaded"), .benchmark])
    }

    @MainActor
    func testTheRowsFollowTheSelectedProvider() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)

        XCTAssertEqual(model.providerName, "Meta Muse · cloud")
        XCTAssertEqual(model.setup.rows, [.metaKey(value: "Saved on iPhone", needsAttention: false)])
        XCTAssertEqual(model.explanation, metaText)

        owners.speech.removeMetaKey()
        XCTAssertEqual(model.setup.rows, [.metaKey(value: "Required", needsAttention: true)])

        // A PC-local provider: the row carries the home label, which follows the monitor.
        owners.speech.select(.nemotron)
        XCTAssertEqual(model.providerName, "Nemotron 3.5 · PC")
        XCTAssertEqual(model.setup.rows, [.pcSpeechModel(value: "PC offline")])
        XCTAssertEqual(model.explanation, pcText)
        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.setup.rows, [.pcSpeechModel(value: "Stopped · open Settings")])

        owners.speech.select(.qwen3)
        XCTAssertEqual(model.providerName, "Qwen3 1.7B · PC")
        XCTAssertEqual(model.explanation, pcText)

        // An on-device provider: nothing is loaded until the user prepares the model.
        owners.speech.select(.parakeet)
        XCTAssertEqual(model.providerName, "Parakeet · iPhone")
        XCTAssertEqual(model.setup.rows, [.modelLibrary(value: "Not loaded"), .benchmark])
        XCTAssertEqual(model.explanation, onDeviceCodexText)
        try await owners.models.prepare(.parakeet)
        XCTAssertEqual(model.setup.rows, [.modelLibrary(value: "Ready"), .benchmark])

        owners.speech.select(.voz)
        XCTAssertEqual(model.providerName, "Voz · iPhone")
        XCTAssertEqual(model.setup.rows, [.modelLibrary(value: "Not loaded"), .benchmark])
    }

    @MainActor
    func testTheOnDeviceExplanationNamesTheAnswerService() async {
        let owners = TestOwners(provider: .voz, answers: .openrouter)
        let model = makeModel(owners)

        XCTAssertEqual(model.explanation, onDeviceOpenRouterText)
    }

    // MARK: - The provider lock

    @MainActor
    func testTheProviderRowIsLockedWhileThePCModelChanges() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        XCTAssertNil(model.providerLockCaption)

        let starting = PCModelStatus(state: "starting", model: "nemotron", elapsedSeconds: 3, message: "Starting")
        await owners.gateway.setStatus(.success(starting))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(model.providerLockCaption, "Available when the PC model has finished changing.")

        let ready = PCModelStatus(state: "ready", model: "nemotron", message: "Ready")
        await owners.gateway.setStatus(.success(ready))
        await owners.relay.refreshPCModel()
        XCTAssertNil(model.providerLockCaption)
    }

    // MARK: - Assistant

    @MainActor
    func testTheAssistantRowShowsTheModelOrWhatToDo() async {
        let codex = makeModel(TestOwners())
        XCTAssertEqual(codex.assistantSubtitle, "gpt-5.6-sol")

        let openRouter = makeModel(TestOwners(answers: .openrouter))
        XCTAssertEqual(openRouter.assistantSubtitle, "Choose an OpenRouter model")
    }

    @MainActor
    func testTheInstructionIsReadFromAndSavedByTheOwner() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        XCTAssertEqual(model.instruction, "")

        model.instruction = "Answer briefly."
        XCTAssertEqual(owners.assistant.instruction, "Answer briefly.")
        XCTAssertEqual(owners.preferences.assistantInstruction, "Answer briefly.")

        owners.assistant.instruction = "Answer in one sentence."
        XCTAssertEqual(model.instruction, "Answer in one sentence.")
    }

    // MARK: - Devices and About

    @MainActor
    func testThePairingRowFollowsTheSavedPC() async {
        let unpaired = makeModel(TestOwners(paired: false))
        XCTAssertEqual(unpaired.pairingTitle, "Pair Windows PC")
        XCTAssertNil(unpaired.pairingSubtitle)

        let owners = TestOwners()
        let model = makeModel(owners)
        XCTAssertEqual(model.pairingTitle, "PC connection")
        XCTAssertEqual(model.pairingSubtitle, "Saved · reconnecting")

        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.pairingSubtitle, "Saved · connected")

        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.pairingSubtitle, "Saved · reconnecting")
    }

    @MainActor
    func testTheVersionIsTheMarketingVersion() async {
        let model = makeModel(TestOwners())

        XCTAssertEqual(model.versionText, AppVersion.marketing)
        XCTAssertFalse(model.versionText.isEmpty)
    }

    // MARK: - PC model observation

    @MainActor
    func testAppearingAndDisappearingStayInPairs() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        owners.relay.start()
        // The health loop made its first pass and sleeps.
        await owners.clock.waitForSleepers(count: 1)

        // Appearing twice still counts as one screen that shows the PC model.
        model.appeared()
        model.appeared()
        await owners.clock.waitForSleepers(count: 2)

        model.disappeared()
        await SpeechTest.eventually { owners.clock.sleeperCount == 1 }
        XCTAssertEqual(owners.clock.sleeperCount, 1, "The PC model is no longer polled.")

        // Disappearing again changes nothing, and the screen can appear again.
        model.disappeared()
        model.appeared()
        await owners.clock.waitForSleepers(count: 2)
        model.disappeared()
        await SpeechTest.eventually { owners.clock.sleeperCount == 1 }
    }
}
