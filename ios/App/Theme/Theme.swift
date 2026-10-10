import SwiftUI

/// Design tokens, named by role as in `docs/design/01-design-system.md` section 2.
///
/// Only the tokens that the placeholder screen and the glass fallback read are defined so far.
/// Views take them from the environment (`@Environment(\.theme)`) and never spell a colour themselves.
struct Theme: Sendable {
    var ground: Ground
    var surface: Surface
    var stroke: Stroke
    var sheen: Sheen
    var ink: Ink
    var accent: Accent
    var shadow: Shadows
    var motion: Motion
    var opacity: Opacity

    /// One soft circle of colour on the ground.
    struct Bloom: Sendable {
        var color: Color
        /// Centre as a fraction of the width and height. It may lie outside the screen.
        var center: UnitPoint
        /// Radius as a fraction of the longer side of the screen.
        var radius: CGFloat
    }

    struct Ground: Sendable {
        var base: Color
        /// In paint order, bottom to top.
        var blooms: [Bloom]
        /// Where a bloom has faded to nothing, as a fraction of its radius.
        var bloomFadeEnd: CGFloat
    }

    struct Surface: Sendable {
        /// Laid over a system material to make fallback floating chrome.
        var floating: Color
        /// Replaces `floating` and its material when Reduce Transparency is on.
        var floatingSolid: Color
        var raisedPressed: Color
    }

    struct Stroke: Sendable {
        /// `stroke.edge` runs from this colour at the top of a shape...
        var edgeTop: Color
        /// ...to this colour at the bottom.
        var edgeBottom: Color
        var onAccent: Color
    }

    struct Sheen: Sendable {
        var top: Color
        /// Where the sheen has faded to nothing, as a fraction of the height.
        var fadeEnd: CGFloat
    }

    struct Ink: Sendable {
        var primary: Color
        var secondary: Color
        var onAccent: Color
    }

    struct Accent: Sendable {
        var fill: Color
        var pressed: Color
        var strong: Color
    }

    struct Shadow: Sendable {
        var color: Color
        var radius: CGFloat
        var y: CGFloat
    }

    struct Shadows: Sendable {
        var floating: Shadow
        var accent: Shadow
    }

    struct Motion: Sendable {
        /// Press feedback: 100 ms, ease out.
        var press: Animation
        var pressedScale: CGFloat
    }

    struct Opacity: Sendable {
        var disabled: Double
    }
}

extension Theme {
    /// Daylight, the only theme.
    static let daylight: Theme = {
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
                floating: Color(hex: 0xFFFFFF, alpha: 0.62),
                floatingSolid: Color(hex: 0xF6F8FB),
                raisedPressed: Color(hex: 0xEEF3FB)
            ),
            stroke: Stroke(
                edgeTop: Color(hex: 0xFFFFFF, alpha: 0.95),
                edgeBottom: Color(hex: 0xFFFFFF, alpha: 0.60),
                onAccent: Color(hex: 0xFFFFFF, alpha: 0.30)
            ),
            sheen: Sheen(top: Color(hex: 0xFFFFFF, alpha: 0.28), fadeEnd: 0.40),
            ink: Ink(
                primary: Color(hex: 0x0E1B33),
                secondary: Color(hex: 0x3D4A63),
                onAccent: Color(hex: 0xFFFFFF)
            ),
            accent: Accent(
                fill: Color(hex: 0x0B57D0),
                pressed: Color(hex: 0x0949B0),
                strong: Color(hex: 0x0842A0)
            ),
            shadow: Shadows(
                floating: Shadow(color: shade.opacity(0.22), radius: 17, y: 14),
                accent: Shadow(color: shade.opacity(0.28), radius: 8, y: 6)
            ),
            motion: Motion(press: .timingCurve(0, 0, 0.2, 1, duration: 0.1), pressedScale: 0.97),
            opacity: Opacity(disabled: 0.45)
        )
    }()
}

extension EnvironmentValues {
    /// The active theme. Daylight unless a parent view says otherwise.
    @Entry var theme: Theme = .daylight
}

extension Color {
    /// An sRGB colour from a 24-bit value such as `0x0B57D0`.
    init(hex: UInt32, alpha: Double = 1) {
        self.init(
            .sRGB,
            red: Double((hex >> 16) & 0xFF) / 255,
            green: Double((hex >> 8) & 0xFF) / 255,
            blue: Double(hex & 0xFF) / 255,
            opacity: alpha
        )
    }
}
