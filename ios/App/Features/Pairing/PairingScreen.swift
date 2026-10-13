import SwiftUI

/// Pairing (screens 10): connect this iPhone to the relay on the PC, and show the state of that
/// connection.
///
/// It owns two modal surfaces: the QR sheet, and the "Replace this PC?" confirmation (architecture 5.9).
/// Every other message of this screen is the global alert.
struct PairingScreen: View {
    @Environment(PairingViewModel.self) private var model

    /// What the scanner delivered, kept until the sheet is gone: an alert or a change of the fields
    /// under a sheet that is still closing would be lost.
    @State private var scanResult: Result<String, QRScanFailure>? = nil
    /// Counts the codes that were read: the trigger of the success haptic.
    @State private var scannedCount = 0

    var body: some View {
        @Bindable var model = model

        ScreenScaffold {
            if model.isPaired {
                SavedPCPanel()
            }
            if model.optionsVisible {
                PairingQuickPanel()
                PairingManualPanel()
            }
            Panel("Privacy") {
                CaptionText(PairingCopy.privacy)
            }
        }
        .navigationTitle(model.title)
        .navigationBarTitleDisplayMode(.inline)
        .sheet(isPresented: $model.isScannerPresented) {
            scannerClosed()
        } content: {
            QRScanSheet(onResult: scanned, onCancel: cancelScan)
        }
        .alert("Replace this PC?", isPresented: replacementShown, presenting: model.replacementPrompt) { _ in
            Button("Replace", role: .destructive, action: replace)
            Button("Cancel", role: .cancel, action: keep)
        } message: { prompt in
            Text(verbatim: PairingCopy.replacementMessage(prompt))
        }
        .haptic(.success, trigger: scannedCount)
        .onAppear {
            model.appeared()
        }
    }

    /// True while the view model holds a replacement to confirm. Closing the alert drops it.
    private var replacementShown: Binding<Bool> {
        Binding(
            get: { model.replacementPrompt != nil },
            set: { isShown in
                if !isShown {
                    model.cancelReplacement()
                }
            }
        )
    }

    // MARK: - Scanner

    /// The scanner is done: the sheet closes first, and `scannerClosed` hands the result on.
    private func scanned(_ result: Result<String, QRScanFailure>) {
        // A result that arrives after "Cancel" belongs to a sheet that is already gone.
        guard model.isScannerPresented else { return }
        scanResult = result
        model.isScannerPresented = false
    }

    private func cancelScan() {
        scanResult = nil
        model.isScannerPresented = false
    }

    private func scannerClosed() {
        guard let result = scanResult else { return }
        scanResult = nil
        if case .success = result {
            scannedCount += 1
        }
        model.scannerFinished(result)
    }

    // MARK: - Replacement

    private func replace() {
        Task {
            await model.confirmReplacement()
        }
    }

    private func keep() {
        model.cancelReplacement()
    }
}

/// The texts of this screen (spec 01 section 8.4; decisions 6.1; architecture 5.9).
private enum PairingCopy {
    static let quickHint = "Open LiveCue Desktop on Windows and scan its QR. Without a camera, paste the pairing text from the PC, or enter the values manually."
    /// The form of a PC's address. It names no real host.
    static let addressPlaceholder = "https://<pc-name>.<network>.ts.net:11443/"
    /// S-13.
    static let privacy = "Your pairing is saved in iPhone Keychain and a protected file in this app, excluded from backups. Meta mode sends audio directly from this iPhone to Meta. Nemotron and Qwen3 send audio to your PC only. Voz and Parakeet transcribe on this iPhone. OpenRouter answers bypass the PC; Codex answers need it. Desktop activity stays in memory."

    /// The question, and one more line when the new host is not a Tailscale name.
    static func replacementMessage(_ prompt: PairingReplacement) -> String {
        let question = "Replace \(prompt.currentHost) with \(prompt.newHost)? Only do this with the QR shown by LiveCue Desktop on your own PC."
        if prompt.newHostIsTailscaleName {
            return question
        }
        return question + "\n\n\(prompt.newHost) is not a Tailscale address."
    }
}

/// Scan the QR, or paste the pairing text and read it. Both only fill the two fields below.
private struct PairingQuickPanel: View {
    @Environment(PairingViewModel.self) private var model

    var body: some View {
        @Bindable var model = model

        Panel("Quick pairing") {
            LCButton("Scan PC QR code", systemImage: "qrcode", size: .large, fullWidth: true, action: openScanner)
                .accessibilityIdentifier("scan-pairing-qr")
            CaptionText(PairingCopy.quickHint)
            LCTextField("", text: $model.pasteText, placeholder: "Paste pairing text", lines: 3...6, monospaced: true)
                .disabled(model.isVerifying)
            LCButton("Read pairing payload", kind: .secondary, action: read)
                .disabled(!model.canReadPayload)
        }
    }

    private func openScanner() {
        FormKeyboard.dismiss()
        model.isScannerPresented = true
    }

    private func read() {
        model.readPayload()
    }
}

/// The address and the token, and the one action that sends anything: "Verify and pair".
private struct PairingManualPanel: View {
    @Environment(PairingViewModel.self) private var model

    var body: some View {
        @Bindable var model = model

        Panel("Manual pairing") {
            LCTextField("PC address", text: $model.address, placeholder: PairingCopy.addressPlaceholder, isURL: true)
                .disabled(model.isVerifying)
            LCSecureField("Pairing token", text: $model.token, placeholder: "Pairing token")
                .disabled(model.isVerifying)
            // Busy while verifying: it keeps its look and ignores input, so it is not disabled then.
            LCButton("Verify and pair", fullWidth: true, isBusy: model.isVerifying, action: verify)
                .disabled(!model.canVerify && !model.isVerifying)
        }
    }

    private func verify() {
        FormKeyboard.dismiss()
        Task {
            await model.verifyAndPair()
        }
    }
}
