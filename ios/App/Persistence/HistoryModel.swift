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
    /// The tag of the newest reload. A reload that ends with an older tag read an older store.
    @ObservationIgnored private var reloadTag = 0

    init(store: any SessionStoring) {
        self.store = store
    }

    func reload() async {
        reloadTag += 1
        let tag = reloadTag
        let stored = await store.loadAll()
        // A later reload asked the store after this one did: its answer is the newer one.
        guard tag == reloadTag else { return }
        sessions = HistoryModel.newestFirst(stored)
    }

    /// Calls `willDelete`, deletes, reloads.
    func delete(id: UUID) async {
        await willDelete?(id)
        // A store that could not delete still holds the session, and the reload shows it again.
        try? await store.delete(id: id)
        await reload()
    }

    func session(id: UUID) -> Session? {
        sessions.first { $0.id == id }
    }

    /// Newest start first. Sessions that started at the same instant keep the store's order.
    private static func newestFirst(_ stored: [Session]) -> [Session] {
        stored.enumerated()
            .sorted { first, second in
                if first.element.startedAt != second.element.startedAt {
                    return first.element.startedAt > second.element.startedAt
                }
                return first.offset < second.offset
            }
            .map { $0.element }
    }
}
