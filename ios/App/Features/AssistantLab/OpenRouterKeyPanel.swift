import SwiftUI
import UIKit

/// The OpenRouter key panel: field, save, state, verify and remove.
///
/// The key is typed into a secure field and never shown anywhere else. A successful save empties the
/// field and closes the keyboard.
struct OpenRouterKeyPanel: View {
    @Environment(AssistantLabViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        @Bindable var model = model

        Panel("OpenRouter API key") {
            LCSecureField("", text: $model.keyDraft, placeholder: "Paste API key", identifier: "openrouter-key")
            saveRow
            if model.hasKey {
                keyActions
            }
            if let message = model.keyMessage {
                AssistantMessageLine(message)
            }
            keyLink
            CaptionText(OpenRouterKeyPanel.disclosure)
        }
        .disabled(model.isLocked)
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
            .disabled(model.keyDraft.isEmpty)
            .accessibilityIdentifier("save-openrouter-key")
    }

    private var stateChip: some View {
        StatusChip(model.keyStateChip)
            .accessibilityIdentifier("openrouter-key-state")
    }

    /// "Verify key (no inference)" and "Remove key": a pair, stacked when the text is large.
    @ViewBuilder
    private var keyActions: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.controlGap) {
                verifyButton(fullWidth: true)
                removeButton(fullWidth: true)
            }
        } else {
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .center, spacing: theme.space.controlGap) {
                    verifyButton(fullWidth: false)
                    removeButton(fullWidth: false)
                }
                VStack(alignment: .leading, spacing: theme.space.controlGap) {
                    verifyButton(fullWidth: false)
                    removeButton(fullWidth: false)
                }
            }
        }
    }

    private func verifyButton(fullWidth: Bool) -> some View {
        LCButton(
            "Verify key (no inference)",
            kind: .secondary,
            fullWidth: fullWidth,
            isBusy: model.isVerifyingKey,
            action: verify
        )
        .accessibilityIdentifier("verify-openrouter-key")
    }

    private func removeButton(fullWidth: Bool) -> some View {
        LCButton("Remove key", kind: .destructive, fullWidth: fullWidth, action: remove)
            .accessibilityIdentifier("remove-openrouter-key")
    }

    /// The one link of this screen. Its address is fixed; nothing a service sent is ever opened.
    @ViewBuilder
    private var keyLink: some View {
        if let address = URL(string: OpenRouterKeyPanel.keysAddress) {
            Link(destination: address) {
                HStack(spacing: theme.space.inlineGap) {
                    LCGlyph("arrow.up.right.square", size: .regular)
                    Text(verbatim: "Get an OpenRouter API key")
                }
            }
            .buttonStyle(LCButtonStyle(kind: .quiet))
        }
    }

    // MARK: - Actions

    private func save() {
        model.saveKey()
        if model.keyDraft.isEmpty {
            // Saved: the field was emptied, and the keyboard closes.
            UIApplication.shared.sendAction(#selector(UIResponder.resignFirstResponder), to: nil, from: nil, for: nil)
        }
    }

    private func verify() {
        Task { await model.verifyKey() }
    }

    private func remove() {
        model.removeKey()
    }

    private static let keysAddress = "https://openrouter.ai/keys"

    /// S-10 of spec 01 section 8.4.
    private static let disclosure = "Stored only in this iPhone's Keychain and sent directly to OpenRouter—not your PC or GitHub. Each Assist sends transcript context and can incur API charges. Stop does not make another paid request. Meta cloud and on-iPhone transcription do not need a PC; PC speech models still do."
}
