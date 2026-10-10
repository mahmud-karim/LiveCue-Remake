import SwiftUI

extension Theme {
    /// The theme as the accessibility settings ask for it: solid twins and no sheen with Reduce
    /// Transparency, the strong colour set and visible outlines with Increase Contrast (design system
    /// 6.3 and 6.4).
    ///
    /// Increase Contrast includes everything Reduce Transparency does. Native Liquid Glass is left to the
    /// system on both counts; this changes what the app fills itself.
    func resolved(reduceTransparency: Bool, increaseContrast: Bool) -> Theme {
        var theme = self
        if reduceTransparency || increaseContrast {
            theme.useSolidTwins()
        }
        if increaseContrast {
            theme.useStrongSet()
        }
        return theme
    }

    /// Design system 6.3: every translucent fill becomes its opaque twin and the sheen goes. Ground,
    /// blooms and strokes stay.
    private mutating func useSolidTwins() {
        let palette = DaylightPalette.self

        surface.panel = Color(hex: palette.panelSolid)
        surface.inset = Color(hex: palette.insetSolid)
        surface.raised = Color(hex: palette.white)
        surface.floating = surface.floatingSolid
        surface.overlay = Color(hex: palette.overlaySolid)
        surface.inverse = Color(hex: palette.ink)
        sheen.top = Color(hex: palette.white, alpha: 0)

        // A shadow's effect alpha is its visible alpha divided by the alpha of the fill that casts it
        // (2.13). The fills are opaque now, so the two are the same.
        shadow.panel.color = Color(hex: palette.shade, alpha: palette.panelShadowAlpha)
        shadow.raised.color = Color(hex: palette.shade, alpha: palette.raisedShadowAlpha)
    }

    /// Design system 6.4: a flat ground, the strong colours, and an outline on every surface.
    private mutating func useStrongSet() {
        let palette = DaylightPalette.self

        ground.blooms = []

        accent.fill = accent.strong
        status.live = accent.strong
        status.success = Color(hex: palette.successStrong)
        status.warning = Color(hex: palette.warningStrong)
        status.danger = Color(hex: palette.dangerStrong)
        status.offline = ink.primary

        stroke.edgeTop = stroke.control
        stroke.edgeBottom = stroke.control
        stroke.raised = stroke.control
        stroke.inset = stroke.control
        stroke.hairline = Color(hex: palette.ink, alpha: palette.strongHairlineAlpha)
        stroke.danger = status.danger
        stroke.focus = accent.strong
    }
}
