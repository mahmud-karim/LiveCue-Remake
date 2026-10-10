import Foundation
import LiveCueCore

/// Returns the service for a provider.
struct LiveAnswerServices: AnswerServiceProviding {
    private let codex: CodexRelayAnswerService
    private let openRouter: OpenRouterAnswerService

    init(codex: CodexRelayAnswerService, openRouter: OpenRouterAnswerService) {
        self.codex = codex
        self.openRouter = openRouter
    }

    func service(for provider: AssistantProvider) -> any AnswerService {
        // STUB(WP-08)
        switch provider {
        case .codex: return codex
        case .openrouter: return openRouter
        }
    }
}
