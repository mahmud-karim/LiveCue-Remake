import LiveCueCore
import XCTest
@testable import LiveCueRemake

// MARK: - Helpers shared by the session controller tests

extension TestOwners {
    /// Owners whose selected provider can start: a PC-local provider finds its model ready on the PC.
    static func ready(_ provider: SpeechProvider, answers: AssistantProvider = .codex) async -> TestOwners {
        let owners = TestOwners(provider: provider, answers: answers)
        if provider.isPCLocal {
            let status = PCModelStatus(state: "ready", model: provider.rawValue, message: "Ready")
            await owners.gateway.setStatus(.success(status))
        }
        return owners
    }

    /// Starts a session and returns its transcriber. The session is listening.
    func startListening(file: StaticString = #filePath, line: UInt = #line) async throws -> FakeTranscriber {
        await session.start()
        XCTAssertEqual(session.phase, .listening, "The session did not start.", file: file, line: line)
        return try XCTUnwrap(transcribers.made.last, file: file, line: line)
    }

    /// Commits a segment through the transcriber and waits until the controller has applied and saved it.
    @discardableResult
    func say(_ text: String, at start: Double = 0) async throws -> TranscriptSegment {
        let transcriber = try XCTUnwrap(transcribers.made.last)
        let segment = TranscriptSegment(text: text, startSeconds: start, endSeconds: start + 1)
        transcriber.emit(.segment(segment))
        await session.waitUntilSettled()
        return segment
    }

    /// One tick of the session clock, applied and saved. Only the session's tick may be sleeping.
    func tick() async {
        await clock.waitForSleepers(count: 1)
        await clock.advance(by: 1)
        await clock.waitForSleepers(count: 1)
        await session.waitUntilSettled()
    }

    /// The stored copy of a session.
    func stored(_ id: UUID) async -> Session? {
        let all = await sessions.loadAll()
        return all.first { $0.id == id }
    }

    /// Waits, for at most five seconds of real time, until `condition` holds. Used only where the
    /// controller works in a task of its own, so that no call of the test returns when it is done.
    func waitUntil(
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

    /// Codex answers can be given: the PC's catalogue offers the default selection.
    func offerCodexCatalogue() async {
        await codex.setCatalogue(.success(FixtureValues.codexCatalogue))
    }

    /// OpenRouter answers can be given: a key, a loaded catalogue and a model chosen from it.
    func prepareOpenRouter() throws {
        try secrets.save("fixture-openrouter-key", for: .openRouterAPIKey)
        assistant.adoptCatalogue(FixtureValues.openRouterCatalogue)
        assistant.selectOpenRouterModel(FixtureValues.openRouterCatalogue[0])
    }
}

/// One test per row T-01 to T-21 of section 4.1.1.
final class SessionControllerStateTests: XCTestCase {
    private let metaKeyText = "Add your Meta API key in Transcription. It stays in this iPhone's Keychain."
    private let microphoneText = "Microphone permission is required for live transcription. If you use LiveContainer, allow it for LiveContainer in Settings."
    private let finalizeText = "Could not finalize the transcript. Some final words may be missing."
    private let connectingToMeta = "Connecting to Meta Muse \u{00B7} cloud\u{2026}"
    private let finishing = "Finishing the transcript\u{2026}"
    private let disconnected = "Disconnected \u{00B7} resume to retry"

    private func segment(_ text: String, at start: Double) -> TranscriptSegment {
        TranscriptSegment(text: text, startSeconds: start, endSeconds: start + 1)
    }

    /// A start that waits in the transcriber. Returns the task of the start and the transcriber.
    @MainActor
    private func beginHeldStart(_ owners: TestOwners) async throws -> (Task<Void, Never>, FakeTranscriber) {
        let previous = owners.transcribers.configure
        owners.transcribers.configure = { transcriber in
            previous?(transcriber)
            transcriber.holdStart = true
        }
        let starting = Task { await owners.session.start() }
        await owners.waitUntil("the transcriber was started") { owners.transcribers.made.last?.starts.count == 1 }
        let transcriber = try XCTUnwrap(owners.transcribers.made.last)
        return (starting, transcriber)
    }

    // MARK: - T-01, T-02 and the start checks

    @MainActor
    func testT01StartEntersPreflightAndIsSingleFlight() async {
        let owners = await TestOwners.ready(.nemotron)
        await owners.gateway.setHold(true, for: "localModelStatus")

        let starting = Task { await owners.session.start() }
        await owners.gateway.waitForCalls(count: 1)
        XCTAssertEqual(owners.session.phase, .preflight)
        XCTAssertTrue(owners.session.isTransitioning)
        XCTAssertNil(owners.session.session, "Nothing exists before the checks have passed.")
        XCTAssertFalse(owners.session.showsLiveScreen)
        XCTAssertFalse(owners.session.controlsEnabled)
        XCTAssertFalse(owners.activity.isActive)

        // A second start returns at once: nothing more is asked of the PC.
        await owners.session.start()
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["localModelStatus"])
        XCTAssertEqual(owners.session.phase, .preflight)

        await owners.gateway.setHold(false, for: "localModelStatus")
        await owners.gateway.release("localModelStatus")
        await starting.value
        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertEqual(owners.transcribers.made.count, 1, "One start made one session.")
        XCTAssertEqual(owners.session.provider, .nemotron)
        XCTAssertNil(owners.session.estimatedCost, "Only a Meta session shows a cost.")
    }

    @MainActor
    func testT02AFailedCheckAlertsAndCreatesNothing() async {
        let owners = TestOwners(metaKey: nil)

        await owners.session.start()

        // AC-IOS-016
        XCTAssertEqual(owners.app.alertMessage, metaKeyText)
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertNil(owners.session.session)
        XCTAssertNil(owners.session.provider)
        XCTAssertFalse(owners.session.showsLiveScreen)
        XCTAssertFalse(owners.activity.isActive)
        XCTAssertTrue(owners.transcribers.made.isEmpty)
        XCTAssertEqual(owners.clock.sleeperCount, 0, "No tick was started.")
        let stored = await owners.sessions.loadAll()
        XCTAssertTrue(stored.isEmpty)
    }

    @MainActor
    func testStartCheckOfThePCModelNamesItsHomeLabel() async {
        // AC-IOS-022: the PC is online and its model is stopped.
        var owners = TestOwners(provider: .nemotron)
        await owners.relay.checkHealthNow()
        await owners.session.start()
        XCTAssertEqual(
            owners.app.alertMessage,
            "PC speech model: Stopped \u{00B7} open Settings. Open Transcription to start it or check its status."
        )
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertTrue(owners.transcribers.made.isEmpty)

        // The model is ready, but another conversation is using it.
        owners = TestOwners(provider: .qwen3)
        await owners.gateway.setStatus(.success(PCModelStatus(state: "ready", model: "qwen3", busy: true, message: "In use")))
        await owners.relay.checkHealthNow()
        await owners.session.start()
        XCTAssertEqual(
            owners.app.alertMessage,
            "PC speech model: In use. Open Transcription to start it or check its status."
        )
        XCTAssertEqual(owners.session.phase, .idle)

        // Nothing is paired: the model cannot be known.
        owners = TestOwners(paired: false, provider: .nemotron)
        await owners.session.start()
        XCTAssertEqual(
            owners.app.alertMessage,
            "PC speech model: Pair PC first. Open Transcription to start it or check its status."
        )
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [], "Nothing is asked of a PC that is not paired.")
    }

    @MainActor
    func testStartCheckOfTheRelayChecksHealthOnceAndShowsTheConnectionMessage() async {
        let owners = await TestOwners.ready(.nemotron)
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))

        await owners.session.start()

        XCTAssertEqual(
            owners.app.alertMessage,
            "Your pairing is saved. Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically."
        )
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["localModelStatus", "health"], "The model first, then one health check.")
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertTrue(owners.transcribers.made.isEmpty)
    }

    @MainActor
    func testStartCheckOfTheMicrophone() async {
        // AC-IOS-017
        let owners = TestOwners(microphoneGranted: false)

        await owners.session.start()

        XCTAssertEqual(owners.app.alertMessage, microphoneText)
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertNil(owners.session.session)
        XCTAssertTrue(owners.transcribers.made.isEmpty)
    }

    @MainActor
    func testStartChecksRunInOrder() async {
        // 1 before 4: Meta without a key, and the microphone denied.
        var owners = TestOwners(metaKey: nil, microphoneGranted: false)
        await owners.session.start()
        XCTAssertEqual(owners.app.alertMessage, metaKeyText)

        // 2 before 3 and 4: the model is stopped, the relay is offline, the microphone is denied.
        owners = TestOwners(provider: .nemotron, microphoneGranted: false)
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await owners.session.start()
        XCTAssertEqual(
            owners.app.alertMessage,
            "PC speech model: PC offline. Open Transcription to start it or check its status."
        )
        var calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["localModelStatus"], "The health check of check 3 was not reached.")

        // 3 before 4: the model is ready, the relay is offline, the microphone is denied.
        owners = TestOwners(provider: .nemotron, microphoneGranted: false)
        await owners.gateway.setStatus(.success(PCModelStatus(state: "ready", model: "nemotron", message: "Ready")))
        await owners.gateway.setHealth(.failure(RelayFailure.wire(.relayPaused)))
        await owners.session.start()
        XCTAssertEqual(owners.app.alertMessage, "PC relay is paused. Resume it in LiveCue Desktop.")
        calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["localModelStatus", "health"])

        // The checks of a PC-local provider do not run for the others.
        owners = TestOwners(paired: false, provider: .voz, microphoneGranted: false)
        await owners.session.start()
        XCTAssertEqual(owners.app.alertMessage, microphoneText)
        owners = TestOwners(paired: false, provider: .parakeet, metaKey: nil)
        await owners.session.start()
        XCTAssertEqual(owners.session.phase, .listening, "An on-device session needs no key and no PC.")
    }

    // MARK: - T-03, T-04, T-05

    @MainActor
    func testT03CreatesTheSessionAndConnects() async throws {
        let owners = TestOwners()
        owners.app.livePath = [.transcription]
        owners.app.settingsPath = [.pairing]

        let (starting, transcriber) = try await beginHeldStart(owners)
        let session = try XCTUnwrap(owners.session.session)

        XCTAssertEqual(owners.session.phase, .connecting)
        XCTAssertTrue(owners.session.isTransitioning)
        XCTAssertTrue(owners.session.showsLiveScreen)
        XCTAssertFalse(owners.session.controlsEnabled)
        XCTAssertEqual(session.title, "New conversation")
        XCTAssertEqual(session.startedAt, owners.clock.now())
        XCTAssertNil(session.endedAt)
        XCTAssertEqual(owners.session.provider, .meta)
        XCTAssertEqual(owners.session.transcriberStatus, connectingToMeta)
        XCTAssertEqual(owners.session.elapsedSeconds, 0)
        XCTAssertEqual(owners.session.partialText, "")
        XCTAssertNil(owners.session.notice)
        XCTAssertEqual(owners.session.estimatedCost, "$0.00000 USD")
        XCTAssertEqual(owners.session.listeningStartedCount, 0)
        XCTAssertTrue(owners.activity.isActive)
        XCTAssertEqual(owners.app.livePath, [])
        XCTAssertEqual(owners.app.settingsPath, [])
        XCTAssertEqual(transcriber.provider, .meta)
        XCTAssertEqual(transcriber.starts.map(\.streamOffsetSeconds), [0])

        // The clock already runs, and nothing is saved before the transcriber is connected (IOS-B2).
        await owners.tick()
        XCTAssertEqual(owners.session.elapsedSeconds, 1)
        let stored = await owners.sessions.loadAll()
        XCTAssertTrue(stored.isEmpty)

        transcriber.releaseStart()
        await starting.value
        XCTAssertEqual(owners.session.phase, .listening)
    }

    @MainActor
    func testT04TheFirstSaveIsMadeWhenListeningBegins() async throws {
        let owners = TestOwners()
        let (starting, transcriber) = try await beginHeldStart(owners)

        // What the start emitted is applied before the session listens (the barrier).
        transcriber.emit(.status("Meta direct \u{00B7} 0.12 s setup"))
        transcriber.emit(.segment(segment("First words", at: 0)))
        transcriber.releaseStart()
        await starting.value
        await owners.session.waitUntilSettled()

        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertFalse(owners.session.isTransitioning)
        XCTAssertTrue(owners.session.controlsEnabled)
        XCTAssertEqual(owners.session.listeningStartedCount, 1)
        XCTAssertEqual(owners.session.transcriberStatus, "Meta direct \u{00B7} 0.12 s setup")
        let id = try XCTUnwrap(owners.session.session?.id)
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.segments.map(\.text), ["First words"])
        XCTAssertEqual(stored?.title, "New conversation")
        XCTAssertNil(stored?.endedAt)
        XCTAssertEqual(stored?.transcriptionUsage?.provider, "meta")
        // History is refreshed only at launch, on Stop, on delete and when a summary arrives.
        XCTAssertTrue(owners.history.sessions.isEmpty)
    }

    @MainActor
    func testT05AFailedStartLeavesNothing() async {
        let owners = TestOwners()
        owners.transcribers.configure = { transcriber in
            transcriber.startResult = .failure(TranscriberFailure(.stream, message: "PC transcription is not ready."))
        }
        owners.app.selectedTab = .settings

        await owners.session.start()

        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertNil(owners.session.session)
        XCTAssertNil(owners.session.provider)
        XCTAssertFalse(owners.session.showsLiveScreen)
        XCTAssertFalse(owners.activity.isActive)
        XCTAssertEqual(owners.app.alertMessage, "Transcription could not start. PC transcription is not ready.")
        XCTAssertEqual(owners.app.selectedTab, .live)
        XCTAssertEqual(owners.transcribers.made.last?.calls, ["start", "shutdown"])
        XCTAssertEqual(owners.session.listeningStartedCount, 0)
        XCTAssertEqual(owners.session.transcriberStatus, "")
        await owners.waitUntil("the tick stopped") { owners.clock.sleeperCount == 0 }

        // Nothing was saved, so nothing is left (IOS-B2).
        await owners.session.waitUntilSettled()
        let stored = await owners.sessions.loadAll()
        XCTAssertTrue(stored.isEmpty)
        await owners.history.reload()
        XCTAssertTrue(owners.history.sessions.isEmpty)

        // The next start is a new session.
        owners.transcribers.configure = nil
        await owners.session.start()
        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertEqual(owners.transcribers.made.count, 2)
    }

    // MARK: - T-06, T-07, T-08

    @MainActor
    func testT06AndT07PauseFinalizesThenPauses() async throws {
        let owners = TestOwners()
        let transcriber = try await owners.startListening()
        let id = try XCTUnwrap(owners.session.session?.id)
        try await owners.say("One")
        transcriber.emit(.level(0.5))
        transcriber.emit(.partial("and tw"))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.level, 0.5)
        transcriber.segmentsOnFinalize = ["and two"]

        await owners.session.togglePause()

        XCTAssertEqual(owners.session.phase, .paused)
        XCTAssertEqual(transcriber.calls, ["start", "finalize"])
        // The final text was committed before "Paused" (AC-IOS-040).
        XCTAssertEqual(owners.session.session?.segments.map(\.text), ["One", "and two"])
        XCTAssertEqual(owners.session.transcriberStatus, finishing)
        XCTAssertEqual(owners.session.level, 0)
        XCTAssertNil(owners.session.notice)
        XCTAssertFalse(owners.session.pausedAutomatically)
        XCTAssertFalse(owners.session.isTransitioning)
        XCTAssertTrue(owners.session.controlsEnabled)
        XCTAssertTrue(owners.session.showsLiveScreen)
        XCTAssertNil(owners.app.alertMessage)
        XCTAssertEqual(owners.session.listeningStartedCount, 1)

        await owners.session.waitUntilSettled()
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.segments.map(\.text), ["One", "and two"])
        XCTAssertNil(stored?.endedAt)
    }

    @MainActor
    func testT08AFailedFinalizeOnPauseIsANotice() async throws {
        // AC-IOS-047, as ruled by R-27: a notice on Pause, never an alert while the live screen is up.
        let owners = TestOwners()
        let transcriber = try await owners.startListening()
        transcriber.finalizeResult = .failure(TranscriberFailureText.finalizeFailed)

        await owners.session.togglePause()

        XCTAssertEqual(owners.session.phase, .paused, "The transition still completes.")
        XCTAssertEqual(owners.session.notice, SessionNotice(kind: .finalizeFailed, message: finalizeText))
        XCTAssertEqual(owners.session.notice?.title, "Paused")
        XCTAssertFalse(owners.session.pausedAutomatically)
        XCTAssertNil(owners.app.alertMessage)
        XCTAssertEqual(owners.session.level, 0)
        XCTAssertTrue(owners.session.controlsEnabled)

        // Resume clears the notice.
        await owners.session.togglePause()
        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertNil(owners.session.notice)
    }

    // MARK: - T-09, T-10

    @MainActor
    func testT09AStreamFailurePausesForEveryStreamingProvider() async throws {
        // AC-IOS-044, 045, 046: the texts of the three capture failures.
        XCTAssertEqual(
            TranscriberFailureText.microphoneInterrupted.message,
            "Microphone interrupted. Resume when your call or other audio finishes."
        )
        XCTAssertEqual(
            TranscriberFailureText.routeChanged.message,
            "Microphone route changed. Resume to use the current microphone."
        )
        XCTAssertEqual(
            TranscriberFailureText.uploadOverflow.message,
            "Network cannot keep up. Recording paused; resume to retry."
        )

        let failures = [
            TranscriberFailure(.stream, message: "The connection to your PC dropped. Resume to reconnect."),
            TranscriberFailureText.uploadOverflow,
            TranscriberFailureText.microphoneInterrupted,
            TranscriberFailureText.routeChanged
        ]
        for provider in [SpeechProvider.meta, .nemotron, .qwen3] {
            for failure in failures {
                let context = "\(provider.rawValue): \(failure.message)"
                let owners = await TestOwners.ready(provider)
                let transcriber = try await owners.startListening()
                let id = try XCTUnwrap(owners.session.session?.id)
                transcriber.emit(.level(0.75))
                transcriber.emit(.failed(failure))
                await owners.session.waitUntilSettled()

                // AC-IOS-043, as ruled by R-03: Paused, with the reason in the notice.
                XCTAssertEqual(owners.session.phase, .paused, context)
                XCTAssertTrue(owners.session.pausedAutomatically, context)
                XCTAssertEqual(
                    owners.session.notice,
                    SessionNotice(kind: .recordingPaused, message: failure.message),
                    context
                )
                XCTAssertEqual(owners.session.notice?.title, "Recording paused", context)
                XCTAssertEqual(owners.session.transcriberStatus, disconnected, context)
                XCTAssertEqual(owners.session.level, 0, context)
                XCTAssertNil(owners.app.alertMessage, context)
                XCTAssertTrue(owners.session.controlsEnabled, context)
                // Nothing is finalised, and nothing reconnects until Resume.
                XCTAssertEqual(transcriber.calls, ["start"], context)
                let stored = await owners.stored(id)
                XCTAssertNotNil(stored, context)

                // The generation is retired: what it still says is dropped.
                transcriber.emit(.partial("too late"))
                await owners.session.waitUntilSettled()
                XCTAssertEqual(owners.session.partialText, "", context)
            }
        }
    }

    @MainActor
    func testT10AnOnDeviceFailureFinalizesFirstForEveryOnDeviceProvider() async throws {
        let failures = [
            TranscriberFailureText.pendingAudioLimit,
            TranscriberFailureText.processingOverflow,
            TranscriberFailureText.processingFailed,
            TranscriberFailureText.microphoneInterrupted,
            TranscriberFailureText.routeChanged
        ]
        for provider in [SpeechProvider.voz, .parakeet] {
            for failure in failures {
                let context = "\(provider.rawValue): \(failure.message)"
                let owners = TestOwners(provider: provider)
                let transcriber = try await owners.startListening()
                let id = try XCTUnwrap(owners.session.session?.id)
                try await owners.say("Heard before")
                transcriber.segmentsOnFinalize = ["Kept words"]

                transcriber.emit(.failed(failure))
                await owners.waitUntil("the session paused (\(context))") { owners.session.phase == .paused }

                // Recognised text is kept: the finalisation ran first (IOS-B4, A-18).
                XCTAssertEqual(transcriber.calls, ["start", "finalize"], context)
                XCTAssertEqual(owners.session.session?.segments.map(\.text), ["Heard before", "Kept words"], context)
                XCTAssertTrue(owners.session.pausedAutomatically, context)
                XCTAssertEqual(
                    owners.session.notice,
                    SessionNotice(kind: .recordingPaused, message: failure.message),
                    context
                )
                XCTAssertEqual(owners.session.transcriberStatus, disconnected, context)
                XCTAssertEqual(owners.session.level, 0, context)
                XCTAssertNil(owners.app.alertMessage, context)
                XCTAssertTrue(owners.session.controlsEnabled, context)
                await owners.session.waitUntilSettled()
                let stored = await owners.stored(id)
                XCTAssertEqual(stored?.segments.map(\.text), ["Heard before", "Kept words"], context)
            }
        }
    }

    // MARK: - T-11, T-12, T-13

    @MainActor
    func testT11AndT12ResumeOpensANewStreamAtTheElapsedOffset() async throws {
        let owners = TestOwners()
        let transcriber = try await owners.startListening()
        let id = try XCTUnwrap(owners.session.session?.id)
        transcriber.emit(.usage(.begin(id: UUID())))
        await owners.tick()
        await owners.tick()
        transcriber.emit(.partial("half a sen"))
        await owners.session.waitUntilSettled()
        await owners.session.togglePause()
        await owners.tick()
        XCTAssertEqual(owners.session.elapsedSeconds, 3)
        XCTAssertEqual(owners.session.partialText, "half a sen")

        transcriber.holdStart = true
        let resuming = Task { await owners.session.togglePause() }
        await owners.waitUntil("the transcriber was started again") { transcriber.starts.count == 2 }

        // T-11
        XCTAssertEqual(owners.session.phase, .resuming)
        XCTAssertTrue(owners.session.isTransitioning)
        XCTAssertFalse(owners.session.controlsEnabled)
        XCTAssertEqual(owners.session.partialText, "")
        XCTAssertEqual(owners.session.transcriberStatus, connectingToMeta)
        XCTAssertEqual(transcriber.starts[1].streamOffsetSeconds, 3, "The offset is the elapsed time (AC-IOS-041).")
        XCTAssertNotEqual(transcriber.starts[1].generation, transcriber.starts[0].generation)

        // The new generation's events apply while it connects; the old generation's are dropped.
        transcriber.emit(.usage(.begin(id: UUID())))
        transcriber.emit(.segment(segment("Later words", at: 3)))
        transcriber.emit(.partial("stale"), generation: transcriber.starts[0].generation)
        transcriber.releaseStart()
        await resuming.value

        // T-12
        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertEqual(owners.session.listeningStartedCount, 2)
        XCTAssertNil(owners.session.notice)
        XCTAssertFalse(owners.session.pausedAutomatically)
        XCTAssertEqual(owners.session.partialText, "")
        XCTAssertTrue(owners.session.controlsEnabled)
        XCTAssertEqual(transcriber.calls, ["start", "finalize", "start"])
        await owners.session.waitUntilSettled()
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.segments.map(\.text), ["Later words"])
        XCTAssertEqual(stored?.segments.first?.startSeconds, 3)
        XCTAssertEqual(stored?.transcriptionUsage?.streams.count, 2, "The usage gained one stream.")
    }

    @MainActor
    func testT12ResumeAfterAnAutomaticPauseClearsTheNotice() async throws {
        let owners = TestOwners()
        let transcriber = try await owners.startListening()
        transcriber.emit(.failed(TranscriberFailureText.routeChanged))
        await owners.session.waitUntilSettled()
        XCTAssertTrue(owners.session.pausedAutomatically)
        XCTAssertNotNil(owners.session.notice)

        await owners.session.togglePause()

        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertNil(owners.session.notice)
        XCTAssertFalse(owners.session.pausedAutomatically)
        XCTAssertEqual(owners.session.listeningStartedCount, 2)
        XCTAssertEqual(owners.session.transcriberStatus, connectingToMeta)
    }

    @MainActor
    func testT13AFailedResumeStaysPausedWithANotice() async throws {
        let owners = TestOwners()
        let transcriber = try await owners.startListening()
        await owners.session.togglePause()
        transcriber.startResult = .failure(TranscriberFailureText.noMicrophone)

        await owners.session.togglePause()

        XCTAssertEqual(owners.session.phase, .paused)
        XCTAssertEqual(
            owners.session.notice,
            SessionNotice(kind: .recordingPaused, message: "Transcription could not start. No usable microphone.")
        )
        XCTAssertEqual(owners.session.notice?.title, "Recording paused")
        XCTAssertTrue(owners.session.pausedAutomatically)
        XCTAssertEqual(owners.session.transcriberStatus, disconnected)
        XCTAssertNil(owners.app.alertMessage)
        XCTAssertEqual(owners.session.listeningStartedCount, 1)
        XCTAssertTrue(owners.session.controlsEnabled)
        XCTAssertEqual(transcriber.starts.count, 2)

        // The generation of the failed start is retired.
        transcriber.emit(.partial("too late"))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.partialText, "")

        // Resume can be tried again.
        transcriber.startResult = .success(())
        await owners.session.togglePause()
        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertNil(owners.session.notice)
        XCTAssertFalse(owners.session.pausedAutomatically)
    }

    // MARK: - T-14, T-15, T-16

    @MainActor
    func testT14AndT16StopFromListeningFinalizesSavesAndEnds() async throws {
        let owners = TestOwners(answers: .openrouter)
        let transcriber = try await owners.startListening()
        let id = try XCTUnwrap(owners.session.session?.id)
        let first = "This first sentence is clearly longer than forty-eight characters in total."
        try await owners.say(first)
        let stream = UUID()
        transcriber.emit(.usage(.begin(id: stream)))
        transcriber.emit(.usage(.update(id: stream, sentMs: 61_000, processedMs: 61_000, hasTranscript: true)))
        transcriber.segmentsOnFinalize = ["Final words"]
        await owners.tick()
        owners.app.selectedTab = .history
        owners.app.sessionPath = [.assistantLab]

        await owners.session.stop()

        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertNil(owners.session.session)
        XCTAssertNil(owners.session.provider)
        XCTAssertNil(owners.session.estimatedCost)
        XCTAssertFalse(owners.session.showsLiveScreen)
        XCTAssertFalse(owners.session.controlsEnabled)
        XCTAssertFalse(owners.activity.isActive)
        XCTAssertEqual(transcriber.calls, ["start", "finalize", "shutdown"])
        XCTAssertEqual(owners.app.selectedTab, .live)
        XCTAssertEqual(owners.app.sessionPath, [])
        XCTAssertNil(owners.app.alertMessage)
        await owners.waitUntil("the tick stopped") { owners.clock.sleeperCount == 0 }

        let found = await owners.stored(id)
        let stored = try XCTUnwrap(found)
        XCTAssertEqual(stored.endedAt, owners.clock.now())
        // AC-IOS-076
        XCTAssertEqual(stored.title, String(first.prefix(48)))
        XCTAssertEqual(stored.title.count, 48)
        XCTAssertEqual(stored.segments.map(\.text), [first, "Final words"])
        XCTAssertEqual(stored.transcriptionUsage?.estimatedSeconds, 61)
        XCTAssertEqual(owners.history.sessions.map(\.id), [id], "History was refreshed.")
        XCTAssertEqual(owners.history.sessions.first?.title, stored.title)
    }

    @MainActor
    func testT15StopFromPausedDoesNotFinalizeAgain() async throws {
        // IOS-B7, AC-IOS-077
        let owners = TestOwners(answers: .openrouter)
        let transcriber = try await owners.startListening()
        let id = try XCTUnwrap(owners.session.session?.id)
        await owners.session.togglePause()
        XCTAssertEqual(transcriber.calls, ["start", "finalize"])

        await owners.session.stop()

        XCTAssertEqual(transcriber.calls, ["start", "finalize", "shutdown"])
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertNil(owners.session.session)
        XCTAssertFalse(owners.activity.isActive)
        let found = await owners.stored(id)
        let stored = try XCTUnwrap(found)
        XCTAssertNotNil(stored.endedAt)
        XCTAssertEqual(stored.title, "Conversation", "A session without segments (AC-IOS-076).")
        XCTAssertEqual(owners.history.sessions.map(\.id), [id])
    }

    @MainActor
    func testT16AFailedFinalizeOnStopIsAnAlertAfterTheEnd() async throws {
        // AC-IOS-047, as ruled by R-27: on Stop the alert, shown on Home.
        let owners = TestOwners(answers: .openrouter)
        let transcriber = try await owners.startListening()
        let id = try XCTUnwrap(owners.session.session?.id)
        try await owners.say("Something was said")
        transcriber.finalizeResult = .failure(TranscriberFailureText.finalizeFailed)

        await owners.session.stop()

        XCTAssertEqual(owners.session.phase, .idle, "The transition still completes.")
        XCTAssertNil(owners.session.session)
        XCTAssertEqual(owners.app.alertMessage, finalizeText)
        XCTAssertEqual(transcriber.calls, ["start", "finalize", "shutdown"])
        let stored = await owners.stored(id)
        XCTAssertNotNil(stored?.endedAt)
        XCTAssertEqual(stored?.title, "Something was said")
    }

    // MARK: - T-17

    @MainActor
    func testT17TheTickCountsElapsedAndSavesAlsoWhilePaused() async throws {
        let owners = TestOwners(answers: .openrouter)
        let transcriber = try await owners.startListening()
        let id = try XCTUnwrap(owners.session.session?.id)
        let stream = UUID()
        transcriber.emit(.usage(.begin(id: stream)))
        transcriber.emit(.usage(.update(id: stream, sentMs: 2_000, processedMs: nil, hasTranscript: false)))
        await owners.session.waitUntilSettled()

        // A usage event shows in the cost at once, and is saved by the next tick.
        XCTAssertEqual(owners.session.estimatedCost, "$0.00010 USD")
        var stored = await owners.stored(id)
        XCTAssertEqual(stored?.transcriptionUsage?.streams.count, 0)

        await owners.tick()
        XCTAssertEqual(owners.session.elapsedSeconds, 1)
        stored = await owners.stored(id)
        XCTAssertEqual(stored?.transcriptionUsage?.streams.first?.sentMs, 2_000, "The tick saves (AC-IOS-154).")

        await owners.tick()
        XCTAssertEqual(owners.session.elapsedSeconds, 2)

        // AC-IOS-030: the clock does not stop while paused.
        await owners.session.togglePause()
        await owners.tick()
        await owners.tick()
        XCTAssertEqual(owners.session.elapsedSeconds, 4)
        XCTAssertEqual(owners.session.phase, .paused)
    }

    // MARK: - T-18, T-19, T-20

    @MainActor
    func testT18EventsOfTheCurrentGenerationAreApplied() async throws {
        let owners = TestOwners()
        let transcriber = try await owners.startListening()
        let id = try XCTUnwrap(owners.session.session?.id)

        transcriber.emit(.status("First words \u{00B7} 0.42 s from stream start"))
        transcriber.emit(.partial("Hello th"))
        transcriber.emit(.level(0.25))
        transcriber.emit(.segment(segment("Second", at: 5)))
        transcriber.emit(.segment(segment("First", at: 2)))
        transcriber.emit(.segment(segment("Also second", at: 5)))
        await owners.session.waitUntilSettled()

        XCTAssertEqual(owners.session.transcriberStatus, "First words \u{00B7} 0.42 s from stream start")
        XCTAssertEqual(owners.session.partialText, "Hello th")
        XCTAssertEqual(owners.session.level, 0.25)
        // A streaming provider's segments are sorted by start time, stably.
        XCTAssertEqual(owners.session.session?.segments.map(\.text), ["First", "Second", "Also second"])
        // A committed segment is saved.
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.segments.map(\.text), ["First", "Second", "Also second"])
        XCTAssertEqual(owners.session.phase, .listening)
    }

    @MainActor
    func testT18OnDeviceSegmentsKeepTheirArrivalOrder() async throws {
        let owners = TestOwners(provider: .voz)
        let transcriber = try await owners.startListening()

        transcriber.emit(.segment(segment("Heard first", at: 4)))
        transcriber.emit(.segment(segment("Heard second", at: 0)))
        await owners.session.waitUntilSettled()

        XCTAssertEqual(owners.session.session?.segments.map(\.text), ["Heard first", "Heard second"])
        XCTAssertNil(owners.session.estimatedCost)
    }

    @MainActor
    func testT18ALevelIsAppliedOnlyWhileListening() async throws {
        let owners = TestOwners(provider: .nemotron)
        await owners.gateway.setStatus(.success(PCModelStatus(state: "ready", model: "nemotron", message: "Ready")))
        let (starting, transcriber) = try await beginHeldStart(owners)

        transcriber.emit(.level(0.5))
        transcriber.emit(.status("Loading Nemotron 3.5 on PC \u{00B7} 12 s. Microphone starts when ready."))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.phase, .connecting)
        XCTAssertEqual(owners.session.level, 0, "The meter stays idle while connecting.")
        XCTAssertEqual(
            owners.session.transcriberStatus,
            "Loading Nemotron 3.5 on PC \u{00B7} 12 s. Microphone starts when ready."
        )

        transcriber.releaseStart()
        await starting.value
        transcriber.emit(.level(0.5))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.level, 0.5)
    }

    @MainActor
    func testT19EventsOfAnotherOrARetiredGenerationAreDropped() async throws {
        let owners = TestOwners()
        let transcriber = try await owners.startListening()
        let first = try XCTUnwrap(transcriber.starts.last?.generation)
        let kinds: [TranscriberEvent.Kind] = [
            .status("Stale status"),
            .partial("stale partial"),
            .segment(segment("Stale segment", at: 0)),
            .level(0.9),
            .usage(.begin(id: UUID())),
            .lastChunk(milliseconds: 5),
            .failed(TranscriberFailureText.routeChanged)
        ]

        // A generation that never was this session's.
        let stranger = UUID()
        for kind in kinds {
            transcriber.emit(kind, generation: stranger)
        }
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertEqual(owners.session.transcriberStatus, connectingToMeta)
        XCTAssertEqual(owners.session.partialText, "")
        XCTAssertEqual(owners.session.session?.segments.count, 0)
        XCTAssertEqual(owners.session.level, 0)
        XCTAssertNil(owners.session.notice)

        // After Pause the generation is retired.
        await owners.session.togglePause()
        for kind in kinds {
            transcriber.emit(kind, generation: first)
        }
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.phase, .paused)
        XCTAssertEqual(owners.session.transcriberStatus, finishing)
        XCTAssertEqual(owners.session.partialText, "")
        XCTAssertEqual(owners.session.session?.segments.count, 0)
        XCTAssertNil(owners.session.notice)
        XCTAssertFalse(owners.session.pausedAutomatically)

        // After Resume the first generation is still not the current one.
        await owners.session.togglePause()
        for kind in kinds {
            transcriber.emit(kind, generation: first)
        }
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.phase, .listening)
        XCTAssertEqual(owners.session.partialText, "")
        XCTAssertEqual(owners.session.session?.segments.count, 0)
        XCTAssertNil(owners.session.notice)
        XCTAssertEqual(owners.session.session?.transcriptionUsage?.streams.count, 0)
    }

    @MainActor
    func testT20AFailureWhileAStartIsInProgressLeavesTheOutcomeToTheStart() async throws {
        // Connecting: the start throws, so the outcome is T-05 with the start's own reason.
        let owners = TestOwners()
        owners.transcribers.configure = { transcriber in
            transcriber.startResult = .failure(TranscriberFailureText.noMicrophone)
        }
        let (starting, transcriber) = try await beginHeldStart(owners)
        transcriber.emit(.failed(TranscriberFailureText.routeChanged))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.phase, .connecting, "The failure event changed nothing.")
        XCTAssertNil(owners.session.notice)

        transcriber.releaseStart()
        await starting.value
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertEqual(owners.app.alertMessage, "Transcription could not start. No usable microphone.")

        // Resuming: the start throws, so the outcome is T-13 with the start's own reason.
        let second = TestOwners()
        let resumed = try await second.startListening()
        await second.session.togglePause()
        resumed.holdStart = true
        resumed.startResult = .failure(TranscriberFailureText.noMicrophone)
        let resuming = Task { await second.session.togglePause() }
        await second.waitUntil("the transcriber was started again") { resumed.starts.count == 2 }
        resumed.emit(.failed(TranscriberFailureText.routeChanged))
        await second.session.waitUntilSettled()
        XCTAssertEqual(second.session.phase, .resuming)
        XCTAssertNil(second.session.notice)

        resumed.releaseStart()
        await resuming.value
        XCTAssertEqual(second.session.phase, .paused)
        XCTAssertEqual(second.session.notice?.message, "Transcription could not start. No usable microphone.")
    }

    @MainActor
    func testAFailureThatArrivesAsTheStartReturnsIsNotLost() async throws {
        // A stream can fail between `start` returning and the barrier: that `failed` is of the
        // generation that is about to listen, and it pauses the session as T-09 says.
        let owners = TestOwners()
        let (starting, transcriber) = try await beginHeldStart(owners)
        let id = try XCTUnwrap(owners.session.session?.id)
        transcriber.emit(.failed(TranscriberFailureText.uploadOverflow))
        transcriber.releaseStart()
        await starting.value
        await owners.session.waitUntilSettled()

        XCTAssertEqual(owners.session.phase, .paused)
        XCTAssertTrue(owners.session.pausedAutomatically)
        XCTAssertEqual(owners.session.notice?.message, "Network cannot keep up. Recording paused; resume to retry.")
        XCTAssertEqual(owners.session.listeningStartedCount, 1)
        XCTAssertNil(owners.app.alertMessage)
        let stored = await owners.stored(id)
        XCTAssertNotNil(stored, "The session was connected, so it was saved.")

        // The same on Resume, for an on-device provider: T-10 follows T-12.
        let second = TestOwners(provider: .parakeet)
        let resumed = try await second.startListening()
        await second.session.togglePause()
        resumed.holdStart = true
        let resuming = Task { await second.session.togglePause() }
        await second.waitUntil("the transcriber was started again") { resumed.starts.count == 2 }
        resumed.emit(.failed(TranscriberFailureText.processingFailed))
        resumed.releaseStart()
        await resuming.value
        await second.waitUntil("the session paused") { second.session.phase == .paused }

        XCTAssertEqual(second.session.notice?.message, "Transcription stopped on this iPhone. Resume to continue.")
        XCTAssertTrue(second.session.pausedAutomatically)
        XCTAssertEqual(resumed.calls, ["start", "finalize", "start", "finalize"])
    }

    // MARK: - T-21 and the controls

    @MainActor
    func testT21AssistIsAnOverlayThatBlocksTheControls() async throws {
        let owners = TestOwners()
        await owners.offerCodexCatalogue()
        let transcriber = try await owners.startListening()
        try await owners.say("What is the capital of France?")
        await owners.codex.setHoldAnswer(true)

        let assisting = Task { await owners.session.assist() }
        await owners.codex.waitForAnswerRequests(count: 1)

        // AC-IOS-042
        XCTAssertEqual(owners.session.phase, .listening, "The phase does not change.")
        XCTAssertTrue(owners.session.isAssisting)
        XCTAssertFalse(owners.session.isTransitioning)
        XCTAssertFalse(owners.session.controlsEnabled)
        XCTAssertTrue(owners.activity.isAssisting)

        // Every intent returns at once.
        await owners.session.togglePause()
        XCTAssertEqual(owners.session.phase, .listening)
        await owners.session.stop()
        XCTAssertNotNil(owners.session.session, "Stop does not abort an Assist.")
        await owners.session.assist()
        await owners.session.retrySameText()
        let requests = await owners.codex.answerRequests
        XCTAssertEqual(requests.count, 1)
        XCTAssertEqual(transcriber.calls, ["start"])

        // A stream failure still pauses the session, and the Assist goes on (T-09).
        transcriber.emit(.failed(TranscriberFailureText.routeChanged))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(owners.session.phase, .paused)
        XCTAssertTrue(owners.session.isAssisting)
        XCTAssertFalse(owners.session.controlsEnabled)

        await owners.codex.releaseAnswer()
        await assisting.value
        XCTAssertFalse(owners.session.isAssisting)
        XCTAssertFalse(owners.activity.isAssisting)
        XCTAssertTrue(owners.session.controlsEnabled)
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 1)
    }

    @MainActor
    func testIntentsDoNothingWhileTransitioningOrIdle() async throws {
        let owners = TestOwners()
        await owners.offerCodexCatalogue()

        // Idle: there is no session to pause, to assist or to stop.
        await owners.session.togglePause()
        await owners.session.assist()
        await owners.session.retrySameText()
        await owners.session.stop()
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertNil(owners.session.assistError)

        // Connecting (AC-IOS-042).
        let (starting, transcriber) = try await beginHeldStart(owners)
        XCTAssertFalse(owners.session.controlsEnabled)
        await owners.session.start()
        await owners.session.togglePause()
        await owners.session.assist()
        await owners.session.retrySameText()
        await owners.session.stop()
        XCTAssertEqual(owners.session.phase, .connecting)
        XCTAssertNotNil(owners.session.session)
        XCTAssertFalse(owners.session.isAssisting)
        XCTAssertEqual(transcriber.calls, ["start"])
        XCTAssertEqual(owners.transcribers.made.count, 1)
        let catalogueCalls = await owners.codex.catalogueCalls
        XCTAssertEqual(catalogueCalls, 0)

        transcriber.releaseStart()
        await starting.value
        XCTAssertTrue(owners.session.controlsEnabled)
    }
}
