import SwiftUI

/// Meta API key (screens 9): save, test and remove the Meta key.
struct MetaKeyScreen: View {
    @Environment(MetaKeyViewModel.self) private var model

    var body: some View {
        ScreenScaffold {
            MetaKeyPanel()
            Panel("How it works") {
                CaptionText(MetaKeyCopy.storage)
                CaptionText(MetaKeyCopy.withoutPC)
                CaptionText(MetaKeyCopy.testing)
            }
        }
        .navigationTitle("Meta API key")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            model.appeared()
        }
    }
}

/// The texts of this screen (spec 01 section 8.4).
private enum MetaKeyCopy {
    /// S-07, without the sentence decisions 6.1 removes.
    static let storage = "Your key is stored only in this iPhone's Keychain, not in app source, history, your PC, or GitHub. Audio connects directly to Meta over TLS."
    /// S-08.
    static let withoutPC = "For a completely PC-free conversation, also choose OpenRouter in Assistant models & timing and save its key. Codex CLI and PC-local speech still need the PC."
    /// S-09.
    static let testing = "Testing connects to Meta but sends no microphone audio. Actual transcription is billable. Disconnections pause recording; there are no automatic paid retries or fallback providers."
    static let dashboardTitle = "Open Meta developer dashboard"
    static let dashboardAddress = "https://dev.meta.ai/"
}

/// The key field, "Save key" and the state, the two actions on a saved key, the message and the link.
private struct MetaKeyPanel: View {
    @Environment(MetaKeyViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        @Bindable var model = model

        Panel("Meta Muse · direct cloud") {
            LCSecureField("", text: $model.keyDraft, placeholder: "Paste Meta API key", identifier: "meta-key")
            saveRow
            if model.hasKey {
                keyActions
            }
            if let message = model.message {
                MetaKeyMessageLine(message: message)
            }
            ExternalLinkButton(MetaKeyCopy.dashboardTitle, address: MetaKeyCopy.dashboardAddress)
        }
    }

    /// "Save key" and the state chip: side by side, or stacked at the accessibility text sizes.
    @ViewBuilder
    private var saveRow: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.controlGap) {
                saveButton(fullWidth: true)
                stateChip
            }
        } else {
            HStack(alignment: .center, spacing: theme.space.controlGap) {
                saveButton(fullWidth: false)
                Spacer(minLength: 0)
                stateChip
            }
        }
    }

    private func saveButton(fullWidth: Bool) -> some View {
        LCButton("Save key", fullWidth: fullWidth, action: save)
            .disabled(!model.canSave)
            .accessibilityIdentifier("save-meta-key")
    }

    private var stateChip: some View {
        StatusChip(model.stateChip)
            .accessibilityIdentifier("meta-key-state")
    }

    /// "Test connection (no audio)" and "Remove key", only with a saved key.
    private var keyActions: some View {
        FormButtonRow { fullWidth in
            LCButton(
                "Test connection (no audio)",
                kind: .secondary,
                fullWidth: fullWidth,
                isBusy: model.isTesting,
                busyTitle: "Connecting…",
                action: test
            )
            .accessibilityIdentifier("verify-meta-key")
            LCButton("Remove key", kind: .destructive, fullWidth: fullWidth, action: remove)
                .disabled(model.isTesting)
                .accessibilityIdentifier("remove-meta-key")
        }
    }

    private func save() {
        model.save()
        if model.keyDraft.isEmpty {
            // Saved: the field was emptied, and the keyboard closes.
            FormKeyboard.dismiss()
        }
    }

    private func test() {
        Task {
            await model.test()
        }
    }

    private func remove() {
        model.remove()
    }
}

/// The result of a key action as a line: a glyph and the text, in the success or the danger colour.
private struct MetaKeyMessageLine: View {
    let message: InlineMessage

    @Environment(\.theme) private var theme

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: theme.space.s2) {
            LCGlyph(message.isError ? "exclamationmark.triangle.fill" : "checkmark", size: .small)
                .foregroundStyle(message.isError ? theme.status.danger : theme.status.success)
            CaptionText(message.text, tone: message.isError ? StatusTone.danger : StatusTone.success)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("meta-key-message")
    }
}
