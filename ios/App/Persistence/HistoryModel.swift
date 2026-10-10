import Foundation
import LiveCueCore
import Observation

/// Stored sessions in memory, and their deletion.
@MainActor @Observable final class HistoryModel {
    /// Newest start first.
    private(set) var sessions: [Session] = []
    /// `SessionController.cancelSummary`, installed by `AppEnvironment`.
    @ObservationIgnored var willDelete: (@MainActor (UUID) async -> Void)?

    private let store: any SessionStoring

    init(store: any SessionStoring) {
        self.store = store
    }

    func reload() async {
        // STUB(WP-09)
    }

    /// Calls `willDelete`, deletes, reloads.
    func delete(id: UUID) async {
        // STUB(WP-09)
    }

    func session(id: UUID) -> Session? {
        // STUB(WP-09)
        nil
    }
}
