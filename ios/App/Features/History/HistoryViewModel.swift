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
        id = session.id
        title = session.title
        dateText = session.startedAt.formatted(date: .abbreviated, time: .shortened)
        countsText = "\(session.segments.count) transcript segments · \(session.assistantTurns.count) assists"
        usage = HistoryRowContent.usageLabel(of: session.transcriptionUsage)
    }

    /// Meta shows the estimated cost, every other provider "Local". A session without recorded usage
    /// shows nothing: never a made-up zero.
    private static func usageLabel(of usage: TranscriptionUsage?) -> HistoryUsageLabel? {
        guard let usage else { return nil }
        let ending = usage.isIncomplete ? " · incomplete" : ""
        if usage.provider == SpeechProvider.meta.rawValue {
            return .meta("\(SpeechProvider.meta.displayName) · \(SessionTexts.cost(usage))\(ending)")
        }
        let name = SpeechProvider(rawValue: usage.provider)?.displayName ?? "On-device"
        return .local("\(name) · Local\(ending)")
    }
}

/// History (screens 13): every stored conversation, newest first, and the deletion of one.
@MainActor @Observable final class HistoryViewModel {
    /// The row whose confirmation is showing.
    var pendingDeleteID: UUID? = nil

    private let history: HistoryModel

    init(history: HistoryModel) {
        self.history = history
    }

    /// Newest start first, as `HistoryModel` keeps them.
    var rows: [HistoryRowContent] {
        history.sessions.map { HistoryRowContent(session: $0) }
    }

    var isEmpty: Bool {
        history.sessions.isEmpty
    }

    /// The swipe action: nothing is deleted before the confirmation.
    func requestDelete(_ id: UUID) {
        pendingDeleteID = id
    }

    func confirmDelete() async {
        guard let id = pendingDeleteID else { return }
        pendingDeleteID = nil
        await history.delete(id: id)
    }

    func cancelDelete() {
        pendingDeleteID = nil
    }
}
