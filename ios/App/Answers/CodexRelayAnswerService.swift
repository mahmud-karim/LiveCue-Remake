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

    init(gateway: any RelayGateway, pairingStore: any PairingStoring) {
        self.gateway = gateway
        self.pairingStore = pairingStore
    }

    func loadCatalogue() async throws -> [AssistantModelOption] {
        // STUB(WP-08)
        throw RelayFailure.local(.notPaired)
    }

    func answer(
        _ request: AssistRequest,
        onProgress: @escaping @Sendable (String) async -> Void
    ) async throws -> AssistResponse {
        // STUB(WP-08)
        throw RelayFailure.local(.notPaired)
    }

    func summarize(_ request: SummaryRequest) async throws -> SummaryResponse {
        // STUB(WP-08)
        throw RelayFailure.local(.notPaired)
    }

    func cancel(requestId: UUID) async {
        // STUB(WP-08)
    }
}
