import Foundation
import Observation

/// Speech benchmark (screens 12).
@MainActor @Observable final class BenchmarkViewModel {
    var reference: String = "The quick brown fox jumps over the lazy dog"
    var hypothesis: String = ""

    init(session: SessionController, history: HistoryModel) {
        // STUB(WP-11)
    }

    /// One decimal and "%", or "–" while the reference is empty.
    var rateText: String { "–" } // STUB(WP-11)
    var canUseLatest: Bool { false } // STUB(WP-11)
    var latestCaption: String { "" } // STUB(WP-11)

    /// The active session, else the newest stored one (IOS-D6).
    func useLatestTranscript() {
        // STUB(WP-11)
    }
}
