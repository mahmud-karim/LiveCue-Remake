import Foundation
import LiveCueCore
import Observation

enum TranscriptPreview: Equatable {
    /// Partial text, with the caret.
    case interim(String)
    /// The last committed segment.
    case committed(String)
    case placeholder(String)
}

struct InProgressState: Equatable {
    /// The stage label, or "Incomplete answer".
    let label: String
    let text: String
    let showsSpinner: Bool
    /// An error card is above it.
    let isIncomplete: Bool
}

/// The live session screen (screens 4).
@MainActor @Observable final class LiveSessionViewModel {
    /// False when a session starts.
    var isTranscriptExpanded: Bool = false

    init(session: SessionController, assistant: AssistantSettings, clipboard: any ClipboardWriting, toasts: ToastCenter) {
        // STUB(WP-10)
    }

    /// Section 4.1.1, by phase.
    var stateChip: StatusChipModel { StatusChipModel("", tone: .neutral, mark: .none) } // STUB(WP-10)
    var meterState: LevelMeterState { .idle } // STUB(WP-10)
    var level: Float { 0 } // STUB(WP-10)
    /// "MM:SS"
    var elapsedText: String { "00:00" } // STUB(WP-10)
    /// "4 minutes 12 seconds"
    var elapsedSpokenText: String { "" } // STUB(WP-10)
    /// The display name; Meta adds " · est. transcription".
    var providerLine: String { "" } // STUB(WP-10)
    /// Meta only.
    var costText: String? { nil } // STUB(WP-10)
    /// `session.transcriberStatus`
    var statusLine: String { "" } // STUB(WP-10)
    /// The text after the last "/", or "Choose model".
    var assistantModelText: String { "" } // STUB(WP-10)
    var notice: SessionNotice? { nil } // STUB(WP-10)
    var preview: TranscriptPreview { .placeholder("") } // STUB(WP-10)
    var segments: [TranscriptSegment] { [] } // STUB(WP-10)
    var partialText: String { "" } // STUB(WP-10)
    var errorText: String? { nil } // STUB(WP-10)
    /// While assisting, or while streamed text is left over.
    var inProgress: InProgressState? { nil } // STUB(WP-10)
    /// The turn whose id is `session.latestTurnID`.
    var latestTurn: AssistantTurn? { nil } // STUB(WP-10)
    /// Newest first.
    var earlierTurns: [AssistantTurn] { [] } // STUB(WP-10)
    var showsEmptyState: Bool { true } // STUB(WP-10)
    /// `session.canRetry` and not assisting.
    var canRetry: Bool { false } // STUB(WP-10)
    var isPaused: Bool { false } // STUB(WP-10)
    var isAssisting: Bool { false } // STUB(WP-10)
    var isTransitioning: Bool { false } // STUB(WP-10)
    /// Changes when an answer lands, an Assist starts or an error appears.
    var feedScrollTrigger: Int { 0 } // STUB(WP-10)

    func togglePause() async {
        // STUB(WP-10)
    }

    func assist() async {
        // STUB(WP-10)
    }

    func retry() async {
        // STUB(WP-10)
    }

    func stop() async {
        // STUB(WP-10)
    }

    /// Clipboard, then the toast "Copied".
    func copy(_ turn: AssistantTurn) {
        // STUB(WP-10)
    }
}
