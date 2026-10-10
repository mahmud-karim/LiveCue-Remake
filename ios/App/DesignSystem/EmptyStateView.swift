import SwiftUI

/// What a screen shows when it has nothing to list (design system 4.14).
///
/// Centred: an icon tile, a title and a body. It sits inside the panel it explains, or directly on the
/// ground when the whole screen is empty; title and body are ink, so both are allowed there. No
/// illustration and no animation.
struct EmptyStateView: View {
    private let systemImage: String
    private let title: String
    private let message: String

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(systemImage: String, title: String, message: String) {
        self.systemImage = systemImage
        self.title = title
        self.message = message
    }

    var body: some View {
        VStack(spacing: 0) {
            LCGlyph(systemImage, size: .tile, weight: .regular)
                .dynamicTypeSize(...DynamicTypeSize.lcTileLimit)
                .foregroundStyle(theme.accent.fill)
                .frame(width: tileSide, height: tileSide)
                .background {
                    LCTintedSurface(
                        shape: RoundedRectangle(cornerRadius: theme.radius.card, style: .continuous),
                        tint: theme.accent.tint
                    )
                }
            Text(verbatim: title)
                .lcText(.title)
                .padding(.top, theme.space.s4)
            Text(verbatim: message)
                .lcText(.body, color: theme.ink.secondary)
                .padding(.top, theme.component.fieldLabelGap)
        }
        .multilineTextAlignment(.center)
        .fixedSize(horizontal: false, vertical: true)
        .frame(maxWidth: theme.component.emptyStateWidth)
        .padding(theme.component.emptyStateInset)
        .frame(maxWidth: .infinity)
        .accessibilityElement(children: .combine)
    }

    private var tileSide: CGFloat {
        dynamicTypeSize.lcScaledTile(theme.component.emptyStateTile)
    }
}
