import SwiftUI

/// A labelled secure field with a reveal button (design system 4.7); never autocapitalised or
/// autocorrected. `identifier` goes on the field itself.
struct LCSecureField: View {
    private let label: String
    private let text: Binding<String>
    private let placeholder: String
    private let identifier: String?

    init(_ label: String, text: Binding<String>, placeholder: String, identifier: String? = nil) {
        self.label = label
        self.text = text
        self.placeholder = placeholder
        self.identifier = identifier
    }

    var body: some View {
        // STUB(WP-05)
        SecureField(label, text: text, prompt: Text(placeholder))
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .accessibilityIdentifier(identifier ?? "")
    }
}
