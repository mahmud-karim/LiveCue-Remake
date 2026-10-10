import Foundation
import LiveCueCore

/// Why the speech provider cannot be changed right now.
enum ProviderLockReason: Equatable, Sendable {
    case preparingModel
    case pcModelChanging

    var caption: String {
        // STUB(WP-09)
        switch self {
        case .preparingModel: return "Available when the model has finished preparing."
        case .pcModelChanging: return "Available when the PC model has finished changing."
        }
    }
}

/// Whether the PC is needed, whether speech and answers are ready, and the provider lock (spec 01
/// section 0.2). It stores nothing: reading its properties inside a view body is tracked through the
/// owners it reads.
@MainActor final class SystemReadiness {
    init(speech: SpeechSettings, assistant: AssistantSettings, relay: RelayMonitor, models: OnDeviceModelManager) {
        // STUB(WP-09)
    }

    var needsPC: Bool {
        // STUB(WP-09)
        false
    }

    var speechReady: Bool {
        // STUB(WP-09)
        false
    }

    var answersReady: Bool {
        // STUB(WP-09)
        false
    }

    var systemReady: Bool {
        // STUB(WP-09)
        false
    }

    var providerLockReason: ProviderLockReason? {
        // STUB(WP-09)
        nil
    }
}
