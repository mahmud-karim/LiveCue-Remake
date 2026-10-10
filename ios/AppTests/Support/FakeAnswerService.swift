import Foundation
import LiveCueCore
@testable import LiveCueRemake

/// An answer service a test scripts by hand. It also stands in for the OpenRouter key check.
///
/// Defaults: an empty catalogue; an answer that echoes the request's selection in its execution record;
/// a fixed summary; a key check that succeeds.
actor FakeAnswerService: AnswerService, OpenRouterKeyVerifying {
    nonisolated let provider: AssistantProvider

    nonisolated var supportsSummary: Bool {
        provider == .codex
    }

    private enum Counter {
        case catalogue, answer, summary
    }

    private var catalogueResult: Result<[AssistantModelOption], any Error> = .success([])
    private var answerResult: Result<AssistResponse, any Error>?
    private var answerProgress: [String] = []
    private var summaryResult: Result<SummaryResponse, any Error> = .success(
        SummaryResponse(title: "Fake summary title", summary: "Fake summary.", keyPoints: ["A key point"], actionItems: [])
    )
    private var verifyResult: Result<Void, any Error> = .success(())
    private var holdsAnswer = false
    private var holdsSummary = false
    private var held: [UUID: CheckedContinuation<Void, any Error>] = [:]
    private var waiters: [(counter: Counter, count: Int, continuation: CheckedContinuation<Void, Never>)] = []

    private(set) var catalogueCalls = 0
    private(set) var answerRequests: [AssistRequest] = []
    private(set) var summaryRequests: [SummaryRequest] = []
    /// The id of every `cancel(requestId:)`, in order.
    private(set) var cancelledIDs: [UUID] = []
    /// The key of every `verify(key:)`, in order.
    private(set) var verifiedKeys: [String] = []

    init(provider: AssistantProvider) {
        self.provider = provider
    }

    // MARK: - Scripting

    func setCatalogue(_ result: Result<[AssistantModelOption], any Error>) {
        catalogueResult = result
    }

    /// `progress` is passed to `onProgress`, one text at a time, before the result.
    func setAnswer(_ result: Result<AssistResponse, any Error>, progress: [String]) {
        answerResult = result
        answerProgress = progress
    }

    func setSummary(_ result: Result<SummaryResponse, any Error>) {
        summaryResult = result
    }

    func setVerify(_ result: Result<Void, any Error>) {
        verifyResult = result
    }

    /// True: `answer` suspends, after its progress calls, until released or cancelled.
    func setHoldAnswer(_ hold: Bool) {
        holdsAnswer = hold
    }

    /// True: `summarize` suspends until released or cancelled.
    func setHoldSummary(_ hold: Bool) {
        holdsSummary = hold
    }

    /// Lets every held answer go on.
    func releaseAnswer() {
        releaseHeld()
    }

    /// Lets every held summary go on.
    func releaseSummary() {
        releaseHeld()
    }

    // MARK: - Waiting

    func waitForCatalogueCalls(count: Int) async {
        await wait(for: .catalogue, count: count)
    }

    func waitForAnswerRequests(count: Int) async {
        await wait(for: .answer, count: count)
    }

    func waitForSummaryRequests(count: Int) async {
        await wait(for: .summary, count: count)
    }

    // MARK: - AnswerService

    func loadCatalogue() async throws -> [AssistantModelOption] {
        catalogueCalls += 1
        wake()
        return try catalogueResult.get()
    }

    func answer(
        _ request: AssistRequest,
        onProgress: @escaping @Sendable (String) async -> Void
    ) async throws -> AssistResponse {
        answerRequests.append(request)
        wake()
        for text in answerProgress {
            await onProgress(text)
        }
        if holdsAnswer {
            try await hold(request.requestId)
        }
        if let answerResult {
            return try answerResult.get()
        }
        return FakeAnswerService.defaultAnswer(to: request)
    }

    func summarize(_ request: SummaryRequest) async throws -> SummaryResponse {
        summaryRequests.append(request)
        wake()
        if holdsSummary {
            try await hold(request.requestId)
        }
        return try summaryResult.get()
    }

    /// Records the id and ends the held request with that id, if any, with `CancellationError`.
    func cancel(requestId: UUID) async {
        cancelledIDs.append(requestId)
        held.removeValue(forKey: requestId)?.resume(throwing: CancellationError())
    }

    func verify(key: String) async throws {
        verifiedKeys.append(key)
        try verifyResult.get()
    }

    // MARK: - Internals

    /// The answer used when none was set: fixed texts, and an execution record that echoes the selection.
    static func defaultAnswer(to request: AssistRequest) -> AssistResponse {
        var response = AssistResponse(
            detectedQuestion: "Fake detected question",
            answer: "Fake answer.",
            details: "Fake details.",
            memory: SessionMemory(summary: "Fake memory", facts: ["A fact"], openQuestions: [])
        )
        if let assistant = request.assistant {
            response.execution = RelayExecution(
                model: assistant.model,
                reasoningEffort: assistant.reasoningEffort,
                codexMs: 600,
                relayTotalMs: 620,
                requestReadMs: 2,
                relayOverheadMs: 20
            )
        }
        return response
    }

    private func hold(_ id: UUID) async throws {
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, any Error>) in
                if Task.isCancelled {
                    continuation.resume(throwing: CancellationError())
                } else {
                    held[id] = continuation
                }
            }
        } onCancel: {
            Task { await self.cancelHeld(id) }
        }
    }

    private func cancelHeld(_ id: UUID) {
        held.removeValue(forKey: id)?.resume(throwing: CancellationError())
    }

    private func releaseHeld() {
        let waiting = held
        held = [:]
        for continuation in waiting.values {
            continuation.resume()
        }
    }

    private func value(of counter: Counter) -> Int {
        switch counter {
        case .catalogue: return catalogueCalls
        case .answer: return answerRequests.count
        case .summary: return summaryRequests.count
        }
    }

    private func wait(for counter: Counter, count: Int) async {
        if value(of: counter) >= count { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiters.append((counter: counter, count: count, continuation: continuation))
        }
    }

    private func wake() {
        var stillWaiting: [(counter: Counter, count: Int, continuation: CheckedContinuation<Void, Never>)] = []
        var ready: [CheckedContinuation<Void, Never>] = []
        for waiter in waiters {
            if value(of: waiter.counter) >= waiter.count {
                ready.append(waiter.continuation)
            } else {
                stillWaiting.append(waiter)
            }
        }
        waiters = stillWaiting
        for continuation in ready {
            continuation.resume()
        }
    }
}

struct FakeAnswerServices: AnswerServiceProviding {
    private let codex: FakeAnswerService
    private let openRouter: FakeAnswerService

    init(codex: FakeAnswerService, openRouter: FakeAnswerService) {
        self.codex = codex
        self.openRouter = openRouter
    }

    func service(for provider: AssistantProvider) -> any AnswerService {
        switch provider {
        case .codex: return codex
        case .openrouter: return openRouter
        }
    }
}
