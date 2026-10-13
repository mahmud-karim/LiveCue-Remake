import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// A transport that records what it is asked to send and reaches nothing.
private actor UnreachableTransport: HTTPTransport {
    private(set) var requests: [HTTPRequest] = []

    func send(_ request: HTTPRequest) async throws -> (HTTPResponseHead, Data) {
        requests.append(request)
        throw TransportFailure.unreachable
    }

    func stream(_ request: HTTPRequest) async throws -> (HTTPResponseHead, AsyncThrowingStream<Data, Error>) {
        requests.append(request)
        throw TransportFailure.unreachable
    }
}

/// Collects the texts an answer service passes to `onProgress`.
private actor ProgressLog {
    private(set) var texts: [String] = []

    func add(_ text: String) {
        texts.append(text)
    }
}

/// An error no part of the app knows.
private struct UnknownFailure: Error {}

/// The two answer services and `AnswerErrorText`.
final class AnswerServicesTests: XCTestCase {
    private let notPairedText = "Pair this iPhone with the Windows relay first."
    private let networkText = "Could not reach your PC because of a network error. Check the connection and retry."
    private let unavailableText = "OpenRouter could not complete this request. Check your connection or try again later. No fallback service was used."

    private func assistRequest(model: String = "gpt-5.6-sol", provider: AssistantProvider = .codex) -> AssistRequest {
        var request = AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: "What is the capital of France?")
        request.assistant = AssistantConfiguration(
            model: model,
            reasoningEffort: provider == .codex ? "low" : "default",
            provider: provider
        )
        return request
    }

    private func summaryRequest() -> SummaryRequest {
        SummaryRequest(
            sessionId: UUID(),
            assistant: AssistantConfiguration(),
            memory: SessionMemory(),
            transcript: "We agreed to ship on Friday."
        )
    }

    private func relayReply() -> AssistResponse {
        var response = AssistResponse(
            detectedQuestion: "What is the capital of France?",
            answer: "Paris.",
            details: "Paris is the capital of France.",
            memory: SessionMemory(summary: "Capitals")
        )
        response.execution = RelayExecution(
            model: "gpt-5.6-sol",
            reasoningEffort: "low",
            codexMs: 600,
            relayTotalMs: 620,
            requestReadMs: 2,
            relayOverheadMs: 20
        )
        return response
    }

    // MARK: - Codex through the relay

    @MainActor
    func testCodexServiceWithoutAPairingThrowsNotPaired() async {
        let gateway = FakeRelayGateway()
        let service = CodexRelayAnswerService(gateway: gateway, pairingStore: FakePairingStore(nil))
        XCTAssertEqual(service.provider, .codex)
        XCTAssertTrue(service.supportsSummary)

        do {
            _ = try await service.loadCatalogue()
            XCTFail("The catalogue was loaded without a pairing.")
        } catch {
            XCTAssertEqual((error as? RelayFailure)?.isLocal(.notPaired), true)
            XCTAssertEqual(AnswerErrorText.message(for: error, provider: .codex), notPairedText)
        }
        do {
            _ = try await service.answer(assistRequest(), onProgress: { _ in })
            XCTFail("An answer was asked for without a pairing.")
        } catch {
            XCTAssertEqual((error as? RelayFailure)?.isLocal(.notPaired), true)
        }
        do {
            _ = try await service.summarize(summaryRequest())
            XCTFail("A summary was asked for without a pairing.")
        } catch {
            XCTAssertEqual((error as? RelayFailure)?.isLocal(.notPaired), true)
        }

        let calls = await gateway.calls
        XCTAssertEqual(calls, [], "Nothing is sent without a pairing.")
    }

    @MainActor
    func testCodexServiceAsksTheRelayWithThePairingOfTheMoment() async throws {
        let gateway = FakeRelayGateway()
        let store = FakePairingStore(nil)
        let service = CodexRelayAnswerService(gateway: gateway, pairingStore: store)
        let summary = SummaryResponse(title: "Friday", summary: "Ship on Friday.", keyPoints: ["Friday"], actionItems: [])
        await gateway.setModels(.success(FixtureValues.codexCatalogue))
        await gateway.setAssist(.success(relayReply()))
        await gateway.setSummary(.success(summary))

        // The pairing is saved after the service was made: it is read at the moment of use.
        try store.save(FixtureValues.pairing)
        let request = assistRequest()
        let summaryBody = summaryRequest()
        let log = ProgressLog()

        let catalogue = try await service.loadCatalogue()
        let answer = try await service.answer(request, onProgress: { text in await log.add(text) })
        let summarised = try await service.summarize(summaryBody)

        XCTAssertEqual(catalogue, FixtureValues.codexCatalogue)
        XCTAssertEqual(answer, relayReply())
        XCTAssertEqual(summarised, summary)
        let calls = await gateway.calls
        XCTAssertEqual(calls, ["models", "assist", "summarize"])
        let pairings = await gateway.pairings
        XCTAssertEqual(pairings, [FixtureValues.pairing, FixtureValues.pairing, FixtureValues.pairing])
        let assistRequests = await gateway.assistRequests
        XCTAssertEqual(assistRequests, [request])
        let summaryRequests = await gateway.summaryRequests
        XCTAssertEqual(summaryRequests, [summaryBody])
        let progress = await log.texts
        XCTAssertEqual(progress, [], "The relay gives one whole reply.")
        // The service itself never sends the cancel request: the relay client does.
        XCTAssertFalse(calls.contains("cancel"))
    }

    @MainActor
    func testCodexServicePassesARelayFailureOn() async {
        let gateway = FakeRelayGateway()
        let service = CodexRelayAnswerService(gateway: gateway, pairingStore: FakePairingStore(FixtureValues.pairing))
        await gateway.setAssist(.failure(RelayFailure.wire(.assistantBusy)))

        do {
            _ = try await service.answer(assistRequest(), onProgress: { _ in })
            XCTFail("The answer did not fail.")
        } catch {
            XCTAssertEqual((error as? RelayFailure)?.isCode(.assistantBusy), true)
            XCTAssertEqual(
                AnswerErrorText.message(for: error, provider: .codex),
                "The PC is processing another request. Try again shortly."
            )
        }
    }

    @MainActor
    func testCodexCancelCancelsTheRunningAnswer() async {
        let gateway = FakeRelayGateway()
        let service = CodexRelayAnswerService(gateway: gateway, pairingStore: FakePairingStore(FixtureValues.pairing))
        await gateway.setAssist(.success(relayReply()))
        await gateway.setHold(true, for: "assist")
        let request = assistRequest()

        let answering = Task { () -> Result<AssistResponse, any Error> in
            do {
                return .success(try await service.answer(request, onProgress: { _ in }))
            } catch {
                return .failure(error)
            }
        }
        await gateway.waitForCalls(count: 1)

        // Another id cancels nothing.
        await service.cancel(requestId: UUID())
        await service.cancel(requestId: request.requestId)

        switch await answering.value {
        case .success:
            XCTFail("The answer was not cancelled.")
        case .failure(let error):
            XCTAssertTrue(error is CancellationError, "\(error)")
            XCTAssertNil(AnswerErrorText.message(for: error, provider: .codex), "A cancellation shows nothing.")
        }
        // The task is forgotten when it ends: a later cancel has nothing to cancel.
        await service.cancel(requestId: request.requestId)
        let calls = await gateway.calls
        XCTAssertEqual(calls, ["assist"])
    }

    @MainActor
    func testCodexCancelCancelsTheRunningSummary() async {
        let gateway = FakeRelayGateway()
        let service = CodexRelayAnswerService(gateway: gateway, pairingStore: FakePairingStore(FixtureValues.pairing))
        await gateway.setHold(true, for: "summarize")
        let request = summaryRequest()

        let summarising = Task { () -> Result<SummaryResponse, any Error> in
            do {
                return .success(try await service.summarize(request))
            } catch {
                return .failure(error)
            }
        }
        await gateway.waitForCalls(count: 1)
        await service.cancel(requestId: request.requestId)

        switch await summarising.value {
        case .success:
            XCTFail("The summary was not cancelled.")
        case .failure(let error):
            XCTAssertTrue(error is CancellationError, "\(error)")
        }
    }

    @MainActor
    func testCancellingTheCallerCancelsTheRequest() async {
        let gateway = FakeRelayGateway()
        let service = CodexRelayAnswerService(gateway: gateway, pairingStore: FakePairingStore(FixtureValues.pairing))
        await gateway.setHold(true, for: "assist")
        let request = assistRequest()

        let answering = Task { () -> Result<AssistResponse, any Error> in
            do {
                return .success(try await service.answer(request, onProgress: { _ in }))
            } catch {
                return .failure(error)
            }
        }
        await gateway.waitForCalls(count: 1)
        answering.cancel()

        switch await answering.value {
        case .success:
            XCTFail("The answer was not cancelled.")
        case .failure(let error):
            XCTAssertTrue(error is CancellationError, "\(error)")
        }
    }

    // MARK: - OpenRouter direct

    @MainActor
    func testOpenRouterServiceWithoutAKeyThrowsMissingKey() async {
        let transport = UnreachableTransport()
        let service = OpenRouterAnswerService(client: OpenRouterClient(transport: transport), secrets: FakeSecrets())
        XCTAssertEqual(service.provider, .openrouter)
        XCTAssertFalse(service.supportsSummary)

        do {
            _ = try await service.answer(assistRequest(model: "test/direct", provider: .openrouter), onProgress: { _ in })
            XCTFail("An answer was asked for without a key.")
        } catch {
            XCTAssertEqual(error as? OpenRouterError, .missingKey)
            XCTAssertEqual(
                AnswerErrorText.message(for: error, provider: .openrouter),
                "Add your OpenRouter API key in Assistant models & timing first."
            )
        }
        let sent = await transport.requests
        XCTAssertTrue(sent.isEmpty, "Nothing is sent without a key.")
    }

    @MainActor
    func testOpenRouterServiceReadsTheKeyAtTheMomentOfUseAndSendsTheSelectedModel() async throws {
        let transport = UnreachableTransport()
        let secrets = FakeSecrets()
        let service = OpenRouterAnswerService(client: OpenRouterClient(transport: transport), secrets: secrets)
        try secrets.save("fixture-openrouter-key", for: .openRouterAPIKey)

        do {
            _ = try await service.answer(assistRequest(model: "test/direct", provider: .openrouter), onProgress: { _ in })
            XCTFail("The transport reaches nothing.")
        } catch {
            XCTAssertEqual(error as? OpenRouterError, .unavailable)
        }

        let sent = await transport.requests
        XCTAssertEqual(sent.count, 1)
        let request = try XCTUnwrap(sent.first)
        XCTAssertEqual(request.url.absoluteString, "https://openrouter.ai/api/v1/chat/completions")
        XCTAssertEqual(request.headers["Authorization"], "Bearer fixture-openrouter-key")
        let body = try XCTUnwrap(request.body)
        XCTAssertEqual(JSONValue.parse(body)?["model"]?.stringValue, "test/direct")
    }

    @MainActor
    func testOpenRouterServiceWithoutAModelSendsNothing() async throws {
        let transport = UnreachableTransport()
        let secrets = FakeSecrets([.openRouterAPIKey: "fixture-openrouter-key"])
        let service = OpenRouterAnswerService(client: OpenRouterClient(transport: transport), secrets: secrets)
        // No selection at all, and a selection without a model.
        let bare = AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: "Hello")

        for request in [bare, assistRequest(model: "", provider: .openrouter)] {
            do {
                _ = try await service.answer(request, onProgress: { _ in })
                XCTFail("An answer was asked for without a model.")
            } catch {
                XCTAssertEqual(error as? OpenRouterError, .noModel)
            }
        }
        let sent = await transport.requests
        XCTAssertTrue(sent.isEmpty)
    }

    @MainActor
    func testOpenRouterServiceHasNoSummaryAndPassesClientFailuresOn() async {
        let transport = UnreachableTransport()
        let secrets = FakeSecrets([.openRouterAPIKey: "fixture-openrouter-key"])
        let service = OpenRouterAnswerService(client: OpenRouterClient(transport: transport), secrets: secrets)

        do {
            _ = try await service.summarize(summaryRequest())
            XCTFail("OpenRouter gives no summary.")
        } catch {
            XCTAssertEqual(error as? OpenRouterError, .unavailable)
        }
        var sent = await transport.requests
        XCTAssertTrue(sent.isEmpty, "A summary is never sent to OpenRouter.")

        do {
            _ = try await service.loadCatalogue()
            XCTFail("The transport reaches nothing.")
        } catch {
            XCTAssertEqual(error as? OpenRouterError, .unavailable)
        }
        do {
            try await service.verify(key: "fixture-openrouter-key")
            XCTFail("The transport reaches nothing.")
        } catch {
            XCTAssertEqual(error as? OpenRouterError, .unavailable)
        }
        do {
            try await service.verify(key: "two words")
            XCTFail("A key with a space is malformed.")
        } catch {
            XCTAssertEqual(error as? OpenRouterError, .malformedKey)
        }
        sent = await transport.requests
        XCTAssertEqual(sent.map(\.url.absoluteString), [
            "https://openrouter.ai/api/v1/models",
            "https://openrouter.ai/api/v1/key"
        ])
        XCTAssertNil(sent.first?.headers["Authorization"], "The catalogue needs no key.")

        // Nothing is in flight: a cancel does nothing.
        await service.cancel(requestId: UUID())
    }

    // MARK: - The service of a provider

    @MainActor
    func testLiveAnswerServicesReturnsTheServiceOfTheProvider() async {
        let codex = CodexRelayAnswerService(gateway: FakeRelayGateway(), pairingStore: FakePairingStore(nil))
        let openRouter = OpenRouterAnswerService(
            client: OpenRouterClient(transport: UnreachableTransport()),
            secrets: FakeSecrets()
        )
        let services = LiveAnswerServices(codex: codex, openRouter: openRouter)

        XCTAssertTrue(services.service(for: .codex) as AnyObject === codex)
        XCTAssertTrue(services.service(for: .openrouter) as AnyObject === openRouter)
        XCTAssertEqual(services.service(for: .codex).provider, .codex)
        XCTAssertEqual(services.service(for: .openrouter).provider, .openrouter)
    }

    // MARK: - AnswerErrorText

    @MainActor
    func testAnswerErrorTextForEachErrorType() async {
        // RelayFailure: the phone's own text for the code or the condition.
        XCTAssertEqual(AnswerErrorText.message(for: RelayFailure.local(.notPaired), provider: .codex), notPairedText)
        XCTAssertEqual(
            AnswerErrorText.message(for: RelayFailure.wire(.assistantTimeout), provider: .codex),
            "Codex on your PC took too long to answer. Retry, or choose a faster model or a lower reasoning level."
        )
        XCTAssertEqual(
            AnswerErrorText.message(for: RelayFailure.local(.notConfirmed), provider: .codex),
            "The PC did not confirm the selected model. Restart the updated LiveCue Desktop app, then retry."
        )
        XCTAssertEqual(
            AnswerErrorText.message(for: RelayFailure.local(.http, httpStatus: 502), provider: .codex),
            "Relay error (502)."
        )
        // A code this build does not know shows the relay's own short text.
        let unknownCode = RelayFailure(kind: .wire("NEW_CODE"), origin: .http, httpStatus: 400, relayText: "Something new.")
        XCTAssertEqual(AnswerErrorText.message(for: unknownCode, provider: .codex), "Something new.")

        // OpenRouterError and MetaSpeechError: their own descriptions.
        for failure in [
            OpenRouterError.missingKey, .malformedKey, .invalidKey, .credits, .rateLimited, .unavailable,
            .invalidResponse, .truncated, .noModel
        ] {
            XCTAssertEqual(AnswerErrorText.message(for: failure, provider: .openrouter), failure.errorDescription)
            XCTAssertNotNil(failure.errorDescription)
        }
        XCTAssertEqual(
            AnswerErrorText.message(for: OpenRouterError.truncated, provider: .openrouter),
            "The selected model reached the response limit before finishing. Try a shorter question or another model."
        )
        XCTAssertEqual(
            AnswerErrorText.message(for: MetaSpeechError.connection, provider: .codex),
            "Could not connect directly to Meta. Check this iPhone's internet connection and retry. No PC is required for Meta transcription."
        )

        // TranscriberFailure (Voz transcription inside an Assist): its message.
        XCTAssertEqual(
            AnswerErrorText.message(for: TranscriberFailure(.processingFailed, message: "The reason."), provider: .openrouter),
            "The reason."
        )

        // CancellationError: nothing is shown.
        XCTAssertNil(AnswerErrorText.message(for: CancellationError(), provider: .codex))
        XCTAssertNil(AnswerErrorText.message(for: CancellationError(), provider: .openrouter))

        // Anything else: never its own text.
        XCTAssertEqual(AnswerErrorText.message(for: UnknownFailure(), provider: .codex), networkText)
        XCTAssertEqual(AnswerErrorText.message(for: UnknownFailure(), provider: .openrouter), unavailableText)
        XCTAssertEqual(AnswerErrorText.message(for: TransportFailure.tls, provider: .codex), networkText)
        XCTAssertEqual(AnswerErrorText.message(for: URLError(.notConnectedToInternet), provider: .openrouter), unavailableText)
    }

    // MARK: - The answer fixture (section 4.9)

    @MainActor
    func testFixtureAnswerShowsThePartialTextWaitsAndAnswers() async throws {
        let clock = ManualClock()
        let service = FixtureAnswerService(provider: .codex, mode: TestMode(), clock: clock)
        XCTAssertEqual(service.provider, .codex)
        XCTAssertTrue(service.supportsSummary)
        var request = assistRequest()
        request.assistant = AssistantConfiguration(model: "gpt-6-luna", reasoningEffort: "high", provider: .codex)
        let sent = request
        let log = ProgressLog()

        let answering = Task { try await service.answer(sent, onProgress: { text in await log.add(text) }) }
        await clock.waitForSleepers(count: 1)
        let shown = await log.texts
        XCTAssertEqual(shown, [FixtureValues.partialAnswer], "The partial text comes before the wait.")
        await clock.advance(by: FixtureValues.assistPartialSeconds)
        let answer = try await answering.value

        XCTAssertEqual(answer.answer, FixtureValues.answer)
        XCTAssertEqual(answer.details, FixtureValues.details)
        XCTAssertEqual(answer.detectedQuestion, FixtureValues.segmentText)
        XCTAssertNil(answer.memory.throughSegmentId, "The app owns the marker.")
        var execution = FixtureValues.execution
        execution.model = "gpt-6-luna"
        execution.reasoningEffort = "high"
        XCTAssertEqual(answer.execution, execution, "The selection is echoed with the fixture timings.")
        XCTAssertNil(answer.execution?.usage)
    }

    @MainActor
    func testFixtureOpenRouterAnswerHasTheSentinelAndTheUsage() async throws {
        let clock = ManualClock()
        let service = FixtureAnswerService(provider: .openrouter, mode: TestMode(), clock: clock)
        XCTAssertFalse(service.supportsSummary)
        let request = assistRequest(model: "test/direct", provider: .openrouter)

        let answering = Task { try await service.answer(request, onProgress: { _ in }) }
        await clock.waitForSleepers(count: 1)
        await clock.advance(by: FixtureValues.assistPartialSeconds)
        let answer = try await answering.value

        XCTAssertEqual(answer.detectedQuestion, "Latest conversation")
        XCTAssertEqual(answer.answer, FixtureValues.answer)
        XCTAssertEqual(answer.execution?.model, "test/direct")
        XCTAssertEqual(answer.execution?.reasoningEffort, "default")
        XCTAssertEqual(answer.execution?.usage, FixtureValues.openRouterUsage)
    }

    @MainActor
    func testFixtureAnswerFailsOncePerSessionWhenAsked() async throws {
        let clock = ManualClock()
        var mode = TestMode()
        mode.assistFailsOnce = true
        let service = FixtureAnswerService(provider: .codex, mode: mode, clock: clock)
        let first = assistRequest()
        var again = first
        again.requestId = UUID()
        let retry = again
        let other = assistRequest()
        let log = ProgressLog()

        // The first call for a session fails, after the partial text was shown.
        let failing = Task { () -> Result<AssistResponse, any Error> in
            do {
                return .success(try await service.answer(first, onProgress: { text in await log.add(text) }))
            } catch {
                return .failure(error)
            }
        }
        await clock.waitForSleepers(count: 1)
        await clock.advance(by: FixtureValues.assistPartialSeconds)
        switch await failing.value {
        case .success:
            XCTFail("The first answer of the session did not fail.")
        case .failure(let error):
            XCTAssertEqual(error as? OpenRouterError, .unavailable)
        }
        let shown = await log.texts
        XCTAssertEqual(shown, [FixtureValues.partialAnswer])

        // The second call for the same session answers.
        let answering = Task { try await service.answer(retry, onProgress: { _ in }) }
        await clock.waitForSleepers(count: 1)
        await clock.advance(by: FixtureValues.assistPartialSeconds)
        let answer = try await answering.value
        XCTAssertEqual(answer.answer, FixtureValues.answer)

        // Another session fails once of its own.
        let failingAgain = Task { () -> Bool in
            do {
                _ = try await service.answer(other, onProgress: { _ in })
                return false
            } catch {
                return true
            }
        }
        await clock.waitForSleepers(count: 1)
        await clock.advance(by: FixtureValues.assistPartialSeconds)
        let failedAgain = await failingAgain.value
        XCTAssertTrue(failedAgain)
    }

    @MainActor
    func testFixtureCataloguesKeyCheckAndSummary() async throws {
        let clock = ManualClock()
        let codex = FixtureAnswerService(provider: .codex, mode: TestMode(), clock: clock)
        let openRouter = FixtureAnswerService(provider: .openrouter, mode: TestMode(), clock: clock)

        let codexCatalogue = try await codex.loadCatalogue()
        XCTAssertEqual(codexCatalogue, FixtureValues.codexCatalogue)
        let openRouterCatalogue = try await openRouter.loadCatalogue()
        XCTAssertEqual(openRouterCatalogue, FixtureValues.openRouterCatalogue)
        XCTAssertEqual(clock.sleeperCount, 0, "Neither catalogue waits.")

        try await openRouter.verify(key: "anything")
        await codex.cancel(requestId: UUID())
        let summary = try await codex.summarize(summaryRequest())
        XCTAssertEqual(summary.title, "Local transcription")
        XCTAssertEqual(summary.summary, FixtureValues.answer)
        XCTAssertEqual(summary.keyPoints, [FixtureValues.details])
        XCTAssertEqual(summary.actionItems, [])

        let services = FixtureAnswerServices(mode: TestMode(), clock: clock)
        XCTAssertEqual(services.service(for: .codex).provider, .codex)
        XCTAssertEqual(services.service(for: .openrouter).provider, .openrouter)
    }

    @MainActor
    func testFixtureCodexCatalogueIsSlowWhenAsked() async throws {
        let clock = ManualClock()
        var mode = TestMode()
        mode.pcCatalogSlow = true
        let codex = FixtureAnswerService(provider: .codex, mode: mode, clock: clock)
        let openRouter = FixtureAnswerService(provider: .openrouter, mode: mode, clock: clock)

        // Only the PC's catalogue is slow.
        let direct = try await openRouter.loadCatalogue()
        XCTAssertEqual(direct, FixtureValues.openRouterCatalogue)

        let loading = Task { try await codex.loadCatalogue() }
        await clock.waitForSleepers(count: 1)
        await clock.advance(by: FixtureValues.catalogueSlowSeconds - 1)
        XCTAssertEqual(clock.sleeperCount, 1, "Eight seconds have not passed.")
        await clock.advance(by: 1)
        let catalogue = try await loading.value
        XCTAssertEqual(catalogue, FixtureValues.codexCatalogue)
    }
}
