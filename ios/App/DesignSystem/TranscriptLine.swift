import SwiftUI

/// One line of transcript (design system 4.18): final text, or interim text with an optional caret.
struct TranscriptLine: View {
    private let text: String
    private let isInterim: Bool
    private let showsCaret: Bool

    @Environment(\.theme) private var theme

    init(_ text: String, isInterim: Bool = false, showsCaret: Bool = false) {
        self.text = text
        self.isInterim = isInterim
        self.showsCaret = showsCaret
    }

    var body: some View {
        // STUB(WP-05)
        Text(text)
            .lcText(.transcript, color: isInterim ? theme.ink.secondary : theme.ink.primary)
    }
}
