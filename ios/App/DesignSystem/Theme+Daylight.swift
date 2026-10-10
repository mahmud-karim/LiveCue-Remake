import SwiftUI

/// Daylight's raw values: the palette layer of design system 2.1. Only the theme definition
/// (`Theme+Daylight.swift` and `Theme+Resolved.swift`) reads them.
enum DaylightPalette {
    static let white: UInt32 = 0xFFFFFF
    static let ink: UInt32 = 0x0E1B33
    static let inkSecondary: UInt32 = 0x3D4A63
    static let shade: UInt32 = 0x283C6E

    static let accent: UInt32 = 0x0B57D0
    static let accentPressed: UInt32 = 0x0949B0
    static let accentStrong: UInt32 = 0x0842A0

    static let success: UInt32 = 0x0B6443
    static let successStrong: UInt32 = 0x095438
    static let warning: UInt32 = 0x7D5108
    static let warningStrong: UInt32 = 0x5F3D04
    static let danger: UInt32 = 0xB3243F
    static let dangerStrong: UInt32 = 0x8F172E

    static let groundBase: UInt32 = 0xE6ECF5
    static let bloom1: UInt32 = 0x9DC1F5
    static let bloom2: UInt32 = 0xB9C4F7
    static let bloom3: UInt32 = 0xA8D4F2
    static let bloom4: UInt32 = 0xC7D3F5

    static let raisedPressed: UInt32 = 0xEEF3FB
    static let overlay: UInt32 = 0xF9FBFF

    // Solid twins, used when transparency is reduced (design system 2.3 and 6.3).
    static let panelSolid: UInt32 = 0xF6F8FB
    static let insetSolid: UInt32 = 0xFAFBFD
    static let overlaySolid: UInt32 = 0xF8FBFF

    /// Every tint of the theme (design system 2.19).
    static let tintAlpha = 0.12
    static let neutralTintAlpha = 0.08
    /// `stroke.hairline`, and what it becomes with Increase Contrast (6.4).
    static let hairlineAlpha = 0.12
    static let strongHairlineAlpha = 0.30

    // Shadows (design system 2.13): the alpha that should be visible outside the surface, and the alpha
    // the effect is given so that it comes out that way through a translucent fill.
    static let panelShadowAlpha = 0.14
    static let panelShadowEffectAlpha = 0.23
    static let raisedShadowAlpha = 0.16
    static let raisedShadowEffectAlpha = 0.19
    static let floatingShadowAlpha = 0.22
    static let accentShadowAlpha = 0.28

    // The in-app mark (design system 8.2).
    static let bubbleTop: UInt32 = 0x3D82F0
    static let bubbleBottom: UInt32 = 0x0A47AE
}

extension Theme {
    /// Daylight, the only theme: the values of `docs/design/01-design-system.md` section 2.
    static let daylight: Theme = {
        let palette = DaylightPalette.self
        let shade = palette.shade

        return Theme(
            ground: Ground(
                base: Color(hex: palette.groundBase),
                // Paint order, bottom to top: bloom 4, 3, 2, 1.
                blooms: [
                    Bloom(color: Color(hex: palette.bloom4), center: UnitPoint(x: 0.92, y: 1.00), radius: 0.50),
                    Bloom(color: Color(hex: palette.bloom3), center: UnitPoint(x: 0.02, y: 0.64), radius: 0.48),
                    Bloom(color: Color(hex: palette.bloom2), center: UnitPoint(x: 0.96, y: 0.24), radius: 0.44),
                    Bloom(color: Color(hex: palette.bloom1), center: UnitPoint(x: 0.06, y: -0.06), radius: 0.46)
                ],
                bloomFadeEnd: 0.72
            ),
            surface: Surface(
                panel: Color(hex: palette.white, alpha: 0.62),
                inset: Color(hex: palette.white, alpha: 0.46),
                raised: Color(hex: palette.white, alpha: 0.85),
                raisedPressed: Color(hex: palette.raisedPressed),
                floating: Color(hex: palette.white, alpha: 0.62),
                floatingSolid: Color(hex: palette.panelSolid),
                overlay: Color(hex: palette.overlay, alpha: 0.97),
                inverse: Color(hex: palette.ink, alpha: 0.90),
                scrim: Color(hex: palette.ink, alpha: 0.30),
                qr: Color(hex: palette.white)
            ),
            stroke: Stroke(
                edgeTop: Color(hex: palette.white, alpha: 0.95),
                edgeBottom: Color(hex: palette.white, alpha: 0.60),
                raised: Color(hex: palette.white, alpha: 0.90),
                inset: Color(hex: palette.white, alpha: 0.70),
                onAccent: Color(hex: palette.white, alpha: 0.30),
                hairline: Color(hex: palette.ink, alpha: palette.hairlineAlpha),
                control: Color(hex: palette.ink, alpha: 0.60),
                danger: Color(hex: palette.danger, alpha: 0.35),
                focus: Color(hex: palette.accent)
            ),
            sheen: Sheen(top: Color(hex: palette.white, alpha: 0.28), fadeEnd: 0.40),
            ink: Ink(
                primary: Color(hex: palette.ink),
                secondary: Color(hex: palette.inkSecondary),
                disabled: Color(hex: palette.ink, alpha: 0.38),
                onAccent: Color(hex: palette.white),
                onInverse: Color(hex: palette.white)
            ),
            accent: Accent(
                fill: Color(hex: palette.accent),
                pressed: Color(hex: palette.accentPressed),
                strong: Color(hex: palette.accentStrong),
                tint: Color(hex: palette.accent, alpha: palette.tintAlpha)
            ),
            status: Status(
                // `status.live` is the accent and `status.offline` is `ink.secondary`; they are separate
                // tokens so that a later theme can split them.
                live: Color(hex: palette.accent),
                success: Color(hex: palette.success),
                warning: Color(hex: palette.warning),
                danger: Color(hex: palette.danger),
                offline: Color(hex: palette.inkSecondary),
                liveTint: Color(hex: palette.accent, alpha: palette.tintAlpha),
                successTint: Color(hex: palette.success, alpha: palette.tintAlpha),
                warningTint: Color(hex: palette.warning, alpha: palette.tintAlpha),
                dangerTint: Color(hex: palette.danger, alpha: palette.tintAlpha),
                neutralTint: Color(hex: palette.ink, alpha: palette.neutralTintAlpha)
            ),
            track: Track(progress: Color(hex: palette.ink, alpha: 0.14)),
            radius: Radius(panel: 30, compactPanel: 26, sheet: 32, card: 22, field: 16, tile: 12, small: 8),
            space: Space(
                half: 2, s1: 4, s2: 8, s3: 12, s4: 16, s5: 20, s6: 24, s8: 32,
                screenInset: 16, panelInset: 16, cardInset: 16, panelGap: 12, groupGap: 12,
                inlineGap: 8, controlGap: 12
            ),
            size: Size(
                minimumTarget: 44, largeButton: 52, regularButton: 44, sessionControl: 64,
                clusterCircle: 52, row: 52, twoLineRow: 64, field: 48, chip: 28, iconTile: 36,
                compactHeightThreshold: 740
            ),
            // SwiftUI's shadow radius is half the canonical blur.
            shadow: Shadows(
                panel: Shadow(color: Color(hex: shade, alpha: palette.panelShadowEffectAlpha), radius: 16, y: 12),
                raised: Shadow(color: Color(hex: shade, alpha: palette.raisedShadowEffectAlpha), radius: 6, y: 4),
                floating: Shadow(color: Color(hex: shade, alpha: palette.floatingShadowAlpha), radius: 17, y: 14),
                accent: Shadow(color: Color(hex: shade, alpha: palette.accentShadowAlpha), radius: 8, y: 6)
            ),
            motion: Motion(
                press: .timingCurve(0, 0, 0.2, 1, duration: 0.1),
                fast: .timingCurve(0.2, 0, 0, 1, duration: 0.16),
                base: .timingCurve(0.2, 0, 0, 1, duration: 0.24),
                emphasis: .smooth(duration: 0.36),
                stream: .linear(duration: 0.12),
                pulseSeconds: 1.6,
                pressedScale: 0.97
            ),
            opacity: Opacity(disabled: 0.45, caretLow: 0.30)
        )
    }()
}

extension Theme.Component {
    /// The component values of `docs/design/01-design-system.md` section 4.
    static let daylight = Theme.Component(
        stroke: 1,
        focusStroke: 2,
        ringStroke: 1.5,
        quietButtonInset: 6,
        sessionControlInset: 28,
        sessionControlGap: 10,
        sessionControlMinWidth: 200,
        sessionControlMaxWidth: 420,
        sessionControlLabelShrink: 0.7,
        chipInset: 10,
        chipGap: 6,
        chipDot: 8,
        rowInset: 7,
        detailRow: 26,
        compactPanelInset: 14,
        fieldLabelGap: 6,
        multilineField: 96,
        spinnerStroke: 2.5,
        smallSpinnerStroke: 2,
        smallSpinner: 12,
        spinnerArc: 0.75,
        buttonSpinner: 16,
        spinSeconds: 0.9,
        progressBar: 6,
        emptyStateTile: 64,
        emptyStateWidth: 340,
        emptyStateInset: 28,
        toast: 48,
        toastTravel: 12,
        meterBar: 3,
        meterGap: 2,
        meter: 22,
        meterWeights: [0.45, 0.75, 1.00, 0.70, 0.40],
        meterAttack: .linear(duration: 0.06),
        meterRelease: .linear(duration: 0.24),
        meterInterval: 1.0 / 24.0,
        meterCalmInterval: 0.25,
        caret: 2,
        caretGap: 3,
        transcriptLineSpacing: 3,
        // `ease.inOut` over half of `motion.pulse`.
        pulse: .timingCurve(0.4, 0, 0.6, 1, duration: 0.8),
        scrollFade: 32,
        brandMark: 68,
        compactBrandMark: 48,
        detailedBrandMark: 64
    )
}

extension Theme.StateOverlay {
    static let daylight = Theme.StateOverlay(
        pressed: Color(hex: DaylightPalette.white, alpha: 0.25)
    )
}

extension Theme.Brand {
    static let daylight = Theme.Brand(
        bubbleTop: Color(hex: DaylightPalette.bubbleTop),
        bubbleMiddle: Color(hex: DaylightPalette.accent),
        bubbleBottom: Color(hex: DaylightPalette.bubbleBottom),
        bubbleMiddleStop: 0.6,
        bars: Color(hex: DaylightPalette.white),
        glossTop: Color(hex: DaylightPalette.white, alpha: 0.35),
        rimTop: Color(hex: DaylightPalette.white, alpha: 0.55),
        rimBottom: Color(hex: DaylightPalette.white, alpha: 0.10)
    )
}
