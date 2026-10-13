import Foundation
import LiveCueCore
import Observation

/// What the Transcription row of Home shows under its title.
enum HomeTranscriptionDetail: Equatable {
    /// "Key saved · direct" / "Add API key"
    case meta(state: String, isReady: Bool)
    /// The home label of spec 01 section 3.7.
    case pcModel(label: String, canStart: Bool, isChanging: Bool)
    /// The model description, or "Download a model to start"; for Apple also why it cannot be used.
    case onDevice(text: String)
}

enum HomeStartControl: Equatable {
    /// `caption` is set when a PC model cannot start.
    case start(isBusy: Bool, isEnabled: Bool, caption: String?)
    /// Links to Model Library.
    case download
}

/// Home (screens 3). It stores nothing: every property is read from the state owners, so a view that
/// reads one is redrawn when the owner changes.
@MainActor @Observable final class HomeViewModel {
    private let session: SessionController
    private let speech: SpeechSettings
    private let assistant: AssistantSettings
    private let relay: RelayMonitor
    private let models: OnDeviceModelManager
    private let readiness: SystemReadiness
    private let history: HistoryModel

    init(
        session: SessionController,
        speech: SpeechSettings,
        assistant: AssistantSettings,
        relay: RelayMonitor,
        models: OnDeviceModelManager,
        readiness: SystemReadiness,
        history: HistoryModel
    ) {
        self.session = session
        self.speech = speech
        self.assistant = assistant
        self.relay = relay
        self.models = models
        self.readiness = readiness
        self.history = history
    }

    var captionLine1: String {
        speech.provider.homeCaption
    }

    var captionLine2: String {
        switch assistant.configuration.provider {
        case .codex: return "Tap Assist for an answer from your PC."
        case .openrouter: return "Tap Assist for an answer from OpenRouter."
        }
    }

    /// The five rows of screens 3.
    var statusChip: StatusChipModel {
        guard readiness.needsPC else {
            let text = speech.provider == .meta ? "Cloud direct · no PC" : "OpenRouter direct"
            return StatusChipModel(text, tone: .success, mark: .dot)
        }
        if relay.isOnline {
            return StatusChipModel("PC online", tone: .success, mark: .dot)
        }
        if relay.isPaired {
            return StatusChipModel("PC reconnecting", tone: .warning, mark: .spinner)
        }
        return StatusChipModel("PC offline", tone: .neutral, mark: .ring)
    }

    /// System not ready and History empty.
    var showsFirstRunBanner: Bool {
        !readiness.systemReady && history.sessions.isEmpty
    }

    /// "Ready" (success, check) / "Setup" (warning)
    var readinessChip: StatusChipModel {
        if readiness.systemReady {
            return StatusChipModel("Ready", tone: .success, mark: .check)
        }
        return StatusChipModel("Setup", tone: .warning, mark: .dot)
    }

    var transcriptionValue: String {
        let provider = speech.provider
        guard provider.isOnDevice else {
            return provider.displayName
        }
        return models.loadedModel(for: provider)?.displayName ?? "Not configured"
    }

    /// "Not configured"
    var transcriptionValueNeedsAttention: Bool {
        let provider = speech.provider
        return provider.isOnDevice && models.loadedModel(for: provider) == nil
    }

    var transcriptionDetail: HomeTranscriptionDetail {
        let provider = speech.provider
        if provider.isCloud {
            let hasKey = speech.hasMetaKey
            return .meta(state: hasKey ? "Key saved · direct" : "Add API key", isReady: hasKey)
        }
        if provider.isPCLocal {
            // The spinner belongs to a start or a stop the PC itself reports.
            let isChanging = relay.isPaired && relay.isOnline && relay.pcModelStatus?.isChanging == true
            return .pcModel(
                label: relay.homeLabel(for: provider),
                canStart: relay.canStartSession(for: provider),
                isChanging: isChanging
            )
        }
        if let model = models.loadedModel(for: provider) {
            return .onDevice(text: model.quality)
        }
        let note = provider == .apple ? models.systemSpeechNote : nil
        return .onDevice(text: note ?? "Download a model to start")
    }

    /// "PC connection" / "Pair Windows PC"
    var pairingTitle: String {
        relay.isPaired ? "PC connection" : "Pair Windows PC"
    }

    /// "Not required" / "Paired" / "Pairing required"
    var pairingValue: String {
        guard readiness.needsPC else {
            return "Not required"
        }
        return relay.isPaired ? "Paired" : "Pairing required"
    }

    var pairingSubtitle: String {
        guard readiness.needsPC else {
            return "Optional for current setup"
        }
        guard relay.isPaired else {
            return "Required"
        }
        return relay.isOnline ? "Saved · connected" : "Saved · reconnecting"
    }

    var pairingNeedsAttention: Bool {
        readiness.needsPC && !relay.isPaired
    }

    /// The model id, or "Choose an OpenRouter model".
    var assistantSubtitle: String {
        let model = assistant.configuration.model
        return model.isEmpty ? "Choose an OpenRouter model" : model
    }

    var assistantNeedsAttention: Bool {
        assistant.configuration.model.isEmpty
    }

    var startControl: HomeStartControl {
        let provider = speech.provider
        if provider.isOnDevice, !models.isLoaded(for: provider) {
            return .download
        }
        let isBusy = session.phase == .preflight
        if provider.isPCLocal, !relay.canStartSession(for: provider) {
            let caption = "PC speech model: \(relay.homeLabel(for: provider)). Open Transcription to start it."
            return .start(isBusy: isBusy, isEnabled: false, caption: caption)
        }
        return .start(isBusy: isBusy, isEnabled: true, caption: nil)
    }

    /// `session.start()`
    func start() async {
        await session.start()
    }

    /// `relay.beginPCModelObservation()`
    func appeared() {
        relay.beginPCModelObservation()
    }

    func disappeared() {
        relay.endPCModelObservation()
    }
}
