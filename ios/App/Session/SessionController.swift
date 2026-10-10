import Foundation
import LiveCueCore
import Observation

@MainActor struct SessionDependencies {
    let transcribers: any SpeechTranscriberFactory
    let answers: any AnswerServiceProviding
    let sessions: any SessionStoring
    let secrets: any SecretStoring
    let pairingStore: any PairingStoring
    let permissions: any PermissionProviding
    let clock: any AppClock
    let app: AppModel
    let activity: SessionActivity
    let speech: SpeechSettings
    let assistant: AssistantSettings
    let relay: RelayMonitor
    let models: OnDeviceModelManager
    let history: HistoryModel
}

/// The session state machine, its tick and its event handling (section 4.1).
@MainActor @Observable final class SessionController {
    // Observable state. Views read it; only this class writes it.
    private(set) var phase: SessionPhase = .idle
    /// nil in idle and preflight.
    private(set) var session: Session? = nil
    /// The provider this session started with.
    private(set) var provider: SpeechProvider? = nil
    private(set) var partialText: String = ""
    /// 0 while not listening.
    private(set) var level: Float = 0
    private(set) var elapsedSeconds: Int = 0
    private(set) var transcriberStatus: String = ""
    private(set) var notice: SessionNotice? = nil
    private(set) var pausedAutomatically: Bool = false
    private(set) var isAssisting: Bool = false
    private(set) var assistStage: AssistStage? = nil
    /// Kept after a failure until the next Assist starts.
    private(set) var streamingAnswer: String = ""
    private(set) var assistError: String? = nil
    /// The turn marked "Latest" in this session.
    private(set) var latestTurnID: UUID? = nil
    /// A previous request is remembered.
    private(set) var canRetry: Bool = false
    /// Increments on every entry into `listening` (haptic trigger).
    private(set) var listeningStartedCount: Int = 0
    /// Increments at the first streamed text or saved answer of an Assist.
    private(set) var answerArrivedCount: Int = 0
    private(set) var assistFailedCount: Int = 0

    /// preflight, connecting, pausing, resuming, ending
    var isTransitioning: Bool {
        // STUB(WP-08)
        false
    }

    /// session != nil
    var showsLiveScreen: Bool {
        // STUB(WP-08)
        session != nil
    }

    /// A session exists, not transitioning, not assisting.
    var controlsEnabled: Bool {
        // STUB(WP-08)
        false
    }

    /// "$0.00000 USD" for Meta sessions, else nil.
    var estimatedCost: String? {
        // STUB(WP-08)
        nil
    }

    private let dependencies: SessionDependencies

    init(_ dependencies: SessionDependencies) {
        // STUB(WP-08)
        self.dependencies = dependencies
    }

    // Intents. Each is single-flight: it returns at once when its guard fails.

    func start() async {
        // STUB(WP-08)
    }

    /// Pause when listening, Resume when paused.
    func togglePause() async {
        // STUB(WP-08)
    }

    func stop() async {
        // STUB(WP-08)
    }
}
