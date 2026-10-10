import Foundation
import LiveCueCore

/// The end-of-session summary and its cancellation (section 4.4).
extension SessionController {
    /// Cancels the summary request of a stored session, if one is in flight. Called before a delete.
    func cancelSummary(forSession id: UUID) async {
        // STUB(WP-08)
    }
}
