import Foundation
import LiveCueCore

/// The answer service of the test seam (section 4.9), for either provider. It also stands in for the
/// OpenRouter key check, which succeeds without a request.
actor FixtureAnswerService: AnswerService, OpenRouterKeyVerifying {
    nonisolated let provider: AssistantProvider

    nonisolated var supportsSummary: Bool {
        provider == .codex
    }

    private let mode: TestMode
    private let clock: any AppClock
    /// The sessions whose first answer has already failed (`-assist-fails-once`).
    private var failedSessions: Set<UUID> = []

    init(provider: AssistantProvider, mode: TestMode, clock: any AppClock) {
        self.provider = provider
        self.mode = mode
        self.clock = clock
    }

    /// The Codex catalogue waits 8 s first with `-pc-catalog-slow`.
    func loadCatalogue() async throws -> [AssistantModelOption] {
        switch provider {
        case .codex:
            if mode.pcCatalogSlow {
                try await clock.sleep(seconds: FixtureValues.catalogueSlowSeconds)
            }
            return FixtureValues.codexCatalogue
        case .openrouter:
            return FixtureValues.openRouterCatalogue
        }
    }

    /// The partial text, 0.4 s, then the fixed answer. With `-assist-fails-once` the first call for each
    /// session id fails instead, after the partial text was shown.
    func answer(
        _ request: AssistRequest,
        onProgress: @escaping @Sendable (String) async -> Void
    ) async throws -> AssistResponse {
        await onProgress(FixtureValues.partialAnswer)
        try await clock.sleep(seconds: FixtureValues.assistPartialSeconds)

        if mode.assistFailsOnce, !failedSessions.contains(request.sessionId) {
            failedSessions.insert(request.sessionId)
            throw OpenRouterError.unavailable
        }

        var response = AssistResponse(
            detectedQuestion: provider == .codex ? FixtureValues.segmentText : AssistantTurn.latestConversation,
            answer: FixtureValues.answer,
            details: FixtureValues.details,
            // No marker: the app owns it for both services.
            memory: SessionMemory(summary: FixtureValues.answer, facts: [], openQuestions: [], throughSegmentId: nil)
        )
        var execution = FixtureValues.execution
        if let assistant = request.assistant {
            execution.model = assistant.model
            execution.reasoningEffort = assistant.reasoningEffort
        }
        if provider == .openrouter {
            execution.usage = FixtureValues.openRouterUsage
        }
        response.execution = execution
        return response
    }

    func summarize(_ request: SummaryRequest) async throws -> SummaryResponse {
        SummaryResponse(
            title: FixtureAnswerService.summaryTitle,
            summary: FixtureValues.answer,
            keyPoints: [FixtureValues.details],
            actionItems: []
        )
    }

    /// Nothing is kept in flight here, so there is nothing to cancel.
    func cancel(requestId: UUID) async {}

    func verify(key: String) async throws {}

    static let summaryTitle = "Local transcription"
}

struct FixtureAnswerServices: AnswerServiceProviding {
    private let codex: FixtureAnswerService
    private let openRouter: FixtureAnswerService

    init(mode: TestMode, clock: any AppClock) {
        codex = FixtureAnswerService(provider: .codex, mode: mode, clock: clock)
        openRouter = FixtureAnswerService(provider: .openrouter, mode: mode, clock: clock)
    }

    func service(for provider: AssistantProvider) -> any AnswerService {
        switch provider {
        case .codex: return codex
        case .openrouter: return openRouter
        }
    }
}
