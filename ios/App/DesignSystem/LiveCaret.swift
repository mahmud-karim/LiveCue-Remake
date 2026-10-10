import SwiftUI

/// The pulsing caret after interim text (design system 4.18). Hidden from accessibility.
struct LiveCaret: View {
    private let paused: Bool

    @Environment(\.theme) private var theme

    init(paused: Bool = false) {
        self.paused = paused
    }

    var body: some View {
        // STUB(WP-05)
        Rectangle()
            .fill(theme.accent.fill)
            .frame(width: theme.space.half, height: theme.space.s4)
            .opacity(paused ? theme.opacity.caretLow : 1)
            .accessibilityHidden(true)
    }
}
