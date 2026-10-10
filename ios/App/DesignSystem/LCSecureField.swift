import SwiftUI

/// A labelled secure field with a reveal button (design system 4.7); never autocapitalised or
/// autocorrected. `identifier` goes on the field itself.
///
/// For keys, tokens and passwords. Hidden, it shows the system's bullets; revealed, it shows the value in
/// `type.mono` so that similar characters can be told apart. It hides the value again when the field
/// loses focus or the app leaves the foreground. The value is never an accessibility value: assistive
/// technology hears "Hidden" or "Shown".
struct LCSecureField: View {
    private enum Variant: Hashable {
        case hidden, revealed
    }

    private let label: String
    private let text: Binding<String>
    private let placeholder: String
    private let identifier: String?

    @Environment(\.theme) private var theme
    @Environment(\.scenePhase) private var scenePhase
    @FocusState private var focus: Variant?
    @State private var isRevealed = false
    /// True while focus moves from one of the two fields to the other, so that the move does not count
    /// as the field losing focus.
    @State private var isMovingFocus = false

    init(_ label: String, text: Binding<String>, placeholder: String, identifier: String? = nil) {
        self.label = label
        self.text = text
        self.placeholder = placeholder
        self.identifier = identifier
    }

    var body: some View {
        VStack(alignment: .leading, spacing: theme.component.fieldLabelGap) {
            LCFieldLabel(text: label)
            HStack(spacing: theme.space.s1) {
                entries
                IconButton(
                    systemImage: isRevealed ? "eye.slash" : "eye",
                    label: isRevealed ? "Hide \(spokenName)" : "Show \(spokenName)",
                    action: toggleReveal
                )
            }
            .padding(.leading, theme.space.s4)
            .padding(.trailing, theme.space.s1)
            .frame(minHeight: theme.size.field)
            .background { LCFieldSurface(isFocused: focus != nil) }
        }
        .onChange(of: focus) { _, newFocus in
            focusChanged(to: newFocus)
        }
        .onChange(of: scenePhase) { _, phase in
            if phase != .active {
                isRevealed = false
            }
        }
    }

    /// The secure field and the plain field, one over the other. Both stay in place and share the text and
    /// the focus state, so revealing never rebuilds the field that is being typed in.
    private var entries: some View {
        ZStack(alignment: .leading) {
            SecureField(title, text: text, prompt: prompt)
                .font(TextRole.body.font)
                .focused($focus, equals: Variant.hidden)
                .opacity(isRevealed ? 0 : 1)
                .allowsHitTesting(!isRevealed)
                .accessibilityValue(Text(verbatim: "Hidden"))
                .accessibilityHidden(isRevealed)
                .accessibilityIdentifier(hiddenIdentifier)
            TextField(title, text: text, prompt: prompt)
                .font(TextRole.mono.font)
                .focused($focus, equals: Variant.revealed)
                .opacity(isRevealed ? 1 : 0)
                .allowsHitTesting(isRevealed)
                .accessibilityValue(Text(verbatim: "Shown"))
                .accessibilityHidden(!isRevealed)
                .accessibilityIdentifier(revealedIdentifier)
        }
        .textFieldStyle(.plain)
        .foregroundStyle(theme.ink.primary)
        .tint(theme.accent.fill)
        .textInputAutocapitalization(.never)
        .autocorrectionDisabled()
        .privacySensitive()
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// Only the field that is showing carries the identifier.
    private var hiddenIdentifier: String {
        isRevealed ? "" : (identifier ?? "")
    }

    private var revealedIdentifier: String {
        isRevealed ? (identifier ?? "") : ""
    }

    private var title: String {
        label.isEmpty ? placeholder : label
    }

    /// What the reveal button calls the field. Never the value.
    private var spokenName: String {
        label.isEmpty ? "value" : label
    }

    private var prompt: Text {
        Text(verbatim: placeholder).foregroundStyle(theme.ink.secondary)
    }

    /// Revealing keeps the keyboard up: focus moves to the field that is now visible.
    private func toggleReveal() {
        let wasFocused = focus != nil
        isRevealed.toggle()
        if wasFocused {
            isMovingFocus = true
            focus = isRevealed ? Variant.revealed : Variant.hidden
        }
    }

    private func focusChanged(to newFocus: Variant?) {
        if newFocus != nil {
            isMovingFocus = false
        } else if !isMovingFocus {
            isRevealed = false
        }
    }
}
