import Foundation
import LiveCueCore

/// Latest value wins: a save requested while one is running replaces any queued one.
@MainActor final class SessionPersistence {
    init(store: any SessionStoring, clock: any AppClock) {
        // STUB(WP-08)
    }

    func schedule(_ session: Session) {
        // STUB(WP-08)
    }

    /// Returns when nothing is queued or running.
    func flush() async {
        // STUB(WP-08)
    }
}
