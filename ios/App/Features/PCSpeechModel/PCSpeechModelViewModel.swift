import Foundation
import LiveCueCore
import Observation

/// PC speech model (screens 8).
@MainActor @Observable final class PCSpeechModelViewModel {
    init(speech: SpeechSettings, relay: RelayMonitor) {
        // STUB(WP-11)
    }

    /// The provider's display name.
    var title: String { "" } // STUB(WP-11)
    /// "Stopped", "Starting", "Ready", "Stopping", "Error", "Unavailable"
    var stateText: String { "Unavailable" } // STUB(WP-11)
    var isChanging: Bool { false } // STUB(WP-11)
    var isError: Bool { false } // STUB(WP-11)
    /// "N s" while changing.
    var elapsedText: String? { nil } // STUB(WP-11)
    var message: String { "" } // STUB(WP-11)
    /// "Currently loaded: <id>"
    var otherLoadedModel: String? { nil } // STUB(WP-11)
    /// Ready and busy: the chip "In use".
    var isInUse: Bool { false } // STUB(WP-11)
    /// Not locked and not already ready for this provider.
    var canStart: Bool { false } // STUB(WP-11)
    /// Not locked, status known, not stopped, the loaded model is this provider.
    var canStop: Bool { false } // STUB(WP-11)

    func start() async {
        // STUB(WP-11)
    }

    func stop() async {
        // STUB(WP-11)
    }

    func refresh() async {
        // STUB(WP-11)
    }

    func appeared() {
        // STUB(WP-11)
    }

    func disappeared() {
        // STUB(WP-11)
    }
}
