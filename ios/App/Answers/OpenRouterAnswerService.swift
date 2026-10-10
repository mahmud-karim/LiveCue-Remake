import Foundation
import LiveCueCore

/// Service `openrouter`. Reads the key at the moment of use. `answer` passes an option made from
/// `request.assistant.model` and a nil marker (the controller owns the marker). `summarize` is never
/// called and throws `.unavailable`.
actor OpenRouterAnswerService: AnswerService, OpenRouterKeyVerifying {
    nonisolated let provider: AssistantProvider = .openrouter

    nonisolated var supportsSummary: Bool {
        false
    }

    private let client: OpenRouterClient
    private let secrets: any SecretStoring

    init(client: OpenRouterClient, secrets: any SecretStoring) {
        self.client = client
        self.secrets = secrets
    }

    func loadCatalogue() async throws -> [AssistantModelOption] {
        // STUB(WP-08)
        throw OpenRouterError.unavailable
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
        throw OpenRouterError.unavailable
    }
}
