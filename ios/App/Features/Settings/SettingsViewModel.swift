import Foundation
import LiveCueCore
import Observation

/// Settings (screens 6, with the About panel).
@MainActor @Observable final class SettingsViewModel {
    init(
        speech: SpeechSettings,
        assistant: AssistantSettings,
        relay: RelayMonitor,
        models: OnDeviceModelManager,
        readiness: SystemReadiness
    ) {
        // STUB(WP-11)
    }

    var providerName: String { "" } // STUB(WP-11)
    /// `readiness.providerLockReason?.caption`
    var providerLockCaption: String? { nil } // STUB(WP-11)
    var setup: ProviderSetupState {
        // STUB(WP-11)
        ProviderSetupState(provider: .meta, hasMetaKey: false, pcModelLabel: "", isModelLoaded: false)
    }
    /// S-01 / S-02 / S-03
    var explanation: String { "" } // STUB(WP-11)
    var assistantSubtitle: String { "" } // STUB(WP-11)

    /// `assistant.instruction`
    var instruction: String {
        get {
            // STUB(WP-11)
            ""
        }
        set {
            // STUB(WP-11)
        }
    }

    var pairingTitle: String { "" } // STUB(WP-11)
    /// "Saved · connected" / "Saved · reconnecting"
    var pairingSubtitle: String? { nil } // STUB(WP-11)
    /// `AppVersion.marketing`
    var versionText: String { AppVersion.marketing } // STUB(WP-11)

    /// `relay.beginPCModelObservation()`
    func appeared() {
        // STUB(WP-11)
    }

    func disappeared() {
        // STUB(WP-11)
    }
}
