import SwiftUI

extension Theme {
    /// Daylight, the only theme: the values of `docs/design/01-design-system.md` section 2.
    static let daylight: Theme = {
        // STUB(WP-05)
        // Copied from the design system once, for the shell. The owner checks every value against it.
        let white: UInt32 = 0xFFFFFF
        let ink: UInt32 = 0x0E1B33
        let accent: UInt32 = 0x0B57D0
        let shade = Color(hex: 0x283C6E)

        return Theme(
            ground: Ground(
                base: Color(hex: 0xE6ECF5),
                blooms: [
                    Bloom(color: Color(hex: 0xC7D3F5), center: UnitPoint(x: 0.92, y: 1.00), radius: 0.50),
                    Bloom(color: Color(hex: 0xA8D4F2), center: UnitPoint(x: 0.02, y: 0.64), radius: 0.48),
                    Bloom(color: Color(hex: 0xB9C4F7), center: UnitPoint(x: 0.96, y: 0.24), radius: 0.44),
                    Bloom(color: Color(hex: 0x9DC1F5), center: UnitPoint(x: 0.06, y: -0.06), radius: 0.46)
                ],
                bloomFadeEnd: 0.72
            ),
            surface: Surface(
                panel: Color(hex: white, alpha: 0.62),
                inset: Color(hex: white, alpha: 0.46),
                raised: Color(hex: white, alpha: 0.85),
                raisedPressed: Color(hex: 0xEEF3FB),
                floating: Color(hex: white, alpha: 0.62),
                floatingSolid: Color(hex: 0xF6F8FB),
                overlay: Color(hex: 0xF9FBFF, alpha: 0.97),
                inverse: Color(hex: ink, alpha: 0.90),
                scrim: Color(hex: ink, alpha: 0.30),
                qr: Color(hex: white)
            ),
            stroke: Stroke(
                edgeTop: Color(hex: white, alpha: 0.95),
                edgeBottom: Color(hex: white, alpha: 0.60),
                raised: Color(hex: white, alpha: 0.90),
                inset: Color(hex: white, alpha: 0.70),
                onAccent: Color(hex: white, alpha: 0.30),
                hairline: Color(hex: ink, alpha: 0.12),
                control: Color(hex: ink, alpha: 0.60),
                danger: Color(hex: 0xB3243F, alpha: 0.35),
                focus: Color(hex: accent)
            ),
            sheen: Sheen(top: Color(hex: white, alpha: 0.28), fadeEnd: 0.40),
            ink: Ink(
                primary: Color(hex: ink),
                secondary: Color(hex: 0x3D4A63),
                disabled: Color(hex: ink, alpha: 0.38),
                onAccent: Color(hex: white),
                onInverse: Color(hex: white)
            ),
            accent: Accent(
                fill: Color(hex: accent),
                pressed: Color(hex: 0x0949B0),
                strong: Color(hex: 0x0842A0),
                tint: Color(hex: accent, alpha: 0.12)
            ),
            status: Status(
                live: Color(hex: accent),
                success: Color(hex: 0x0B6443),
                warning: Color(hex: 0x7D5108),
                danger: Color(hex: 0xB3243F),
                offline: Color(hex: 0x3D4A63),
                liveTint: Color(hex: accent, alpha: 0.12),
                successTint: Color(hex: 0x0B6443, alpha: 0.12),
                warningTint: Color(hex: 0x7D5108, alpha: 0.12),
                dangerTint: Color(hex: 0xB3243F, alpha: 0.12),
                neutralTint: Color(hex: ink, alpha: 0.08)
            ),
            track: Track(progress: Color(hex: ink, alpha: 0.14)),
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
            shadow: Shadows(
                panel: Shadow(color: shade.opacity(0.23), radius: 16, y: 12),
                raised: Shadow(color: shade.opacity(0.19), radius: 6, y: 4),
                floating: Shadow(color: shade.opacity(0.22), radius: 17, y: 14),
                accent: Shadow(color: shade.opacity(0.28), radius: 8, y: 6)
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
