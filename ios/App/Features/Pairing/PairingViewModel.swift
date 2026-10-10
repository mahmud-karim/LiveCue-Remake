import Foundation
import LiveCueCore
import Observation

/// Pairing and the QR sheet (screens 10).
@MainActor @Observable final class PairingViewModel {
    var pasteText: String = ""
    var address: String = ""
    var token: String = ""
    /// The disclosure, when paired.
    var showsOptions: Bool = false
    var isScannerPresented: Bool = false

    init(pairing: PairingController, relay: RelayMonitor, app: AppModel) {
        // STUB(WP-11)
    }

    /// "PC connection" / "Pair Windows PC"
    var title: String { "PC connection" } // STUB(WP-11)
    var isPaired: Bool { false } // STUB(WP-11)
    /// Connected / Reconnecting / Pairing needs attention
    var savedStateChip: StatusChipModel { StatusChipModel("", tone: .neutral, mark: .none) } // STUB(WP-11)
    var savedAddress: String { "" } // STUB(WP-11)
    /// `relay.connectionMessage`, or the default text.
    var savedMessage: String { "" } // STUB(WP-11)
    var isChecking: Bool { false } // STUB(WP-11)
    /// Not paired, or `showsOptions`.
    var optionsVisible: Bool { true } // STUB(WP-11)
    var canReadPayload: Bool { false } // STUB(WP-11)
    /// Both fields not empty and not verifying.
    var canVerify: Bool { false } // STUB(WP-11)
    var isVerifying: Bool { false } // STUB(WP-11)
    /// Added by the review: replacing a saved PC by another host needs a confirmation. Non-nil while
    /// the confirmation is shown.
    var replacementPrompt: PairingReplacement? { nil } // STUB(WP-11)

    /// address = the saved endpoint; token and paste box emptied.
    func appeared() {
        // STUB(WP-11)
    }

    func reconnect() async {
        // STUB(WP-11)
    }

    /// Fills the two fields and clears the box, or alerts.
    func readPayload() {
        // STUB(WP-11)
    }

    /// Closes the sheet; success is handled as pasted text.
    func scannerFinished(_ result: Result<String, QRScanFailure>) {
        // STUB(WP-11)
    }

    /// On failure: alert with the message; the fields keep their text.
    func verifyAndPair() async {
        // STUB(WP-11)
    }

    /// Added by the review. Clears the prompt, then verifies and pairs.
    func confirmReplacement() async {
        // STUB(WP-11)
    }

    /// Added by the review. Clears the prompt; nothing was sent, the saved pairing stays.
    func cancelReplacement() {
        // STUB(WP-11)
    }
}
