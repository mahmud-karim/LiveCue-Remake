import SwiftUI

/// The pulsing caret after interim text (design system 4.18). Hidden from accessibility.
///
/// A bar 2 wide and as tall as a capital letter of body text, in the accent. It stands on the text
/// baseline, so it lines up when placed in a row aligned on the last baseline of the text before it. It
/// pulses while live; `paused` makes it stand still in `status.warning`, and so does Reduce Motion (in the
/// accent). `TranscriptLine` draws its own caret, right after its last character.
struct LiveCaret: View {
    private let paused: Bool

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(paused: Bool = false) {
        self.paused = paused
    }

    var body: some View {
        Capsule()
            .fill(paused ? theme.status.warning : theme.accent.fill)
            .frame(width: theme.component.caret, height: dynamicTypeSize.lcBodyCapHeight)
            .lcPulse(!paused)
            .accessibilityHidden(true)
    }
}
