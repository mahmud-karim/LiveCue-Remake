import Foundation
import LiveCueCore
import Observation

enum HistoryUsageLabel: Equatable {
    /// Identifier `history-transcription-cost`.
    case meta(String)
    /// Identifier `history-local-transcription`.
    case local(String)
}

/// What one History row shows.
struct HistoryRowContent: Equatable, Identifiable {
    let id: UUID
    let title: String
    /// Abbreviated date, short time, device locale.
    let dateText: String
    /// "N transcript segments · M assists"
    let countsText: String
    /// nil when the session recorded no usage.
    let usage: HistoryUsageLabel?

    init(session: Session) {
        // STUB(WP-11)
        id = session.id
        title = session.title
        dateText = ""
        countsText = ""
        usage = nil
    }
}

/// History (screens 13).
@MainActor @Observable final class HistoryViewModel {
    /// The row whose confirmation is showing.
    var pendingDeleteID: UUID? = nil

    init(history: HistoryModel) {
        // STUB(WP-11)
    }

    var rows: [HistoryRowContent] { [] } // STUB(WP-11)
    var isEmpty: Bool { true } // STUB(WP-11)

    func requestDelete(_ id: UUID) {
        // STUB(WP-11)
    }

    func confirmDelete() async {
        // STUB(WP-11)
    }

    func cancelDelete() {
        // STUB(WP-11)
    }
}
