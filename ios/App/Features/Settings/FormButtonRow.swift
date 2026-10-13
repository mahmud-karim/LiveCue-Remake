import SwiftUI
import UIKit

/// Two or more buttons of a form: side by side when they fit, one under the other when they do not, and
/// stacked at full width at the accessibility text sizes (screens 2).
///
/// `content` is given whether its buttons should take the full width.
struct FormButtonRow<Content: View>: View {
    private let content: (Bool) -> Content

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(@ViewBuilder content: @escaping (_ fullWidth: Bool) -> Content) {
        self.content = content
    }

    var body: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.controlGap) {
                content(true)
            }
        } else {
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .center, spacing: theme.space.controlGap) {
                    content(false)
                }
                VStack(alignment: .leading, spacing: theme.space.controlGap) {
                    content(false)
                }
            }
        }
    }
}

/// A fixed web address opened in the browser: a quiet button with the external-link glyph. The address
/// is one of the app's own constants; nothing a service or the PC sent is ever opened.
struct ExternalLinkButton: View {
    private let title: String
    private let address: String

    @Environment(\.theme) private var theme

    init(_ title: String, address: String) {
        self.title = title
        self.address = address
    }

    var body: some View {
        if let destination = URL(string: address) {
            Link(destination: destination) {
                HStack(spacing: theme.space.inlineGap) {
                    Text(verbatim: title)
                    LCGlyph("arrow.up.right.square", size: .regular)
                }
            }
            .buttonStyle(LCButtonStyle(kind: .quiet))
        }
    }
}

enum FormKeyboard {
    /// Closes the keyboard, whichever field has it.
    @MainActor static func dismiss() {
        UIApplication.shared.sendAction(#selector(UIResponder.resignFirstResponder), to: nil, from: nil, for: nil)
    }
}
