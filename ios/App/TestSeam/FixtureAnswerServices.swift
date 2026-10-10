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

    init(provider: AssistantProvider, mode: TestMode, clock: any AppClock) {
        self.provider = provider
        self.mode = mode
        self.clock = clock
    }

    func loadCatalogue() async throws -> [AssistantModelOption] {
        // STUB(WP-08)
        provider == .codex ? FixtureValues.codexCatalogue : FixtureValues.openRouterCatalogue
    }

    func answer(
        _ request: AssistRequest,
        onProgress: @escaping @Sendable (String) async -> Void
    ) async throws -> AssistResponse {
        // STUB(WP-08)
        throw OpenRouterError.unavailable
    }

    func summarize(_ request: SummaryRequest) async throws -> SummaryResponse {
        // STUB(WP-08)
        throw OpenRouterError.unavailable
    }

    func cancel(requestId: UUID) async {
        // STUB(WP-08)
    }

    func verify(key: String) async throws {
        // STUB(WP-08)
    }
}

struct FixtureAnswerServices: AnswerServiceProviding {
    private let codex: FixtureAnswerService
    private let openRouter: FixtureAnswerService

    init(mode: TestMode, clock: any AppClock) {
        codex = FixtureAnswerService(provider: .codex, mode: mode, clock: clock)
        openRouter = FixtureAnswerService(provider: .openrouter, mode: mode, clock: clock)
    }

    func service(for provider: AssistantProvider) -> any AnswerService {
        // STUB(WP-08)
        switch provider {
        case .codex: return codex
        case .openrouter: return openRouter
        }
    }
}
