import Foundation
import LiveCueCore

/// Two answer services behind one interface (architecture 4.8).
protocol AnswerService: Sendable {
    var provider: AssistantProvider { get }
    /// True for Codex only.
    var supportsSummary: Bool { get }
    func loadCatalogue() async throws -> [AssistantModelOption]
    /// `request.assistant` is set and the request is already fitted. `onProgress` receives the whole text so far.
    func answer(
        _ request: AssistRequest,
        onProgress: @escaping @Sendable (String) async -> Void
    ) async throws -> AssistResponse
    func summarize(_ request: SummaryRequest) async throws -> SummaryResponse
    /// Cancels the task that is running the request with this id, if any. Sends nothing itself.
    func cancel(requestId: UUID) async
}

protocol AnswerServiceProviding: Sendable {
    func service(for provider: AssistantProvider) -> any AnswerService
}

/// The OpenRouter key check. Throws `OpenRouterError`.
protocol OpenRouterKeyVerifying: Sendable {
    func verify(key: String) async throws
}
