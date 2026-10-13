import Foundation
import LiveCueCore
import Observation

/// Speech benchmark (screens 12): a manual word-error-rate worksheet. Nothing is saved.
@MainActor @Observable final class BenchmarkViewModel {
    var reference: String = "The quick brown fox jumps over the lazy dog"
    var hypothesis: String = ""

    private let session: SessionController
    private let history: HistoryModel

    init(session: SessionController, history: HistoryModel) {
        self.session = session
        self.history = history
    }

    /// One decimal and "%", or "–" while the reference is empty.
    var rateText: String {
        guard !reference.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return "–"
        }
        let rate = WordErrorRate.compute(reference: reference, hypothesis: hypothesis)
        return String(format: "%.1f%%", locale: Locale(identifier: "en_US_POSIX"), rate * 100)
    }

    var canUseLatest: Bool {
        latestSession != nil
    }

    var latestCaption: String {
        canUseLatest ? BenchmarkViewModel.fillsCaption : BenchmarkViewModel.emptyCaption
    }

    /// The active session, else the newest stored one (IOS-D6).
    func useLatestTranscript() {
        guard let latest = latestSession else { return }
        hypothesis = latest.spaceJoinedTranscript
    }

    /// The session in progress when there is one; History is newest first.
    private var latestSession: Session? {
        session.session ?? history.sessions.first
    }

    private static let fillsCaption = "Fills the box with your most recent conversation, active or saved."
    private static let emptyCaption = "No conversations yet."
}
