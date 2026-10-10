import Foundation
import LiveCueCore

struct TimingRow: Equatable, Identifiable {
    let id: String
    let key: String
    let value: String
}

/// What the timing view shows for one answer. Pure, so it is unit-tested without a view.
struct TimingViewContent: Equatable {
    /// "<model used> · <effort used>"
    let caption: String
    /// Screens 4.6, in order.
    let rows: [TimingRow]
    /// "N input · M output tokens"
    let usageCaption: String?
    /// "PC timing unavailable. Restart the updated desktop relay."
    let warning: String?
    /// "<speech id> · <N> transcript characters" (+ " · same-text retry")
    let footerCaption: String
    /// S-20 or S-21.
    let footnote: String

    /// nil without a performance record.
    init?(turn: AssistantTurn) {
        // STUB(WP-10)
        return nil
    }
}
