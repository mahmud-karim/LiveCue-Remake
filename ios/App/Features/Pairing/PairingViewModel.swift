import Foundation
import LiveCueCore
import Observation

/// Pairing and the QR sheet (screens 10).
///
/// Three ways in (scan, paste, type) end in the same two fields. Nothing is sent or saved before
/// "Verify and pair", and nothing before "Replace" when another host would replace the saved PC.
@MainActor @Observable final class PairingViewModel {
    var pasteText: String = ""
    var address: String = ""
    var token: String = ""
    /// The disclosure, when paired.
    var showsOptions: Bool = false
    var isScannerPresented: Bool = false
    /// Added by the review: replacing a saved PC by another host needs a confirmation. Non-nil while
    /// the confirmation is shown.
    private(set) var replacementPrompt: PairingReplacement? = nil

    private let pairing: PairingController
    private let relay: RelayMonitor
    private let app: AppModel

    init(pairing: PairingController, relay: RelayMonitor, app: AppModel) {
        self.pairing = pairing
        self.relay = relay
        self.app = app
    }

    /// "PC connection" / "Pair Windows PC"
    var title: String {
        relay.isPaired ? "PC connection" : "Pair Windows PC"
    }

    var isPaired: Bool {
        relay.isPaired
    }

    /// Connected / Reconnecting / Pairing needs attention
    var savedStateChip: StatusChipModel {
        switch relay.savedPCState {
        case .connected:
            return StatusChipModel("Connected", tone: .success, mark: .check)
        case .reconnecting:
            return StatusChipModel("Reconnecting", tone: .warning, mark: .spinner)
        case .needsAttention:
            return StatusChipModel("Pairing needs attention", tone: .danger, mark: .alert)
        }
    }

    var savedAddress: String {
        relay.pairing?.endpoint ?? ""
    }

    /// `relay.connectionMessage`, or the default text.
    var savedMessage: String {
        relay.connectionMessage ?? PairingViewModel.defaultSavedMessage
    }

    var isChecking: Bool {
        relay.isCheckingHealth
    }

    /// Not paired, or `showsOptions`.
    var optionsVisible: Bool {
        !relay.isPaired || showsOptions
    }

    var canReadPayload: Bool {
        !pasteText.isEmpty
    }

    /// Both fields not empty and not verifying.
    var canVerify: Bool {
        !address.isEmpty && !token.isEmpty && !pairing.isVerifying
    }

    var isVerifying: Bool {
        pairing.isVerifying
    }

    /// address = the saved endpoint; token and paste box emptied. The pairing options start closed.
    func appeared() {
        address = relay.pairing?.endpoint ?? ""
        token = ""
        pasteText = ""
        showsOptions = false
        replacementPrompt = nil
    }

    func reconnect() async {
        await relay.checkHealthNow()
    }

    /// Fills the two fields and clears the box, or alerts.
    func readPayload() {
        read(pasteText)
    }

    /// Closes the sheet; success is handled as pasted text.
    func scannerFinished(_ result: Result<String, QRScanFailure>) {
        isScannerPresented = false
        switch result {
        case .success(let text):
            read(text)
        case .failure(let failure):
            app.presentAlert(failure.message)
        }
    }

    /// On failure: alert with the message; the fields keep their text.
    ///
    /// With a saved PC and another host in the address field nothing is sent: the confirmation is shown
    /// first.
    func verifyAndPair() async {
        if let replacement = pairing.replacement(forAddress: address) {
            replacementPrompt = replacement
            return
        }
        await pair()
    }

    /// Added by the review. Clears the prompt, then verifies and pairs.
    func confirmReplacement() async {
        replacementPrompt = nil
        await pair()
    }

    /// Added by the review. Clears the prompt; nothing was sent, the saved pairing stays.
    func cancelReplacement() {
        replacementPrompt = nil
    }

    // MARK: - Pieces

    /// A payload that was pasted or scanned: only the two fields change. Nothing is sent or saved.
    private func read(_ text: String) {
        do {
            let payload = try pairing.readPayload(text)
            address = payload.endpoint
            token = payload.token
            pasteText = ""
        } catch {
            app.presentAlert(PairingViewModel.message(for: error))
        }
    }

    private func pair() async {
        do {
            try await pairing.verifyAndPair(address: address, token: token)
            // Paired: the screen shows the Saved PC panel, and the token is not kept on screen.
            address = relay.pairing?.endpoint ?? address
            token = ""
            pasteText = ""
            showsOptions = false
        } catch is CancellationError {
            // Never shown.
        } catch {
            app.presentAlert(PairingViewModel.message(for: error))
        }
    }

    /// The text of a failed read or a failed "Verify and pair". It never starts with "Your pairing is
    /// saved.": that prefix belongs to health checks of the saved pairing (IOS-B10).
    private static func message(for error: any Error) -> String {
        if let failure = error as? RelayFailure {
            return RelayMessages.text(for: failure)
        }
        if let invalid = error as? PairingPayloadError {
            return invalid.errorDescription ?? PairingViewModel.notSavedMessage
        }
        return PairingViewModel.notSavedMessage
    }

    private static let defaultSavedMessage = "Your pairing is saved. The app reconnects automatically after restarting."
    /// The PC accepted the pairing and the phone could not store it. The store's own error has no text
    /// that may be shown.
    private static let notSavedMessage = "This iPhone could not save the pairing. Try again."
}
