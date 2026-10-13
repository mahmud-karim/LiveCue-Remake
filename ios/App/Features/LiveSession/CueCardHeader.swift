import SwiftUI

/// The header of a cue card (design system 4.19): a round tile with the assistant's glyph, the kicker
/// "Assistant", and whatever the card puts at its trailing edge.
struct CueCardHeader<Trailing: View>: View {
    private let trailing: Trailing

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(@ViewBuilder trailing: () -> Trailing) {
        self.trailing = trailing()
    }

    var body: some View {
        HStack(alignment: .center, spacing: theme.space.s2) {
            LCGlyph("sparkles", size: .small)
                .dynamicTypeSize(...DynamicTypeSize.lcTileLimit)
                .foregroundStyle(theme.accent.fill)
                .frame(width: side, height: side)
                .background {
                    Circle().fill(theme.accent.tint)
                }
            Text(verbatim: "Assistant")
                .lcText(.micro, color: theme.accent.fill)
            Spacer(minLength: theme.space.s2)
            trailing
        }
        .frame(minHeight: theme.size.chip)
    }

    /// The tile is as tall as a chip, so the header keeps one height with and without one.
    private var side: CGFloat {
        dynamicTypeSize.lcScaledTile(theme.size.chip)
    }
}
