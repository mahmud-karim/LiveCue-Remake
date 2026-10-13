import Foundation
import LiveCueCore

/// Service `codex`. Reads the pairing at the moment of use; without one every call throws
/// `RelayFailure.local(.notPaired)`. Each answer and summary runs in a task the actor keeps by request id
/// until it ends; `cancel` cancels that task, and the relay client then sends the one DELETE.
actor CodexRelayAnswerService: AnswerService {
    nonisolated let provider: AssistantProvider = .codex

    nonisolated var supportsSummary: Bool {
        true
    }

    private let gateway: any RelayGateway
    private let pairingStore: any PairingStoring
    private var answers: [UUID: Task<AssistResponse, any Error>] = [:]
    private var summaries: [UUID: Task<SummaryResponse, any Error>] = [:]

    init(gateway: any RelayGateway, pairingStore: any PairingStoring) {
        self.gateway = gateway
        self.pairingStore = pairingStore
    }

    func loadCatalogue() async throws -> [AssistantModelOption] {
        let pairing = try currentPairing()
        return try await gateway.models(pairing)
    }

    /// The relay gives one whole reply: `onProgress` is never called.
    func answer(
        _ request: AssistRequest,
        onProgress: @escaping @Sendable (String) async -> Void
    ) async throws -> AssistResponse {
        let pairing = try currentPairing()
        let gateway = self.gateway
        let task = Task { () async throws -> AssistResponse in
            try await gateway.assist(request, pairing: pairing)
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
        let pairing = try currentPairing()
        let gateway = self.gateway
        let task = Task { () async throws -> SummaryResponse in
            try await gateway.summarize(request, pairing: pairing)
        }
        summaries[request.requestId] = task
        defer { summaries[request.requestId] = nil }
        return try await withTaskCancellationHandler {
            try await task.value
        } onCancel: {
            task.cancel()
        }
    }

    func cancel(requestId: UUID) async {
        answers[requestId]?.cancel()
        summaries[requestId]?.cancel()
    }

    private func currentPairing() throws -> PairingPayload {
        guard let pairing = pairingStore.load() else {
            throw RelayFailure.local(.notPaired)
        }
        return pairing
    }
}
