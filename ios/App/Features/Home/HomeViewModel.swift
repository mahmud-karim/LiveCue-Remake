import Foundation
import LiveCueCore
import Observation

/// What the Transcription row of Home shows under its title.
enum HomeTranscriptionDetail: Equatable {
    /// "Key saved · direct" / "Add API key"
    case meta(state: String, isReady: Bool)
    /// The home label of spec 01 section 3.7.
    case pcModel(label: String, canStart: Bool, isChanging: Bool)
    /// The model description, or "Download a model to start".
    case onDevice(text: String)
}

enum HomeStartControl: Equatable {
    /// `caption` is set when a PC model cannot start.
    case start(isBusy: Bool, isEnabled: Bool, caption: String?)
    /// Links to Model Library.
    case download
}

/// Home (screens 3).
@MainActor @Observable final class HomeViewModel {
    init(
        session: SessionController,
        speech: SpeechSettings,
        assistant: AssistantSettings,
        relay: RelayMonitor,
        models: OnDeviceModelManager,
        readiness: SystemReadiness,
        history: HistoryModel
    ) {
        // STUB(WP-10)
    }

    var captionLine1: String { "" } // STUB(WP-10)
    var captionLine2: String { "" } // STUB(WP-10)
    /// The five rows of screens 3.
    var statusChip: StatusChipModel { StatusChipModel("", tone: .neutral, mark: .none) } // STUB(WP-10)
    /// System not ready and History empty.
    var showsFirstRunBanner: Bool { false } // STUB(WP-10)
    /// "Ready" (success, check) / "Setup" (warning)
    var readinessChip: StatusChipModel { StatusChipModel("", tone: .neutral, mark: .none) } // STUB(WP-10)
    var transcriptionValue: String { "" } // STUB(WP-10)
    /// "Not configured"
    var transcriptionValueNeedsAttention: Bool { false } // STUB(WP-10)
    var transcriptionDetail: HomeTranscriptionDetail { .onDevice(text: "") } // STUB(WP-10)
    /// "PC connection" / "Pair Windows PC"
    var pairingTitle: String { "" } // STUB(WP-10)
    /// "Not required" / "Paired" / "Pairing required"
    var pairingValue: String { "" } // STUB(WP-10)
    var pairingSubtitle: String { "" } // STUB(WP-10)
    var pairingNeedsAttention: Bool { false } // STUB(WP-10)
    /// The model id, or "Choose an OpenRouter model".
    var assistantSubtitle: String { "" } // STUB(WP-10)
    var assistantNeedsAttention: Bool { false } // STUB(WP-10)
    var startControl: HomeStartControl { .start(isBusy: false, isEnabled: false, caption: nil) } // STUB(WP-10)

    /// `session.start()`
    func start() async {
        // STUB(WP-10)
    }

    /// `relay.beginPCModelObservation()`
    func appeared() {
        // STUB(WP-10)
    }

    func disappeared() {
        // STUB(WP-10)
    }
}
