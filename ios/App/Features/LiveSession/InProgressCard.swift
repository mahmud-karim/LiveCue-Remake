import SwiftUI

/// The card of the answer that is being made, or of the streamed text a failure left behind.
///
/// While it is the newest thing in the feed it is the prominent cue card: a spinner with the stage label,
/// and the streamed text with the live caret. Under an error card it is a plain card in a compact panel,
/// labelled "Incomplete answer", with its text in the secondary ink. The streamed text is drawn as it is:
/// nothing in it is read as formatting or as a link.
struct InProgressCard: View {
    private let state: InProgressState

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(state: InProgressState) {
        self.state = state
    }

    var body: some View {
        if state.isIncomplete {
            Panel(compact: true) {
                Card {
                    header
                    text
                }
            }
        } else {
            Card(prominent: true) {
                header
                text
            }
        }
    }

    private var header: some View {
        CueCardHeader {
            HStack(alignment: .center, spacing: theme.component.chipGap) {
                if state.showsSpinner {
                    Spinner(size: dynamicTypeSize.lcScaled(theme.component.smallSpinner))
                        .foregroundStyle(theme.ink.secondary)
                }
                CaptionText(state.label, tone: state.isIncomplete ? .danger : nil)
            }
        }
    }

    @ViewBuilder
    private var text: some View {
        if !state.text.isEmpty {
            if state.showsSpinner {
                // Still arriving: the caret follows the last character.
                streamedText
                    .modifier(StreamingCaret())
                    .accessibilityAddTraits(.updatesFrequently)
                    .accessibilityIdentifier("streaming-answer")
            } else {
                streamedText
                    .textSelection(.enabled)
                    .accessibilityIdentifier("streaming-answer")
            }
        }
    }

    private var streamedText: some View {
        Text(verbatim: state.text)
            .lcText(.cue, color: state.isIncomplete ? theme.ink.secondary : theme.ink.primary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// The live caret after the last character of a streaming text (design system 4.18). It pulses, and
/// stands still with Reduce Motion.
private struct StreamingCaret: ViewModifier {
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        if reduceMotion {
            content.textRenderer(caret(opacity: 1))
        } else {
            content.phaseAnimator([false, true]) { view, isLow in
                view.textRenderer(caret(opacity: isLow ? theme.opacity.caretLow : 1))
            } animation: { _ in
                theme.component.pulse
            }
        }
    }

    private func caret(opacity: Double) -> LCCaretRenderer {
        LCCaretRenderer(
            color: theme.accent.fill,
            width: theme.component.caret,
            height: dynamicTypeSize.lcBodyCapHeight,
            gap: theme.component.caretGap,
            opacity: opacity
        )
    }
}
