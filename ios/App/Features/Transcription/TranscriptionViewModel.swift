import Foundation
import LiveCueCore
import Observation

/// Transcription (screens 7).
@MainActor @Observable final class TranscriptionViewModel {
    init(
        speech: SpeechSettings,
        assistant: AssistantSettings,
        relay: RelayMonitor,
        models: OnDeviceModelManager,
        readiness: SystemReadiness
    ) {
        // STUB(WP-11)
    }

    /// Catalogue order.
    var providers: [SpeechProvider] { SpeechProvider.allCases } // STUB(WP-11)
    var selected: SpeechProvider { .meta } // STUB(WP-11)
    var lockCaption: String? { nil } // STUB(WP-11)
    /// The display name: panel 2's title.
    var selectedTitle: String { "" } // STUB(WP-11)
    var setup: ProviderSetupState {
        // STUB(WP-11)
        ProviderSetupState(provider: .meta, hasMetaKey: false, pcModelLabel: "", isModelLoaded: false)
    }
    var explanation: String { "" } // STUB(WP-11)

    /// Stays on this screen.
    func select(_ provider: SpeechProvider) {
        // STUB(WP-11)
    }

    func appeared() {
        // STUB(WP-11)
    }

    func disappeared() {
        // STUB(WP-11)
    }
}
