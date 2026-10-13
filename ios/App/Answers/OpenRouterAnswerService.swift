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
    /// The answers in flight, by request id.
    private var answers: [UUID: Task<AssistResponse, any Error>] = [:]

    init(client: OpenRouterClient, secrets: any SecretStoring) {
        self.client = client
        self.secrets = secrets
    }

    func loadCatalogue() async throws -> [AssistantModelOption] {
        try await client.models()
    }

    func answer(
        _ request: AssistRequest,
        onProgress: @escaping @Sendable (String) async -> Void
    ) async throws -> AssistResponse {
        guard let key = secrets.read(.openRouterAPIKey) else {
            throw OpenRouterError.missingKey
        }
        // Without a model the id is empty, and the client throws `.noModel` before anything is sent.
        let model = request.assistant?.model ?? ""
        let option = AssistantModelOption(id: model, name: model, reasoningEfforts: ["default"])
        let client = self.client
        let task = Task { () async throws -> AssistResponse in
            try await client.assist(request, option: option, key: key, throughSegmentId: nil, onUpdate: onProgress)
        }
        answers[request.requestId] = task
        defer { answers[request.requestId] = nil }
        // A caller that is cancelled cancels the request too.
        return try await withTaskCancellationHandler {
            try await task.value
        } onCancel: {
            task.cancel()
        }
    }

    func summarize(_ request: SummaryRequest) async throws -> SummaryResponse {
        throw OpenRouterError.unavailable
    }

    func cancel(requestId: UUID) async {
        answers[requestId]?.cancel()
    }

    func verify(key: String) async throws {
        try await client.verifyKey(key)
    }
}
