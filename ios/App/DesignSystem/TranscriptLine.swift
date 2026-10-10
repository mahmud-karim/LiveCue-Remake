import SwiftUI

/// One line of transcript (design system 4.18): final text, or interim text with an optional caret.
///
/// Final text is `ink.primary` and selectable. Interim text is `ink.secondary` in the same font, size,
/// weight and line spacing, so nothing reflows when it becomes final. The live caret follows the last
/// interim character and pulses; with Reduce Motion it stands still. A transcript line lives on a panel,
/// never on glass. The text is shown as it is: nothing in it is read as formatting or as a link.
struct TranscriptLine: View {
    private let text: String
    private let isInterim: Bool
    private let showsCaret: Bool

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(_ text: String, isInterim: Bool = false, showsCaret: Bool = false) {
        self.text = text
        self.isInterim = isInterim
        self.showsCaret = showsCaret
    }

    var body: some View {
        if showsCaret {
            line
                .modifier(LCCaretPulse(caret: caret, isPulsing: !reduceMotion))
                .accessibilityAddTraits(.updatesFrequently)
        } else if isInterim {
            line
                .accessibilityAddTraits(.updatesFrequently)
        } else {
            line
                .textSelection(.enabled)
        }
    }

    private var line: some View {
        Text(verbatim: text)
            .lcText(.transcript, color: isInterim ? theme.ink.secondary : theme.ink.primary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
    }

    private var caret: LCCaretRenderer {
        LCCaretRenderer(
            color: theme.accent.fill,
            width: theme.component.caret,
            height: dynamicTypeSize.lcBodyCapHeight,
            gap: theme.component.caretGap
        )
    }
}

/// Draws a text and then the live caret after its last character: a bar as tall as a capital letter,
/// standing on the baseline of the last line.
struct LCCaretRenderer: TextRenderer {
    var color: Color
    var width: CGFloat
    var height: CGFloat
    var gap: CGFloat
    var opacity: Double = 1

    var animatableData: Double {
        get { opacity }
        set { opacity = newValue }
    }

    /// Room for the caret when the last line runs to the trailing edge.
    var displayPadding: EdgeInsets {
        EdgeInsets(top: 0, leading: 0, bottom: 0, trailing: width + gap)
    }

    func draw(layout: Text.Layout, in context: inout GraphicsContext) {
        for line in layout {
            context.draw(line)
        }
        guard let last = layout.last else {
            return
        }
        let bounds = last.typographicBounds
        let bar = CGRect(
            x: bounds.rect.maxX + gap,
            y: bounds.origin.y - height,
            width: width,
            height: height
        )
        context.fill(
            Path(roundedRect: bar, cornerRadius: width / 2),
            with: .color(color.opacity(opacity))
        )
    }
}

/// Runs the live pulse on the caret of a text: its opacity goes from 1 to `opacity.caretLow` and back.
private struct LCCaretPulse: ViewModifier {
    let caret: LCCaretRenderer
    let isPulsing: Bool

    @Environment(\.theme) private var theme

    func body(content: Content) -> some View {
        if isPulsing {
            content.phaseAnimator([false, true]) { view, isLow in
                view.textRenderer(renderer(opacity: isLow ? theme.opacity.caretLow : 1))
            } animation: { _ in
                theme.component.pulse
            }
        } else {
            content.textRenderer(caret)
        }
    }

    private func renderer(opacity: Double) -> LCCaretRenderer {
        var copy = caret
        copy.opacity = opacity
        return copy
    }
}
