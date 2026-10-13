import SwiftUI

/// What a status chip shows: a text, a tone and a mark.
struct StatusChipModel: Equatable, Sendable {
    let text: String
    let tone: StatusTone
    let mark: ChipMark

    init(_ text: String, tone: StatusTone, mark: ChipMark = .dot) {
        self.text = text
        self.tone = tone
        self.mark = mark
    }
}

/// A status chip (design system 4.12): one accessibility element whose label is the text.
///
/// A compact, non-interactive state label. It brings its own base (`surface.raised` under the tone's
/// tint), so its coloured text passes on any surface, and it is never glass. Pass `raised: true` when it
/// sits directly on the ground. The words carry the meaning; the mark and the colour only support them.
struct StatusChip: View {
    private let model: StatusChipModel
    private let raised: Bool

    @Environment(\.theme) private var theme
    @Environment(\.accessibilityDifferentiateWithoutColor) private var differentiateWithoutColor

    init(_ model: StatusChipModel, raised: Bool = false) {
        self.model = model
        self.raised = raised
    }

    init(_ text: String, tone: StatusTone, mark: ChipMark = .dot, raised: Bool = false) {
        self.init(StatusChipModel(text, tone: tone, mark: mark), raised: raised)
    }

    var body: some View {
        HStack(spacing: theme.component.chipGap) {
            LCChipMark(mark: mark)
            Text(verbatim: model.text)
                .font(TextRole.caption.font.weight(.semibold))
        }
        .foregroundStyle(theme.status.color(for: model.tone))
        .padding(.horizontal, theme.component.chipInset)
        .padding(.vertical, theme.space.s1)
        .frame(minHeight: theme.size.chip)
        .background {
            LCTintedSurface(shape: shape, tint: theme.status.tint(for: model.tone), castsShadow: raised)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(model.text)
        // A change of variant cross-fades.
        .animation(theme.motion.base, value: model)
    }

    private var shape: LCPill {
        LCPill(radius: theme.size.chip / 2)
    }

    /// With Differentiate Without Colour, a dot gives way to the glyph of its tone (design system 6.8).
    private var mark: ChipMark {
        guard differentiateWithoutColor, model.mark == .dot || model.mark == .pulsingDot else {
            return model.mark
        }
        switch model.tone {
        case .live: return model.mark
        case .success: return .check
        case .warning: return .pause
        case .danger: return .alert
        case .neutral: return .ring
        }
    }
}

/// The leading mark of a chip, in the chip's colour. Each state has its own shape as well as its colour.
private struct LCChipMark: View {
    let mark: ChipMark

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        switch mark {
        case .dot:
            dot
        case .pulsingDot:
            dot.lcPulse()
        case .ring:
            Circle()
                .strokeBorder(.foreground, lineWidth: theme.component.ringStroke)
                .frame(width: side, height: side)
        case .check:
            LCGlyph("checkmark", size: .chip, weight: .bold)
        case .pause:
            LCGlyph("pause.fill", size: .chip)
        case .alert:
            LCGlyph("exclamationmark.triangle.fill", size: .chip)
        case .spinner:
            Spinner(size: dynamicTypeSize.lcScaled(theme.component.smallSpinner))
        case .none:
            EmptyView()
        }
    }

    private var dot: some View {
        Circle()
            .fill(.foreground)
            .frame(width: side, height: side)
    }

    private var side: CGFloat {
        dynamicTypeSize.lcScaled(theme.component.chipDot)
    }
}
