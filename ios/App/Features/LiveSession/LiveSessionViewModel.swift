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

/// The live session screen (screens 4). Apart from the transcript's expansion it stores nothing: every
/// property is read from the session controller, so a view that reads one follows the session.
@MainActor @Observable final class LiveSessionViewModel {
    private let session: SessionController
    private let assistant: AssistantSettings
    private let clipboard: any ClipboardWriting
    private let toasts: ToastCenter

    /// The session whose transcript is expanded. A new session has another id, so it starts collapsed.
    private var expandedSessionID: UUID? = nil

    /// False when a session starts.
    var isTranscriptExpanded: Bool {
        get {
            guard let expandedSessionID else { return false }
            return expandedSessionID == session.session?.id
        }
        set {
            expandedSessionID = newValue ? session.session?.id : nil
        }
    }

    init(session: SessionController, assistant: AssistantSettings, clipboard: any ClipboardWriting, toasts: ToastCenter) {
        self.session = session
        self.assistant = assistant
        self.clipboard = clipboard
        self.toasts = toasts
    }

    /// Section 4.1.1, by phase.
    var stateChip: StatusChipModel {
        LiveSessionViewModel.chip(for: session.phase)
    }

    var meterState: LevelMeterState {
        LiveSessionViewModel.meterState(for: session.phase)
    }

    var level: Float {
        session.level
    }

    /// "MM:SS"
    var elapsedText: String {
        LiveSessionViewModel.elapsedText(seconds: session.elapsedSeconds)
    }

    /// "4 minutes 12 seconds"
    var elapsedSpokenText: String {
        LiveSessionViewModel.spokenElapsed(seconds: session.elapsedSeconds)
    }

    /// The display name; Meta adds " · est. transcription".
    var providerLine: String {
        guard let provider = session.provider else {
            return ""
        }
        if provider == .meta {
            return provider.displayName + " · est. transcription"
        }
        return provider.displayName
    }

    /// Meta only.
    var costText: String? {
        session.estimatedCost
    }

    /// `session.transcriberStatus`
    var statusLine: String {
        session.transcriberStatus
    }

    /// The text after the last "/", or "Choose model".
    var assistantModelText: String {
        let model = assistant.configuration.model
        guard !model.isEmpty else {
            return "Choose model"
        }
        guard let slash = model.lastIndex(of: "/") else {
            return model
        }
        return String(model[model.index(after: slash)...])
    }

    var notice: SessionNotice? {
        session.notice
    }

    var preview: TranscriptPreview {
        let partial = session.partialText
        if !partial.isEmpty {
            return .interim(partial)
        }
        if let last = session.session?.segments.last {
            return .committed(last.text)
        }
        return .placeholder(placeholder)
    }

    var segments: [TranscriptSegment] {
        session.session?.segments ?? []
    }

    var partialText: String {
        session.partialText
    }

    var errorText: String? {
        session.assistError
    }

    /// While assisting, or while streamed text is left over.
    var inProgress: InProgressState? {
        let text = session.streamingAnswer
        if session.isAssisting {
            let label = session.assistStage?.label ?? AssistStage.thinking.label
            return InProgressState(label: label, text: text, showsSpinner: true, isIncomplete: false)
        }
        guard !text.isEmpty else {
            return nil
        }
        // Text a failed Assist left behind: the error card is above it.
        return InProgressState(label: "Incomplete answer", text: text, showsSpinner: false, isIncomplete: true)
    }

    /// The turn whose id is `session.latestTurnID`.
    var latestTurn: AssistantTurn? {
        guard let id = session.latestTurnID else {
            return nil
        }
        return session.session?.assistantTurns.first { $0.id == id }
    }

    /// Newest first.
    var earlierTurns: [AssistantTurn] {
        let latestID = latestTurn?.id
        let turns = session.session?.assistantTurns ?? []
        return turns.filter { $0.id != latestID }.reversed()
    }

    var showsEmptyState: Bool {
        let turns = session.session?.assistantTurns ?? []
        return turns.isEmpty && !session.isAssisting && session.assistError == nil && session.streamingAnswer.isEmpty
    }

    /// `session.canRetry` and not assisting.
    var canRetry: Bool {
        session.canRetry && !session.isAssisting
    }

    var isPaused: Bool {
        session.phase == .paused
    }

    var isAssisting: Bool {
        session.isAssisting
    }

    var isTransitioning: Bool {
        session.isTransitioning
    }

    /// Changes when an answer lands, an Assist starts or an error appears.
    var feedScrollTrigger: Int {
        let turns = session.session?.assistantTurns.count ?? 0
        let assisting = session.isAssisting ? 2 : 0
        let failed = session.assistError == nil ? 0 : 1
        return turns * 4 + assisting + failed
    }

    func togglePause() async {
        await session.togglePause()
    }

    func assist() async {
        await session.assist()
    }

    func retry() async {
        await session.retrySameText()
    }

    func stop() async {
        await session.stop()
    }

    /// Clipboard, then the toast "Copied".
    func copy(_ turn: AssistantTurn) {
        clipboard.copy(turn.answer)
        toasts.show("Copied")
    }

    // MARK: - Texts by value

    /// The chip of each phase (architecture 4.1.1). The live screen is not shown in `idle` and
    /// `preflight`; they take the chip of the transition next to them.
    static func chip(for phase: SessionPhase) -> StatusChipModel {
        switch phase {
        case .preflight, .connecting, .resuming:
            return StatusChipModel("Connecting…", tone: .live, mark: .spinner)
        case .listening:
            return StatusChipModel("Listening", tone: .live, mark: .pulsingDot)
        case .pausing:
            return StatusChipModel("Pausing…", tone: .warning, mark: .spinner)
        case .paused:
            return StatusChipModel("Paused", tone: .warning, mark: .pause)
        case .ending, .idle:
            return StatusChipModel("Stopping…", tone: .neutral, mark: .spinner)
        }
    }

    static func meterState(for phase: SessionPhase) -> LevelMeterState {
        switch phase {
        case .listening:
            return .listening
        case .preflight, .connecting, .resuming:
            return .idle
        case .pausing, .paused, .ending, .idle:
            return .paused
        }
    }

    /// "MM:SS". The minutes keep counting past 59.
    static func elapsedText(seconds: Int) -> String {
        let total = max(0, seconds)
        return String(format: "%02d:%02d", locale: Locale(identifier: "en_US_POSIX"), total / 60, total % 60)
    }

    /// "4 minutes 12 seconds"; the minutes are left out while there are none.
    static func spokenElapsed(seconds: Int) -> String {
        let total = max(0, seconds)
        let minutes = total / 60
        let rest = total % 60
        let secondsText = rest == 1 ? "1 second" : "\(rest) seconds"
        guard minutes > 0 else {
            return secondsText
        }
        let minutesText = minutes == 1 ? "1 minute" : "\(minutes) minutes"
        return "\(minutesText) \(secondsText)"
    }

    /// The text of the transcript panel while nothing was heard yet.
    private var placeholder: String {
        guard let provider = session.provider else {
            return ""
        }
        switch session.phase {
        case .preflight, .connecting, .resuming:
            return provider.connectingPlaceholder
        case .idle, .listening, .pausing, .paused, .ending:
            return provider.listeningPlaceholder
        }
    }
}
