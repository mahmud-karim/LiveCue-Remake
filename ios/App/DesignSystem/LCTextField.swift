import SwiftUI

/// A labelled text field (design system 4.6). `identifier` goes on the text field itself.
struct LCTextField: View {
    private let label: String
    private let text: Binding<String>
    private let placeholder: String
    private let lines: ClosedRange<Int>?
    private let monospaced: Bool
    private let isURL: Bool
    private let identifier: String?

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
        // STUB(WP-05)
        TextField(label, text: text, prompt: Text(placeholder))
            .accessibilityIdentifier(identifier ?? "")
    }
}
