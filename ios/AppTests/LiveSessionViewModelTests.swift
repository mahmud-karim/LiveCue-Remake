import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Live chip, preview and feed states.
final class LiveSessionViewModelTests: XCTestCase {
    private let question = "What is the capital of France?"
    private let unavailableText = "OpenRouter could not complete this request. Check your connection or try again later. No fallback service was used."

    @MainActor
    private func makeModel(_ owners: TestOwners) -> LiveSessionViewModel {
        LiveSessionViewModel(session: owners.session, assistant: owners.assistant, clipboard: owners.clipboard, toasts: owners.toasts)
    }

    /// A listening session with Codex answers that can be given and one committed segment.
    @MainActor
    private func codexSession() async throws -> (owners: TestOwners, model: LiveSessionViewModel) {
        let owners = TestOwners()
        await owners.offerCodexCatalogue()
        _ = try await owners.startListening()
        try await owners.say(question)
        return (owners, makeModel(owners))
    }

    /// A listening session with OpenRouter answers that can be given and one committed segment.
    @MainActor
    private func openRouterSession() async throws -> (owners: TestOwners, model: LiveSessionViewModel) {
        let owners = TestOwners(answers: .openrouter)
        try owners.prepareOpenRouter()
        _ = try await owners.startListening()
        try await owners.say(question)
        return (owners, makeModel(owners))
    }

    private func reply(_ answer: String) -> AssistResponse {
        AssistResponse(
            detectedQuestion: AssistantTurn.latestConversation,
            answer: answer,
            details: "",
            memory: SessionMemory(summary: "A summary after \(answer)")
        )
    }

    // MARK: - State chip and level meter (AC-IOS-029 as changed)

    @MainActor
    func testEveryPhaseHasItsChipAndMeter() async {
        XCTAssertEqual(LiveSessionViewModel.chip(for: .connecting), StatusChipModel("Connecting…", tone: .live, mark: .spinner))
        XCTAssertEqual(LiveSessionViewModel.chip(for: .resuming), StatusChipModel("Connecting…", tone: .live, mark: .spinner))
        XCTAssertEqual(LiveSessionViewModel.chip(for: .listening), StatusChipModel("Listening", tone: .live, mark: .pulsingDot))
        XCTAssertEqual(LiveSessionViewModel.chip(for: .pausing), StatusChipModel("Pausing…", tone: .warning, mark: .spinner))
        XCTAssertEqual(LiveSessionViewModel.chip(for: .paused), StatusChipModel("Paused", tone: .warning, mark: .pause))
        XCTAssertEqual(LiveSessionViewModel.chip(for: .ending), StatusChipModel("Stopping…", tone: .neutral, mark: .spinner))

        XCTAssertEqual(LiveSessionViewModel.meterState(for: .connecting), .idle)
        XCTAssertEqual(LiveSessionViewModel.meterState(for: .resuming), .idle)
        XCTAssertEqual(LiveSessionViewModel.meterState(for: .listening), .listening)
        XCTAssertEqual(LiveSessionViewModel.meterState(for: .pausing), .paused)
        XCTAssertEqual(LiveSessionViewModel.meterState(for: .paused), .paused)
        XCTAssertEqual(LiveSessionViewModel.meterState(for: .ending), .paused)
    }

    @MainActor
    func testTheChipFollowsTheSession() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        owners.transcribers.configure = { $0.holdStart = true }

        // Connecting: the transcriber has not answered yet.
        let starting = Task { await owners.session.start() }
        await owners.waitUntil("the session is connecting") { owners.session.phase == .connecting }
        XCTAssertEqual(model.stateChip.text, "Connecting…")
        XCTAssertEqual(model.meterState, .idle)
        XCTAssertTrue(model.isTransitioning)
        XCTAssertFalse(model.isPaused)
        XCTAssertEqual(model.statusLine, "Connecting to Meta Muse · cloud…")
        XCTAssertEqual(model.preview, .placeholder("Listening starts when the connection is ready."))

        let transcriber = try XCTUnwrap(owners.transcribers.made.last)
        transcriber.holdStart = false
        transcriber.releaseStart()
        await starting.value

        // Listening.
        XCTAssertEqual(model.stateChip, StatusChipModel("Listening", tone: .live, mark: .pulsingDot))
        XCTAssertEqual(model.meterState, .listening)
        XCTAssertFalse(model.isTransitioning)
        XCTAssertEqual(model.preview, .placeholder("Listening for speech…"))

        transcriber.emit(.level(0.4))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(model.level, 0.4)

        // Paused by the user.
        await model.togglePause()
        XCTAssertEqual(model.stateChip, StatusChipModel("Paused", tone: .warning, mark: .pause))
        XCTAssertEqual(model.meterState, .paused)
        XCTAssertTrue(model.isPaused)
        XCTAssertEqual(model.level, 0)
        XCTAssertNil(model.notice)

        // Resumed.
        await model.togglePause()
        XCTAssertEqual(model.stateChip.text, "Listening")
        XCTAssertFalse(model.isPaused)

        // Stopped: the session is gone.
        await model.stop()
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertFalse(owners.session.showsLiveScreen)
    }

    @MainActor
    func testAStreamFailurePausesWithANotice() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        let transcriber = try await owners.startListening()

        transcriber.emit(.failed(TranscriberFailureText.uploadOverflow))
        await owners.session.waitUntilSettled()

        XCTAssertEqual(model.stateChip.text, "Paused")
        XCTAssertTrue(model.isPaused)
        XCTAssertEqual(
            model.notice,
            SessionNotice(kind: .recordingPaused, message: "Network cannot keep up. Recording paused; resume to retry.")
        )
        XCTAssertEqual(model.notice?.title, "Recording paused")
        XCTAssertEqual(model.statusLine, "Disconnected · resume to retry")
        XCTAssertNil(owners.app.alertMessage, "A failure during a session is a notice, not an alert.")

        await model.togglePause()
        XCTAssertNil(model.notice, "The notice goes when Resume succeeds.")
        XCTAssertEqual(model.stateChip.text, "Listening")
    }

    // MARK: - Status panel

    @MainActor
    func testElapsedTimeAsDigitsAndAsWords() async throws {
        XCTAssertEqual(LiveSessionViewModel.elapsedText(seconds: 0), "00:00")
        XCTAssertEqual(LiveSessionViewModel.elapsedText(seconds: 252), "04:12")
        XCTAssertEqual(LiveSessionViewModel.elapsedText(seconds: 3_661), "61:01")
        XCTAssertEqual(LiveSessionViewModel.elapsedText(seconds: -4), "00:00")

        XCTAssertEqual(LiveSessionViewModel.spokenElapsed(seconds: 0), "0 seconds")
        XCTAssertEqual(LiveSessionViewModel.spokenElapsed(seconds: 1), "1 second")
        XCTAssertEqual(LiveSessionViewModel.spokenElapsed(seconds: 12), "12 seconds")
        XCTAssertEqual(LiveSessionViewModel.spokenElapsed(seconds: 61), "1 minute 1 second")
        XCTAssertEqual(LiveSessionViewModel.spokenElapsed(seconds: 252), "4 minutes 12 seconds")

        let owners = TestOwners()
        let model = makeModel(owners)
        _ = try await owners.startListening()
        XCTAssertEqual(model.elapsedText, "00:00")

        await owners.tick()
        await owners.tick()
        XCTAssertEqual(model.elapsedText, "00:02")
        XCTAssertEqual(model.elapsedSpokenText, "2 seconds")
    }

    @MainActor
    func testTheProviderLineAndTheCostAreForTheSessionsProvider() async throws {
        let meta = TestOwners()
        let metaModel = makeModel(meta)
        XCTAssertEqual(metaModel.providerLine, "", "There is no provider without a session.")
        XCTAssertNil(metaModel.costText)

        let transcriber = try await meta.startListening()
        XCTAssertEqual(metaModel.providerLine, "Meta Muse · cloud · est. transcription")
        XCTAssertEqual(metaModel.costText, "$0.00000 USD")

        // Three seconds of audio at the Meta rate.
        let stream = UUID()
        transcriber.emit(.usage(.begin(id: stream)))
        transcriber.emit(.usage(.update(id: stream, sentMs: 3_000, processedMs: 3_000, hasTranscript: true)))
        await meta.session.waitUntilSettled()
        XCTAssertEqual(metaModel.costText, "$0.00015 USD")

        let voz = TestOwners(provider: .voz, answers: .openrouter)
        let vozModel = makeModel(voz)
        _ = try await voz.startListening()
        XCTAssertEqual(vozModel.providerLine, "Voz · iPhone")
        XCTAssertNil(vozModel.costText, "Only Meta has an estimated cost.")
        XCTAssertEqual(vozModel.preview, .placeholder("Recording locally. Tap Assist to transcribe."))
    }

    @MainActor
    func testTheStatusLineIsTheTranscribersStatus() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        let transcriber = try await owners.startListening()

        transcriber.emit(.status("First words · 1.20 s from stream start"))
        await owners.session.waitUntilSettled()

        XCTAssertEqual(model.statusLine, "First words · 1.20 s from stream start")
    }

    @MainActor
    func testTheModelTextIsWhatFollowsTheLastSlash() async {
        // AC-IOS-038
        let codex = makeModel(TestOwners())
        XCTAssertEqual(codex.assistantModelText, "gpt-5.6-sol")

        let owners = TestOwners(answers: .openrouter)
        let model = makeModel(owners)
        XCTAssertEqual(model.assistantModelText, "Choose model")

        let nested = AssistantModelOption(id: "vendor/family/model-x", name: "Model X", reasoningEfforts: ["default"])
        owners.assistant.adoptCatalogue(FixtureValues.openRouterCatalogue + [nested])
        owners.assistant.selectOpenRouterModel(FixtureValues.openRouterCatalogue[0])
        XCTAssertEqual(model.assistantModelText, "direct")
        owners.assistant.selectOpenRouterModel(nested)
        XCTAssertEqual(model.assistantModelText, "model-x")
    }

    // MARK: - Transcript (AC-IOS-027)

    @MainActor
    func testThePreviewPrefersInterimTextThenTheLastSegment() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        let transcriber = try await owners.startListening()
        XCTAssertEqual(model.preview, .placeholder("Listening for speech…"))
        XCTAssertTrue(model.segments.isEmpty)

        try await owners.say("The main risk is the onboarding flow.")
        try await owners.say("Design says they need one more week.", at: 4)
        XCTAssertEqual(model.preview, .committed("Design says they need one more week."))
        XCTAssertEqual(model.segments.map(\.text), ["The main risk is the onboarding flow.", "Design says they need one more week."])

        transcriber.emit(.partial("and could the dashboard"))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(model.preview, .interim("and could the dashboard"))
        XCTAssertEqual(model.partialText, "and could the dashboard")

        transcriber.emit(.partial(""))
        await owners.session.waitUntilSettled()
        XCTAssertEqual(model.preview, .committed("Design says they need one more week."))
    }

    @MainActor
    func testTheTranscriptIsCollapsedWhenASessionStarts() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        XCTAssertFalse(model.isTranscriptExpanded)

        _ = try await owners.startListening()
        XCTAssertFalse(model.isTranscriptExpanded)
        model.isTranscriptExpanded = true
        XCTAssertTrue(model.isTranscriptExpanded)
        model.isTranscriptExpanded = false
        XCTAssertFalse(model.isTranscriptExpanded)
        model.isTranscriptExpanded = true

        await model.stop()
        XCTAssertFalse(model.isTranscriptExpanded)
        _ = try await owners.startListening()
        XCTAssertFalse(model.isTranscriptExpanded, "A new session starts with the transcript collapsed.")
    }

    // MARK: - Feed (AC-IOS-050, 072)

    @MainActor
    func testTheFeedIsEmptyUntilSomethingIsAsked() async throws {
        let (owners, model) = try await codexSession()

        XCTAssertTrue(model.showsEmptyState)
        XCTAssertNil(model.errorText)
        XCTAssertNil(model.inProgress)
        XCTAssertNil(model.latestTurn)
        XCTAssertTrue(model.earlierTurns.isEmpty)
        XCTAssertFalse(model.canRetry, "Nothing was asked yet.")
        XCTAssertFalse(model.isAssisting)
        XCTAssertTrue(owners.session.controlsEnabled)
    }

    @MainActor
    func testOneAnswerIsLatestAndTheOthersAreEarlier() async throws {
        let (owners, model) = try await codexSession()
        var triggers: [Int] = [model.feedScrollTrigger]

        for answer in ["First answer.", "Second answer.", "Third answer."] {
            var response = AssistResponse(
                detectedQuestion: "Fake detected question",
                answer: answer,
                details: "",
                memory: SessionMemory(summary: "A summary after \(answer)")
            )
            response.execution = RelayExecution(
                model: "gpt-5.6-sol",
                reasoningEffort: "low",
                codexMs: 600,
                relayTotalMs: 620,
                requestReadMs: 2,
                relayOverheadMs: 20
            )
            await owners.codex.setAnswer(.success(response), progress: [])
            await model.assist()
            triggers.append(model.feedScrollTrigger)
        }

        XCTAssertNil(model.errorText)
        XCTAssertEqual(model.latestTurn?.answer, "Third answer.")
        XCTAssertEqual(model.earlierTurns.map(\.answer), ["Second answer.", "First answer."], "Earlier answers are newest first.")
        XCTAssertFalse(model.showsEmptyState)
        XCTAssertNil(model.inProgress)
        XCTAssertTrue(model.canRetry)
        XCTAssertFalse(model.isAssisting)
        XCTAssertEqual(Set(triggers).count, triggers.count, "The feed goes to the top for every answer.")

        // A Codex answer shows its detected question (AC-IOS-072).
        let latest = try XCTUnwrap(model.latestTurn)
        XCTAssertEqual(AnswerFormat.visibleQuestion(of: latest), "Fake detected question")
    }

    @MainActor
    func testAnOpenRouterAnswerHidesItsQuestion() async throws {
        // AC-IOS-072
        let (owners, model) = try await openRouterSession()
        await owners.openRouter.setAnswer(.success(reply("A direct answer.")), progress: [])

        await model.assist()

        let latest = try XCTUnwrap(model.latestTurn)
        XCTAssertEqual(latest.answer, "A direct answer.")
        XCTAssertEqual(latest.detectedQuestion, "Latest conversation")
        XCTAssertNil(AnswerFormat.visibleQuestion(of: latest), "The sentinel is never shown.")

        let empty = AssistantTurn(detectedQuestion: "", answer: "An answer.", details: "")
        XCTAssertNil(AnswerFormat.visibleQuestion(of: empty))
    }

    @MainActor
    func testARunningAssistIsTheCardInProgress() async throws {
        let (owners, model) = try await openRouterSession()
        await owners.openRouter.setAnswer(.success(reply("Your audio stays on the iPhone.")), progress: ["Your audio"])
        await owners.openRouter.setHoldAnswer(true)
        let triggerBefore = model.feedScrollTrigger

        let assisting = Task { await model.assist() }
        await owners.waitUntil("the first text streamed in") { owners.session.streamingAnswer == "Your audio" }

        XCTAssertEqual(
            model.inProgress,
            InProgressState(label: "Replying…", text: "Your audio", showsSpinner: true, isIncomplete: false)
        )
        XCTAssertTrue(model.isAssisting)
        XCTAssertFalse(model.canRetry, "No retry while an Assist runs.")
        XCTAssertFalse(model.showsEmptyState)
        XCTAssertNil(model.errorText)
        XCTAssertNotEqual(model.feedScrollTrigger, triggerBefore, "The feed goes to the top when an Assist starts.")

        await owners.openRouter.releaseAnswer()
        await assisting.value

        XCTAssertNil(model.inProgress)
        XCTAssertEqual(model.latestTurn?.answer, "Your audio stays on the iPhone.")
        XCTAssertFalse(model.isAssisting)
    }

    @MainActor
    func testAFailedAssistKeepsItsTextAsIncomplete() async throws {
        // AC-IOS-057, 058
        let (owners, model) = try await openRouterSession()
        await owners.openRouter.setAnswer(.failure(OpenRouterError.unavailable), progress: ["The smallest cut is"])
        let triggerBefore = model.feedScrollTrigger

        await model.assist()

        XCTAssertEqual(model.errorText, unavailableText)
        XCTAssertEqual(
            model.inProgress,
            InProgressState(label: "Incomplete answer", text: "The smallest cut is", showsSpinner: false, isIncomplete: true)
        )
        XCTAssertNil(model.latestTurn, "A failed Assist saves no answer.")
        XCTAssertTrue(model.earlierTurns.isEmpty)
        XCTAssertFalse(model.showsEmptyState)
        XCTAssertTrue(model.canRetry)
        XCTAssertNotEqual(model.feedScrollTrigger, triggerBefore, "The feed goes to the top when an error appears.")
        XCTAssertNil(owners.app.alertMessage)
        XCTAssertTrue(owners.session.controlsEnabled)

        await owners.openRouter.setAnswer(.success(reply("The referral banner.")), progress: [])
        await model.retry()

        XCTAssertNil(model.errorText)
        XCTAssertNil(model.inProgress)
        XCTAssertEqual(model.latestTurn?.answer, "The referral banner.")
        XCTAssertTrue(model.earlierTurns.isEmpty, "Exactly one answer exists after the retry.")
    }

    @MainActor
    func testAPreconditionIsAnErrorCardWithoutAnAnswerInProgress() async throws {
        // AC-IOS-055
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        _ = try await owners.startListening()
        try await owners.say(question)

        await model.assist()

        XCTAssertEqual(model.errorText, "Pair this iPhone with the Windows relay first.")
        XCTAssertNil(model.inProgress)
        XCTAssertFalse(model.canRetry, "Nothing was sent, so there is nothing to retry.")
        XCTAssertFalse(model.showsEmptyState)
        XCTAssertNil(model.latestTurn)
    }

    // MARK: - Copy

    @MainActor
    func testCopyPutsTheAnswerOnTheClipboardAndSaysSo() async throws {
        let (owners, model) = try await codexSession()
        await model.assist()
        let latest = try XCTUnwrap(model.latestTurn)

        model.copy(latest)

        XCTAssertEqual(owners.clipboard.copies, [latest.answer])
        XCTAssertEqual(owners.toasts.message, "Copied")
    }
}
