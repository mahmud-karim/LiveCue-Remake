import SwiftUI

// The content-layer surfaces of design system 2.3 and 3.4: fills the app draws itself, the same on iOS 26
// and before. None of them is glass. Each is drawn behind its content with `.background { }`, and the
// shadow is cast by the fill shape alone (2.13), so that nothing inside the surface casts one too.

extension Theme {
    /// `stroke.edge`: the top highlight of panels and of floating chrome on the fallback path.
    var edgeStroke: LinearGradient {
        LinearGradient(colors: [stroke.edgeTop, stroke.edgeBottom], startPoint: .top, endPoint: .bottom)
    }

    /// `glass.sheen`: the inner glow of panels and of floating chrome on the fallback path.
    var sheenFill: LinearGradient {
        LinearGradient(
            stops: [
                Gradient.Stop(color: sheen.top, location: 0),
                Gradient.Stop(color: sheen.top.opacity(0), location: sheen.fadeEnd)
            ],
            startPoint: .top,
            endPoint: .bottom
        )
    }
}

/// `surface.panel` with its sheen, edge and shadow (design system 4.9).
struct LCPanelSurface<S: InsettableShape>: View {
    let shape: S

    @Environment(\.theme) private var theme

    var body: some View {
        let shadow = theme.shadow.panel

        ZStack {
            shape
                .fill(theme.surface.panel)
                .shadow(color: shadow.color, radius: shadow.radius, y: shadow.y)
            shape.fill(theme.sheenFill)
            shape.strokeBorder(theme.edgeStroke, lineWidth: theme.component.stroke)
        }
    }
}

/// `surface.inset` with its stroke: a card inside a panel. It casts no shadow.
struct LCInsetSurface<S: InsettableShape>: View {
    let shape: S

    @Environment(\.theme) private var theme

    var body: some View {
        ZStack {
            shape.fill(theme.surface.inset)
            shape.strokeBorder(theme.stroke.inset, lineWidth: theme.component.stroke)
        }
    }
}

/// `surface.raised` with its stroke and shadow: secondary buttons and the prominent card.
struct LCRaisedSurface<S: InsettableShape>: View {
    let shape: S
    var isPressed = false
    var castsShadow = true
    /// The outline, when it is not `stroke.raised`.
    var outline: Color?

    @Environment(\.theme) private var theme

    var body: some View {
        let shadow = theme.shadow.raised

        ZStack {
            shape
                .fill(isPressed ? theme.surface.raisedPressed : theme.surface.raised)
                .shadow(color: shadow.color.opacity(castsShadow ? 1 : 0), radius: shadow.radius, y: shadow.y)
            shape.strokeBorder(outline ?? theme.stroke.raised, lineWidth: theme.component.stroke)
        }
    }
}

/// The base of a chip, a banner and the tile of an empty state: `surface.raised`, then a tint over it
/// (design system 2.9, table C). Text in the tint's colour is only ever drawn on this pair.
struct LCTintedSurface<S: InsettableShape>: View {
    let shape: S
    let tint: Color
    var castsShadow = false

    @Environment(\.theme) private var theme

    var body: some View {
        let shadow = theme.shadow.raised

        ZStack {
            shape
                .fill(theme.surface.raised)
                .shadow(color: shadow.color.opacity(castsShadow ? 1 : 0), radius: shadow.radius, y: shadow.y)
            shape.fill(tint)
        }
    }
}

/// `stroke.hairline`: a divider. Decorative.
struct LCHairline: View {
    @Environment(\.theme) private var theme

    var body: some View {
        Rectangle()
            .fill(theme.stroke.hairline)
            .frame(height: theme.component.stroke)
            .accessibilityHidden(true)
    }
}

/// The square tile that holds a row's icon (design system 4.8): the accent tint with an accent glyph, or
/// the neutral tint with a secondary glyph. It grows with the text size. Decorative.
struct LCIconTile: View {
    let systemImage: String
    var isNeutral = false

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        LCGlyph(systemImage, size: .regular, weight: .regular)
            .dynamicTypeSize(...DynamicTypeSize.lcTileLimit)
            .foregroundStyle(isNeutral ? theme.ink.secondary : theme.accent.fill)
            .frame(width: side, height: side)
            .background {
                RoundedRectangle(cornerRadius: dynamicTypeSize.lcScaledTile(theme.radius.tile), style: .continuous)
                    .fill(isNeutral ? theme.status.neutralTint : theme.accent.tint)
            }
    }

    private var side: CGFloat {
        dynamicTypeSize.lcScaledTile(theme.size.iconTile)
    }
}
