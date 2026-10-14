import LiveCueCore
import Observation
import XCTest
@testable import LiveCueRemake

/// Records every value `assistStage` had, in order. Observation reports a change before it is made, on
/// the main actor, so each report gives the value that is about to be replaced.
@MainActor private final class StageLog {
    private var replaced: [AssistStage?] = []
    private let controller: SessionController

    init(_ controller: SessionController) {
        self.controller = controller
        observe()
    }

    /// The stages in order, without immediate repeats, ending with the current one.
    var stages: [AssistStage?] {
        var result: [AssistStage?] = []
        for stage in replaced + [controller.assistStage] where result.isEmpty || result[result.count - 1] != stage {
            result.append(stage)
        }
        return result
    }

    private func observe() {
        withObservationTracking {
            _ = controller.assistStage
        } onChange: { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.replaced.append(self.controller.assistStage)
                self.observe()
            }
        }
    }
}

/// An error no part of the app knows.
private struct UnknownFailure: Error {}

/// The thirteen steps of section 4.3 for both services.
final class SessionControllerAssistTests: XCTestCase {
    private let notPairedText = "Pair this iPhone with the Windows relay first."
    private let openRouterKeyText = "Add your OpenRouter API key in Assistant models & timing first."
    private let unavailableText = "OpenRouter could not complete this request. Check your connection or try again later. No fallback service was used."
    private let question = "What is the capital of France?"

    /// A listening session with Codex answers that can be given and one committed segment.
    @MainActor
    private func codexSession(
        provider: SpeechProvider = .meta
    ) async throws -> (owners: TestOwners, transcriber: FakeTranscriber, segment: TranscriptSegment) {
        let owners = TestOwners(provider: provider)
        await owners.offerCodexCatalogue()
        let transcriber = try await owners.startListening()
        let segment = try await owners.say(question)
        return (owners, transcriber, segment)
    }

    /// A listening session with OpenRouter answers that can be given and one committed segment.
    @MainActor
    private func openRouterSession() async throws -> (owners: TestOwners, segment: TranscriptSegment) {
        let owners = TestOwners(answers: .openrouter)
        try owners.prepareOpenRouter()
        _ = try await owners.startListening()
        let segment = try await owners.say(question)
        return (owners, segment)
    }

    private func reply(_ answer: String) -> AssistResponse {
        AssistResponse(
            detectedQuestion: AssistantTurn.latestConversation,
            answer: answer,
            details: "",
            memory: SessionMemory(summary: "A summary after \(answer)")
        )
    }

    // MARK: - Steps 1 and 2

    @MainActor
    func testStep2CodexWithoutAPairingIsReportedOnTheCard() async throws {
        // AC-IOS-055
        let owners = TestOwners(paired: false)
        await owners.offerCodexCatalogue()
        _ = try await owners.startListening()
        try await owners.say(question)

        await owners.session.assist()

        XCTAssertEqual(owners.session.assistError, notPairedText)
        XCTAssertNil(owners.app.alertMessage, "The text is inline, not an alert.")
        XCTAssertFalse(owners.session.isAssisting)
        XCTAssertFalse(owners.activity.isAssisting)
        XCTAssertNil(owners.session.assistStage)
        XCTAssertFalse(owners.session.canRetry)
        XCTAssertEqual(owners.session.assistFailedCount, 0, "The Assist did not start.")
        XCTAssertTrue(owners.session.controlsEnabled)
        let catalogueCalls = await owners.codex.catalogueCalls
        XCTAssertEqual(catalogueCalls, 0)
        let requests = await owners.codex.answerRequests
        XCTAssertTrue(requests.isEmpty)
    }

    @MainActor
    func testStep2OpenRouterWithoutAKeyIsReportedOnTheCard() async throws {
        // AC-IOS-056
        let owners = TestOwners(answers: .openrouter)
        owners.assistant.adoptCatalogue(FixtureValues.openRouterCatalogue)
        owners.assistant.selectOpenRouterModel(FixtureValues.openRouterCatalogue[0])
        _ = try await owners.startListening()
        try await owners.say(question)

        await owners.session.assist()

        XCTAssertEqual(owners.session.assistError, openRouterKeyText)
        XCTAssertTrue(owners.session.assistError?.contains("OpenRouter API key") ?? false)
        XCTAssertNil(owners.app.alertMessage)
        XCTAssertFalse(owners.session.isAssisting)
        XCTAssertNil(owners.session.assistStage)
        XCTAssertFalse(owners.session.canRetry)
        let requests = await owners.openRouter.answerRequests
        XCTAssertTrue(requests.isEmpty)
    }

    // MARK: - Step 3

    @MainActor
    func testStep3SnapshotsTheSelectionTheSessionAndTheInstruction() async throws {
        let (owners, _, _) = try await codexSession()
        let id = try XCTUnwrap(owners.session.session?.id)
        owners.assistant.instruction = "  Answer in one sentence.  "
        await owners.codex.setHoldAnswer(true)

        let assisting = Task { await owners.session.assist() }
        await owners.codex.waitForAnswerRequests(count: 1)
        XCTAssertTrue(owners.session.isAssisting)
        XCTAssertTrue(owners.activity.isAssisting)
        XCTAssertEqual(owners.session.assistStage, .thinking)

        // The selection and the instruction change while the request is out: the snapshot counts.
        owners.assistant.selectReasoningEffort("high")
        owners.assistant.instruction = "Something else"
        await owners.codex.releaseAnswer()
        await assisting.value

        let requests = await owners.codex.answerRequests
        let request = try XCTUnwrap(requests.first)
        XCTAssertEqual(request.assistant, AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "low", provider: .codex))
        XCTAssertEqual(request.instruction, "Answer in one sentence.")
        XCTAssertEqual(request.sessionId, id)
        XCTAssertEqual(request.transcript, question)
        let turn = try XCTUnwrap(owners.session.session?.assistantTurns.first)
        XCTAssertEqual(turn.request, "Answer in one sentence.", "The turn keeps the instruction that was sent.")
        XCTAssertEqual(turn.performance?.configuration.reasoningEffort, "low")
        // Step 13
        XCTAssertFalse(owners.session.isAssisting)
        XCTAssertFalse(owners.activity.isAssisting)
        XCTAssertNil(owners.session.assistStage)
    }

    // MARK: - Step 4

    @MainActor
    func testStep4VozTranscribesFirstAndTheSegmentIsInTheRequest() async throws {
        // AC-IOS-065
        let owners = TestOwners(provider: .voz)
        await owners.offerCodexCatalogue()
        let transcriber = try await owners.startListening()
        transcriber.segmentsOnTranscribePending = ["Spoken into the phone"]
        transcriber.pendingSeconds = 1.5
        await owners.codex.setHoldAnswer(true)

        let assisting = Task { await owners.session.assist() }
        await owners.codex.waitForAnswerRequests(count: 1)
        XCTAssertEqual(transcriber.calls, ["start", "transcribePending"])
        XCTAssertEqual(
            owners.session.session?.segments.map(\.text),
            ["Spoken into the phone"],
            "The new text was committed before the request was sent."
        )
        await owners.codex.releaseAnswer()
        await assisting.value

        let requests = await owners.codex.answerRequests
        XCTAssertEqual(requests.first?.transcript, "Spoken into the phone")
        let session = try XCTUnwrap(owners.session.session)
        let turn = try XCTUnwrap(session.assistantTurns.first)
        XCTAssertEqual(turn.performance?.transcriptionMs, 1_500)
        XCTAssertEqual(turn.performance?.speechModel, "voz")
        XCTAssertEqual(turn.performance?.reusedText, false)
        XCTAssertEqual(session.memory.throughSegmentId, session.segments.last?.id)
    }

    @MainActor
    func testStep4OtherProvidersDoNotTranscribeAndAVozFailureIsShown() async throws {
        let (owners, transcriber, _) = try await codexSession(provider: .parakeet)
        await owners.session.assist()
        XCTAssertEqual(transcriber.calls, ["start"], "Only Voz transcribes on Assist.")
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 1)

        // The transcription inside an Assist fails: its text is the card's, and nothing is sent.
        let (voz, vozTranscriber, _) = try await codexSession(provider: .voz)
        vozTranscriber.transcribePendingResult = .failure(TranscriberFailureText.processingFailed)
        await voz.session.assist()
        XCTAssertEqual(voz.session.assistError, "Transcription stopped on this iPhone. Resume to continue.")
        XCTAssertEqual(voz.session.assistFailedCount, 1)
        XCTAssertFalse(voz.session.isAssisting)
        XCTAssertFalse(voz.session.canRetry)
        let requests = await voz.codex.answerRequests
        XCTAssertTrue(requests.isEmpty)
    }

    // MARK: - Step 5

    @MainActor
    func testStep5NothingRecognisedIsReportedAndNothingIsSent() async throws {
        // AC-IOS-054
        let owners = TestOwners()
        await owners.offerCodexCatalogue()
        _ = try await owners.startListening()

        await owners.session.assist()

        XCTAssertEqual(owners.session.assistError, "No speech was recognized yet.")
        XCTAssertEqual(owners.session.assistFailedCount, 1)
        XCTAssertFalse(owners.session.canRetry)
        XCTAssertFalse(owners.session.isAssisting)
        XCTAssertNil(owners.session.assistStage)
        XCTAssertNil(owners.app.alertMessage)
        let catalogueCalls = await owners.codex.catalogueCalls
        XCTAssertEqual(catalogueCalls, 0)
        let requests = await owners.codex.answerRequests
        XCTAssertTrue(requests.isEmpty)
    }

    @MainActor
    func testStep5APartialAloneIsEnough() async throws {
        let owners = TestOwners()
        await owners.offerCodexCatalogue()
        let transcriber = try await owners.startListening()
        transcriber.emit(.partial("  What about tomorr  "))
        await owners.session.waitUntilSettled()

        await owners.session.assist()

        let requests = await owners.codex.answerRequests
        let request = try XCTUnwrap(requests.first)
        XCTAssertEqual(request.transcript, "")
        XCTAssertEqual(request.partialTranscript, "What about tomorr")
        let session = try XCTUnwrap(owners.session.session)
        XCTAssertEqual(session.assistantTurns.count, 1)
        XCTAssertNil(session.memory.throughSegmentId, "There is no segment to mark.")
        XCTAssertEqual(session.assistantTurns.first?.performance?.transcriptCharacters, "What about tomorr".count)
    }

    // MARK: - Step 6

    @MainActor
    func testStep6CodexFetchesTheCatalogueBeforeEveryAssist() async throws {
        // AC-IOS-062
        let (owners, _, _) = try await codexSession()

        await owners.session.assist()
        await owners.session.assist()
        await owners.session.retrySameText()

        let catalogueCalls = await owners.codex.catalogueCalls
        XCTAssertEqual(catalogueCalls, 3)
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 3)
        XCTAssertTrue(owners.assistant.catalogue.isEmpty, "The check's catalogue is not the settings screen's.")
    }

    @MainActor
    func testStep6ACodexCatalogueFailureEndsTheAssistWithItsText() async throws {
        let (owners, _, _) = try await codexSession()
        await owners.codex.setCatalogue(.failure(RelayFailure.local(.timeout)))

        await owners.session.assist()

        XCTAssertEqual(
            owners.session.assistError,
            "Your PC did not answer in time. Check that LiveCue Desktop is running, then retry."
        )
        XCTAssertEqual(owners.session.assistFailedCount, 1)
        XCTAssertFalse(owners.session.canRetry, "Nothing was remembered: step 8 was not reached.")
        XCTAssertFalse(owners.session.isAssisting)
        let requests = await owners.codex.answerRequests
        XCTAssertTrue(requests.isEmpty)
    }

    @MainActor
    func testStep6OpenRouterFetchesTheCatalogueOnlyWhenItIsEmpty() async throws {
        let (owners, _) = try await openRouterSession()

        await owners.session.assist()
        var catalogueCalls = await owners.openRouter.catalogueCalls
        XCTAssertEqual(catalogueCalls, 0, "The loaded catalogue is used.")

        // The catalogue is kept in memory only. Without one, the Assist fetches and stores it.
        owners.assistant.adoptCatalogue([])
        await owners.openRouter.setCatalogue(.success(FixtureValues.openRouterCatalogue))
        await owners.session.assist()
        catalogueCalls = await owners.openRouter.catalogueCalls
        XCTAssertEqual(catalogueCalls, 1)
        XCTAssertEqual(owners.assistant.catalogue, FixtureValues.openRouterCatalogue)

        await owners.session.assist()
        catalogueCalls = await owners.openRouter.catalogueCalls
        XCTAssertEqual(catalogueCalls, 1)
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 3)
        XCTAssertNil(owners.session.assistError)
    }

    @MainActor
    func testStep6AFailedOpenRouterCatalogueFetchShowsThatFailure() async throws {
        // IOS-B9: the failure of the fetch, not "choose a model".
        let owners = TestOwners(answers: .openrouter)
        try owners.secrets.save("fixture-openrouter-key", for: .openRouterAPIKey)
        owners.assistant.selectOpenRouterModel(FixtureValues.openRouterCatalogue[0])
        await owners.openRouter.setCatalogue(.failure(OpenRouterError.rateLimited))
        _ = try await owners.startListening()
        try await owners.say(question)

        await owners.session.assist()

        XCTAssertEqual(owners.session.assistError, "OpenRouter rate-limited this request. Wait briefly and try again.")
        XCTAssertEqual(owners.session.assistFailedCount, 1)
        XCTAssertTrue(owners.assistant.catalogue.isEmpty)
        XCTAssertEqual(owners.assistant.configuration.model, "test/direct")
        let requests = await owners.openRouter.answerRequests
        XCTAssertTrue(requests.isEmpty)
    }

    // MARK: - Step 7

    @MainActor
    func testStep7ACodexSelectionMissingFromTheCatalogueIsNamedAndLeftUnchanged() async throws {
        // AC-IOS-062
        let (owners, _, _) = try await codexSession()
        let missing = "gpt-5.6-sol / low is not in this PC's current catalog. Open Assistant models & timing and choose an available combination. Your selection has not changed."

        // The model is not offered.
        await owners.codex.setCatalogue(.success([
            AssistantModelOption(id: "gpt-6-astra", name: "GPT-6 Astra", reasoningEfforts: ["low"])
        ]))
        await owners.session.assist()
        XCTAssertEqual(owners.session.assistError, missing)

        // The model is offered, but not with this effort.
        await owners.codex.setCatalogue(.success([
            AssistantModelOption(id: "gpt-5.6-sol", name: "GPT-5.6 Sol", reasoningEfforts: ["high"])
        ]))
        await owners.session.assist()
        XCTAssertEqual(owners.session.assistError, missing)

        XCTAssertEqual(owners.assistant.configuration, AssistantConfiguration(), "The selection is never changed.")
        XCTAssertEqual(owners.session.assistFailedCount, 2)
        XCTAssertFalse(owners.session.canRetry)
        let requests = await owners.codex.answerRequests
        XCTAssertTrue(requests.isEmpty)
    }

    @MainActor
    func testStep7AnOpenRouterModelThatIsNotOfferedIsReported() async throws {
        let owners = TestOwners(answers: .openrouter)
        try owners.secrets.save("fixture-openrouter-key", for: .openRouterAPIKey)
        owners.assistant.adoptCatalogue(FixtureValues.openRouterCatalogue)
        _ = try await owners.startListening()
        try await owners.say(question)

        await owners.session.assist()

        XCTAssertEqual(owners.session.assistError, "Choose an OpenRouter model in Assistant models & timing first.")
        XCTAssertEqual(owners.assistant.configuration, AssistantConfiguration.default(for: .openrouter))
        let requests = await owners.openRouter.answerRequests
        XCTAssertTrue(requests.isEmpty)
    }

    // MARK: - Step 8

    @MainActor
    func testStep8TheRequestIsFittedAndRememberedAlsoWhenTheAnswerFails() async throws {
        let (owners, _, _) = try await codexSession()
        // Quotation marks double in JSON: this transcript fits its own budget, but not the body.
        try await owners.say(String(repeating: "\"", count: 79_000), at: 2)
        await owners.codex.setAnswer(.failure(RelayFailure.wire(.assistantFailed)), progress: [])

        await owners.session.assist()

        let requests = await owners.codex.answerRequests
        let request = try XCTUnwrap(requests.first)
        XCTAssertEqual(request.assistant, AssistantConfiguration())
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(request).count, 126_976)
        XCTAssertLessThan(request.transcript.utf8.count, 79_000, "The transcript was cut to make the body fit.")
        XCTAssertEqual(
            owners.session.assistError,
            "Codex on your PC could not finish this answer. Check that Codex is signed in on the PC, then retry."
        )
        XCTAssertTrue(owners.session.canRetry, "A request that failed is remembered too.")
        XCTAssertEqual(owners.session.rememberedAssist?.request, request)
    }

    // MARK: - Step 9

    @MainActor
    func testStep9StreamedTextIsShownWhileTheAnswerArrives() async throws {
        let (owners, _) = try await openRouterSession()
        await owners.openRouter.setAnswer(.success(reply("Paris is the capital.")), progress: ["Paris", "Paris is the"])
        await owners.openRouter.setHoldAnswer(true)

        let assisting = Task { await owners.session.assist() }
        await owners.waitUntil("the text streamed in") { owners.session.streamingAnswer == "Paris is the" }
        XCTAssertEqual(owners.session.assistStage, .replying)
        XCTAssertTrue(owners.session.isAssisting)
        XCTAssertEqual(owners.session.answerArrivedCount, 1, "Only the first text counts.")
        XCTAssertNil(owners.session.assistError)

        await owners.openRouter.releaseAnswer()
        await assisting.value

        XCTAssertEqual(owners.session.streamingAnswer, "", "The finished answer replaces the streamed text.")
        XCTAssertEqual(owners.session.answerArrivedCount, 1)
        let turn = try XCTUnwrap(owners.session.session?.assistantTurns.first)
        XCTAssertEqual(turn.answer, "Paris is the capital.")
        XCTAssertEqual(turn.detectedQuestion, "Latest conversation")
        XCTAssertEqual(owners.session.latestTurnID, turn.id)
    }

    // MARK: - Steps 10 and 11

    @MainActor
    func testStep10ACodexAnswerIsSavedWithItsMemoryAndTheAppsMarker() async throws {
        // P2-209, AC-CORE-046: the marker is the last segment when the request was built.
        let (owners, _, _) = try await codexSession()
        let id = try XCTUnwrap(owners.session.session?.id)
        let second = try await owners.say("And of Italy?", at: 4)
        var answer = AssistResponse(
            detectedQuestion: "What are the capitals?",
            answer: "Paris and Rome.",
            details: "Both are capital cities.",
            // A marker in a reply is never used.
            memory: SessionMemory(summary: "Capitals", facts: ["Paris"], openQuestions: ["Spain?"], throughSegmentId: UUID())
        )
        answer.execution = RelayExecution(
            model: "gpt-5.6-sol",
            reasoningEffort: "low",
            codexMs: 900,
            relayTotalMs: 950,
            requestReadMs: 3,
            relayOverheadMs: 50
        )
        await owners.codex.setAnswer(.success(answer), progress: [])
        await owners.codex.setHoldAnswer(true)

        let assisting = Task { await owners.session.assist() }
        await owners.codex.waitForAnswerRequests(count: 1)
        // A segment that arrives while the request is out is not covered by this answer.
        let third = try await owners.say("And of Spain?", at: 8)
        await owners.codex.releaseAnswer()
        await assisting.value

        let session = try XCTUnwrap(owners.session.session)
        XCTAssertEqual(session.assistantTurns.count, 1)
        let turn = try XCTUnwrap(session.assistantTurns.first)
        XCTAssertEqual(turn.detectedQuestion, "What are the capitals?")
        XCTAssertEqual(turn.answer, "Paris and Rome.")
        XCTAssertEqual(turn.details, "Both are capital cities.")
        XCTAssertNil(turn.request, "No instruction was sent.")
        XCTAssertEqual(turn.createdAt, owners.clock.now())
        XCTAssertEqual(turn.performance?.execution, answer.execution)
        XCTAssertEqual(
            session.memory,
            SessionMemory(summary: "Capitals", facts: ["Paris"], openQuestions: ["Spain?"], throughSegmentId: second.id)
        )
        XCTAssertEqual(owners.session.latestTurnID, turn.id)
        XCTAssertTrue(owners.session.canRetry)
        XCTAssertEqual(owners.session.streamingAnswer, "")
        XCTAssertNil(owners.session.assistError)
        XCTAssertEqual(owners.session.answerArrivedCount, 1, "A saved answer counts when no text was streamed.")
        XCTAssertEqual(owners.session.assistFailedCount, 0)

        // Saved.
        await owners.session.waitUntilSettled()
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.assistantTurns, [turn])
        XCTAssertEqual(stored?.memory.throughSegmentId, second.id)

        // The next request carries that memory, and its answer moves the marker on.
        await owners.codex.setHoldAnswer(false)
        await owners.session.assist()
        let requests = await owners.codex.answerRequests
        XCTAssertEqual(requests.last?.memory.throughSegmentId, second.id)
        XCTAssertEqual(owners.session.session?.memory.throughSegmentId, third.id)
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 2)
        XCTAssertEqual(owners.session.latestTurnID, owners.session.session?.assistantTurns.last?.id)
    }

    @MainActor
    func testStep10AnOpenRouterAnswerIsSavedWithTheAppsMarker() async throws {
        let (owners, segment) = try await openRouterSession()
        let id = try XCTUnwrap(owners.session.session?.id)

        await owners.session.assist()

        let selection = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)
        let requests = await owners.openRouter.answerRequests
        XCTAssertEqual(requests.first?.assistant, selection)
        XCTAssertEqual(requests.first?.transcript, question)
        let session = try XCTUnwrap(owners.session.session)
        let turn = try XCTUnwrap(session.assistantTurns.first)
        XCTAssertEqual(turn.answer, "Fake answer.")
        XCTAssertEqual(turn.performance?.configuration, selection)
        XCTAssertEqual(turn.performance?.speechModel, "meta")
        XCTAssertEqual(
            session.memory,
            SessionMemory(summary: "Fake memory", facts: ["A fact"], openQuestions: [], throughSegmentId: segment.id)
        )
        XCTAssertEqual(owners.session.latestTurnID, turn.id)
        XCTAssertTrue(owners.session.canRetry)
        XCTAssertEqual(owners.session.answerArrivedCount, 1)
        let codexRequests = await owners.codex.answerRequests
        XCTAssertTrue(codexRequests.isEmpty, "The other service is not asked.")
        await owners.session.waitUntilSettled()
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.assistantTurns.count, 1)
        XCTAssertEqual(stored?.memory.throughSegmentId, segment.id)
    }

    @MainActor
    func testStep11AResultForASessionThatEndedOrChangedIsDropped() async throws {
        let (owners, _, _) = try await codexSession()
        await owners.codex.setSummary(.failure(RelayFailure.local(.network)))
        let id = try XCTUnwrap(owners.session.session?.id)
        let turn = AssistantTurn(detectedQuestion: "A question", answer: "An answer", details: "")
        let memory = SessionMemory(summary: "Not this session's")

        // A result that names another session.
        let other = UUID()
        owners.session.beginAssist(stage: .thinking)
        owners.session.showStreamedAnswer("Text of another session", forSession: other)
        XCTAssertEqual(owners.session.streamingAnswer, "")
        XCTAssertEqual(owners.session.assistStage, .thinking)
        XCTAssertFalse(owners.session.saveAnswer(turn, memory: memory, forSession: other))
        owners.session.endAssist()
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 0)
        XCTAssertEqual(owners.session.session?.memory, SessionMemory())
        XCTAssertNil(owners.session.latestTurnID)
        XCTAssertEqual(owners.session.answerArrivedCount, 0)
        XCTAssertNil(owners.session.assistError, "It is dropped silently.")

        // The session ended: a late result of its own is dropped too.
        await owners.session.stop()
        owners.session.showStreamedAnswer("Late text", forSession: id)
        XCTAssertEqual(owners.session.streamingAnswer, "")
        XCTAssertFalse(owners.session.saveAnswer(turn, memory: memory, forSession: id))
        XCTAssertNil(owners.session.latestTurnID)
        await owners.session.waitUntilSettled()
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.assistantTurns.count, 0)
        XCTAssertEqual(stored?.memory, SessionMemory())
    }

    // MARK: - Steps 12 and 13

    @MainActor
    func testStep12AFailureSavesNothingAndKeepsTheStreamedText() async throws {
        // AC-IOS-057, AC-IOS-059
        let (owners, _) = try await openRouterSession()
        let id = try XCTUnwrap(owners.session.session?.id)
        await owners.openRouter.setAnswer(.failure(OpenRouterError.unavailable), progress: ["Your audio stays"])

        await owners.session.assist()

        XCTAssertEqual(owners.session.assistError, unavailableText)
        XCTAssertEqual(owners.session.streamingAnswer, "Your audio stays", "The text stays as an incomplete answer.")
        XCTAssertEqual(owners.session.assistFailedCount, 1)
        XCTAssertEqual(owners.session.answerArrivedCount, 1)
        XCTAssertNil(owners.app.alertMessage)
        let session = try XCTUnwrap(owners.session.session)
        XCTAssertTrue(session.assistantTurns.isEmpty, "No turn is saved.")
        XCTAssertEqual(session.memory, SessionMemory(), "The memory and its marker are unchanged.")
        XCTAssertNil(owners.session.latestTurnID)
        XCTAssertTrue(owners.session.canRetry)
        // Step 13: the controls are back, so Stop is tappable.
        XCTAssertFalse(owners.session.isAssisting)
        XCTAssertFalse(owners.activity.isAssisting)
        XCTAssertNil(owners.session.assistStage)
        XCTAssertTrue(owners.session.controlsEnabled)
        await owners.session.waitUntilSettled()
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.assistantTurns.count, 0)
        XCTAssertEqual(stored?.memory, SessionMemory())
    }

    @MainActor
    func testASuccessAfterAFailureClearsTheCard() async throws {
        // AC-IOS-058
        let (owners, segment) = try await openRouterSession()
        await owners.openRouter.setAnswer(.failure(OpenRouterError.unavailable), progress: ["Your audio stays"])
        await owners.session.assist()
        XCTAssertNotNil(owners.session.assistError)

        await owners.openRouter.setAnswer(.success(reply("Your audio stays on the iPhone.")), progress: [])
        await owners.openRouter.setHoldAnswer(true)
        let retrying = Task { await owners.session.retrySameText() }
        await owners.openRouter.waitForAnswerRequests(count: 2)
        // Step 1: the error and the leftover text go as soon as the retry starts.
        XCTAssertNil(owners.session.assistError)
        XCTAssertEqual(owners.session.streamingAnswer, "")
        await owners.openRouter.releaseAnswer()
        await retrying.value

        XCTAssertNil(owners.session.assistError)
        XCTAssertEqual(owners.session.streamingAnswer, "")
        let session = try XCTUnwrap(owners.session.session)
        XCTAssertEqual(session.assistantTurns.map(\.answer), ["Your audio stays on the iPhone."], "Exactly one answer.")
        XCTAssertEqual(owners.session.latestTurnID, session.assistantTurns.first?.id)
        XCTAssertEqual(session.assistantTurns.first?.performance?.reusedText, true)
        // The marker of the request that failed is applied by the retry that succeeded.
        XCTAssertEqual(session.memory.throughSegmentId, segment.id)
        let requests = await owners.openRouter.answerRequests
        XCTAssertEqual(requests.count, 2)
        XCTAssertEqual(requests.first?.transcript, requests.last?.transcript)
        XCTAssertNotEqual(requests.first?.requestId, requests.last?.requestId)
    }

    @MainActor
    func testStep12ACancellationShowsNothing() async throws {
        let (owners, _, _) = try await codexSession()
        await owners.codex.setAnswer(.failure(CancellationError()), progress: [])

        await owners.session.assist()

        XCTAssertNil(owners.session.assistError)
        XCTAssertEqual(owners.session.assistFailedCount, 0)
        XCTAssertFalse(owners.session.isAssisting)
        XCTAssertNil(owners.session.assistStage)
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 0)
    }

    @MainActor
    func testStep12EveryFailureHasAPhoneText() async throws {
        let (owners, _, _) = try await codexSession()

        // The echo rule of protocol 13.5.
        await owners.codex.setAnswer(.failure(RelayFailure.local(.notConfirmed)), progress: [])
        await owners.session.assist()
        XCTAssertEqual(
            owners.session.assistError,
            "The PC did not confirm the selected model. Restart the updated LiveCue Desktop app, then retry."
        )

        // An error nobody knows: never its own text.
        await owners.codex.setAnswer(.failure(UnknownFailure()), progress: [])
        await owners.session.assist()
        XCTAssertEqual(
            owners.session.assistError,
            "Could not reach your PC because of a network error. Check the connection and retry."
        )
        XCTAssertEqual(owners.session.assistFailedCount, 2)
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 0)
        XCTAssertNil(owners.app.alertMessage)
    }

    // MARK: - Retry

    @MainActor
    func testRetrySendsTheSameTextWithANewIdAndTheCurrentSelection() async throws {
        // AC-IOS-060, P2-209
        let (owners, _, segment) = try await codexSession()
        owners.assistant.instruction = "Be brief."
        await owners.session.assist()
        XCTAssertEqual(owners.session.session?.memory.throughSegmentId, segment.id)

        try await owners.say("Something new was said", at: 5)
        owners.assistant.selectReasoningEffort("high")
        owners.assistant.instruction = "Be thorough."
        await owners.session.retrySameText()

        let requests = await owners.codex.answerRequests
        XCTAssertEqual(requests.count, 2)
        let first = try XCTUnwrap(requests.first)
        let second = try XCTUnwrap(requests.last)
        XCTAssertNotEqual(second.requestId, first.requestId)
        XCTAssertEqual(second.sessionId, first.sessionId)
        XCTAssertEqual(second.transcript, question, "The new segment is not part of a retry.")
        XCTAssertEqual(second.transcript, first.transcript)
        XCTAssertEqual(second.partialTranscript, first.partialTranscript)
        XCTAssertEqual(second.instruction, "Be brief.")
        XCTAssertEqual(second.memory, first.memory)
        XCTAssertEqual(second.assistant, AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "high", provider: .codex))

        let session = try XCTUnwrap(owners.session.session)
        XCTAssertEqual(session.assistantTurns.count, 2)
        XCTAssertEqual(session.assistantTurns.first?.performance?.reusedText, false)
        let retried = try XCTUnwrap(session.assistantTurns.last?.performance)
        XCTAssertTrue(retried.reusedText)
        XCTAssertEqual(retried.transcriptionMs, 0)
        XCTAssertEqual(retried.configuration.reasoningEffort, "high")
        XCTAssertEqual(session.memory.throughSegmentId, segment.id, "A retry does not move the marker.")
        XCTAssertEqual(owners.session.latestTurnID, session.assistantTurns.last?.id)
    }

    @MainActor
    func testARetryDoesNotTranscribeAgain() async throws {
        let owners = TestOwners(provider: .voz)
        await owners.offerCodexCatalogue()
        let transcriber = try await owners.startListening()
        transcriber.segmentsOnTranscribePending = ["Spoken into the phone"]
        transcriber.pendingSeconds = 2
        await owners.session.assist()

        await owners.session.retrySameText()

        XCTAssertEqual(transcriber.calls, ["start", "transcribePending"], "Step 4 is skipped on a retry.")
        let turns = try XCTUnwrap(owners.session.session?.assistantTurns)
        XCTAssertEqual(turns.count, 2)
        XCTAssertEqual(turns.first?.performance?.transcriptionMs, 2_000)
        XCTAssertEqual(turns.last?.performance?.transcriptionMs, 0)
        XCTAssertEqual(turns.last?.performance?.reusedText, true)
    }

    @MainActor
    func testARetryWithNothingRememberedRunsAsFresh() async throws {
        let (owners, _, segment) = try await codexSession()
        XCTAssertFalse(owners.session.canRetry)

        await owners.session.retrySameText()

        let requests = await owners.codex.answerRequests
        XCTAssertEqual(requests.count, 1)
        XCTAssertEqual(requests.first?.transcript, question)
        let session = try XCTUnwrap(owners.session.session)
        XCTAssertEqual(session.assistantTurns.first?.performance?.reusedText, false)
        XCTAssertEqual(session.memory.throughSegmentId, segment.id)
        XCTAssertTrue(owners.session.canRetry)
    }

    @MainActor
    func testANewSessionForgetsTheRememberedRequestAndTheCard() async throws {
        let (owners, _, _) = try await codexSession()
        await owners.codex.setSummary(.failure(RelayFailure.local(.network)))
        await owners.codex.setAnswer(.failure(RelayFailure.wire(.assistantTimeout)), progress: ["Half an"])
        await owners.session.assist()
        XCTAssertTrue(owners.session.canRetry)
        XCTAssertNotNil(owners.session.assistError)
        XCTAssertEqual(owners.session.streamingAnswer, "Half an")

        await owners.session.stop()
        _ = try await owners.startListening()

        XCTAssertFalse(owners.session.canRetry)
        XCTAssertNil(owners.session.assistError)
        XCTAssertEqual(owners.session.streamingAnswer, "")
        XCTAssertNil(owners.session.latestTurnID)
        XCTAssertNil(owners.session.rememberedAssist)

        // A retry in the new session is a fresh request for the new session's text.
        let id = try XCTUnwrap(owners.session.session?.id)
        try await owners.say("New words")
        await owners.codex.setAnswer(.success(reply("An answer")), progress: [])
        await owners.session.retrySameText()
        let requests = await owners.codex.answerRequests
        XCTAssertEqual(requests.last?.transcript, "New words")
        XCTAssertEqual(requests.last?.sessionId, id)
        XCTAssertEqual(owners.session.session?.assistantTurns.first?.performance?.reusedText, false)
    }

    // MARK: - Paused, the timing record, the stages

    @MainActor
    func testAssistWorksWhileTheSessionIsPaused() async throws {
        // AC-IOS-067
        let (owners, _, segment) = try await codexSession()
        await owners.session.togglePause()
        XCTAssertEqual(owners.session.phase, .paused)

        await owners.session.assist()

        XCTAssertEqual(owners.session.phase, .paused, "The phase does not change.")
        XCTAssertNil(owners.session.assistError)
        XCTAssertEqual(owners.session.session?.assistantTurns.count, 1)
        XCTAssertEqual(owners.session.session?.memory.throughSegmentId, segment.id)
        XCTAssertTrue(owners.session.controlsEnabled)
    }

    @MainActor
    func testThePerformanceRecord() async throws {
        let (owners, transcriber, _) = try await codexSession(provider: .parakeet)
        transcriber.emit(.partial("and th"))
        transcriber.emit(.lastChunk(milliseconds: 42))
        await owners.session.waitUntilSettled()
        await owners.codex.setHoldAnswer(true)

        let assisting = Task { await owners.session.assist() }
        await owners.codex.waitForAnswerRequests(count: 1)
        // Half a second passes while the request is out. The session's tick is not due yet.
        await owners.clock.advance(by: 0.5)
        await owners.codex.releaseAnswer()
        await assisting.value

        let found = owners.session.session?.assistantTurns.first?.performance
        let performance = try XCTUnwrap(found)
        XCTAssertEqual(performance.configuration, AssistantConfiguration())
        XCTAssertEqual(performance.speechModel, "parakeet")
        XCTAssertFalse(performance.reusedText)
        XCTAssertEqual(performance.transcriptionMs, 0)
        XCTAssertEqual(performance.contextMs, 0)
        XCTAssertEqual(performance.roundTripMs, 500, accuracy: 0.001)
        XCTAssertEqual(performance.totalMs, 500, accuracy: 0.001)
        XCTAssertEqual(performance.lastLiveChunkMs, 42)
        XCTAssertEqual(performance.transcriptCharacters, question.count + "and th".count)
        XCTAssertEqual(
            performance.execution,
            RelayExecution(
                model: "gpt-5.6-sol",
                reasoningEffort: "low",
                codexMs: 600,
                relayTotalMs: 620,
                requestReadMs: 2,
                relayOverheadMs: 20
            )
        )

        // A Fluid Speech Mini session records the time of its last pass in the same place (D-29).
        let (fluid, fluidTranscriber, _) = try await codexSession(provider: .fluid)
        fluidTranscriber.emit(.lastChunk(milliseconds: 310))
        await fluid.session.waitUntilSettled()
        await fluid.session.assist()
        let fluidPerformance = try XCTUnwrap(fluid.session.session?.assistantTurns.first?.performance)
        XCTAssertEqual(fluidPerformance.lastLiveChunkMs, 310)
        XCTAssertEqual(fluidPerformance.speechModel, "fluid")

        // No other session records the last live chunk.
        let (meta, metaTranscriber, _) = try await codexSession()
        metaTranscriber.emit(.lastChunk(milliseconds: 42))
        await meta.session.waitUntilSettled()
        await meta.session.assist()
        let metaPerformance = try XCTUnwrap(meta.session.session?.assistantTurns.first?.performance)
        XCTAssertNil(metaPerformance.lastLiveChunkMs)
        XCTAssertEqual(metaPerformance.speechModel, "meta")
    }

    @MainActor
    func testTheStagesOfAnAssist() async throws {
        XCTAssertEqual(AssistStage.transcribing.label, "Transcribing\u{2026}")
        XCTAssertEqual(AssistStage.thinking.label, "Thinking\u{2026}")
        XCTAssertEqual(AssistStage.checkingModel.label, "Checking model\u{2026}")
        XCTAssertEqual(AssistStage.replying.label, "Replying\u{2026}")

        // Voz, a fresh request to Codex: the transcription comes first.
        let owners = TestOwners(provider: .voz)
        await owners.offerCodexCatalogue()
        let transcriber = try await owners.startListening()
        transcriber.segmentsOnTranscribePending = ["Spoken into the phone"]
        let fresh = StageLog(owners.session)
        await owners.session.assist()
        XCTAssertEqual(fresh.stages, [nil, .transcribing, .checkingModel, .thinking, nil])

        // A retry skips the transcription.
        let retry = StageLog(owners.session)
        await owners.session.retrySameText()
        XCTAssertEqual(retry.stages, [nil, .thinking, .checkingModel, .thinking, nil])

        // OpenRouter: the first streamed text changes the stage.
        let (direct, _) = try await openRouterSession()
        await direct.openRouter.setAnswer(.success(reply("Paris.")), progress: ["Par"])
        let streamed = StageLog(direct.session)
        await direct.session.assist()
        XCTAssertEqual(streamed.stages, [nil, .thinking, .checkingModel, .thinking, .replying, nil])
    }
}
