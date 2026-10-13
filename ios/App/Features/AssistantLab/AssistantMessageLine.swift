import SwiftUI

/// The result of an action as a line on the screen that started it: a glyph and the text, in the success
/// or the danger colour. The text is drawn as it is.
struct AssistantMessageLine: View {
    private let message: InlineMessage

    @Environment(\.theme) private var theme

    init(_ message: InlineMessage) {
        self.message = message
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: theme.space.s2) {
            LCGlyph(message.isError ? "exclamationmark.triangle.fill" : "checkmark", size: .small)
                .foregroundStyle(color)
            CaptionText(message.text, tone: message.isError ? .danger : .success)
        }
        .accessibilityElement(children: .combine)
    }

    private var color: Color {
        message.isError ? theme.status.danger : theme.status.success
    }
}
