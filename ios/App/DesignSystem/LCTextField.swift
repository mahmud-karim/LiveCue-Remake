import SwiftUI

/// A labelled text field (design system 4.6). `identifier` goes on the text field itself.
///
/// The label sits above the field and stays visible; the placeholder is an example, never the label. The
/// outline is what identifies an empty field, so a field belongs in a panel, a card or a sheet, never
/// directly on the ground (rule C-2). Focus shows as a thicker accent outline.
///
/// With an empty `label` no label is drawn and the placeholder is what assistive technology reads.
struct LCTextField: View {
    private let label: String
    private let text: Binding<String>
    private let placeholder: String
    private let lines: ClosedRange<Int>?
    private let monospaced: Bool
    private let isURL: Bool
    private let identifier: String?

    @Environment(\.theme) private var theme
    @FocusState private var isFocused: Bool

    init(
        _ label: String,
        text: Binding<String>,
        placeholder: String = "",
        lines: ClosedRange<Int>? = nil,
        monospaced: Bool = false,
        isURL: Bool = false,
        identifier: String? = nil
    ) {
        self.label = label
        self.text = text
        self.placeholder = placeholder
        self.lines = lines
        self.monospaced = monospaced
        self.isURL = isURL
        self.identifier = identifier
    }

    var body: some View {
        VStack(alignment: .leading, spacing: theme.component.fieldLabelGap) {
            LCFieldLabel(text: label)
            field
                .textFieldStyle(.plain)
                .font(monospaced ? TextRole.mono.font : TextRole.body.font)
                .foregroundStyle(theme.ink.primary)
                .tint(theme.accent.fill)
                .focused($isFocused)
                .keyboardType(isURL ? .URL : .default)
                .textContentType(isURL ? UITextContentType.URL : nil)
                .textInputAutocapitalization(plainEntry ? TextInputAutocapitalization.never : TextInputAutocapitalization.sentences)
                .autocorrectionDisabled(plainEntry)
                .accessibilityIdentifier(identifier ?? "")
                .padding(.horizontal, theme.space.s4)
                .padding(.vertical, theme.space.s3)
                .frame(
                    maxWidth: .infinity,
                    minHeight: lines == nil ? theme.size.field : theme.component.multilineField,
                    alignment: lines == nil ? .leading : .topLeading
                )
                .background { LCFieldSurface(isFocused: isFocused) }
                // The padding around the text is part of the field: a tap there focuses it too.
                .contentShape(Rectangle())
                .onTapGesture { isFocused = true }
        }
    }

    /// Addresses and codes are typed as they are: no capitals, no corrections.
    private var plainEntry: Bool {
        isURL || monospaced
    }

    private var title: String {
        label.isEmpty ? placeholder : label
    }

    private var prompt: Text {
        Text(verbatim: placeholder).foregroundStyle(theme.ink.secondary)
    }

    @ViewBuilder
    private var field: some View {
        if let lines {
            TextField(title, text: text, prompt: prompt, axis: .vertical)
                .lineLimit(lines)
        } else {
            TextField(title, text: text, prompt: prompt)
        }
    }
}

/// The label above a field: `type.caption` semibold in `ink.secondary`. It is hidden from accessibility
/// because the field carries the same words as its own label.
struct LCFieldLabel: View {
    let text: String

    @Environment(\.theme) private var theme

    var body: some View {
        if !text.isEmpty {
            Text(verbatim: text)
                .font(TextRole.caption.font.weight(.semibold))
                .foregroundStyle(theme.ink.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.leading, theme.space.half)
                .accessibilityHidden(true)
        }
    }
}

/// The container of a field: `surface.raised` with a `stroke.control` outline, or a thicker accent outline
/// while focused. The outline is drawn inside the shape, so nothing moves when it changes.
struct LCFieldSurface: View {
    let isFocused: Bool

    @Environment(\.theme) private var theme

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: theme.radius.field, style: .continuous)

        ZStack {
            shape.fill(theme.surface.raised)
            shape.strokeBorder(
                isFocused ? theme.accent.fill : theme.stroke.control,
                lineWidth: isFocused ? theme.component.focusStroke : theme.component.stroke
            )
        }
        .animation(theme.motion.fast, value: isFocused)
    }
}
