import SwiftUI

/// Design tokens, named by role as in `docs/design/01-design-system.md` section 2.
///
/// Views take them from the environment (`@Environment(\.theme)`) and never spell a colour, a size, a
/// radius or a font themselves. The values of Daylight, the only theme, are in `Theme+Daylight.swift`.
struct Theme: Sendable {
    var ground: Ground
    var surface: Surface
    var stroke: Stroke
    var sheen: Sheen
    var ink: Ink
    var accent: Accent
    var status: Status
    var track: Track
    var radius: Radius
    var space: Space
    var size: Size
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
        var panel: Color, inset: Color, raised: Color, raisedPressed: Color
        /// `floating` is laid over a system material to make fallback floating chrome; `floatingSolid`
        /// replaces both when Reduce Transparency is on.
        var floating: Color, floatingSolid: Color, overlay: Color, inverse: Color, scrim: Color, qr: Color
    }

    struct Stroke: Sendable {
        /// `stroke.edge` runs from `edgeTop` at the top of a shape to `edgeBottom` at the bottom.
        var edgeTop: Color, edgeBottom: Color, raised: Color, inset: Color, onAccent: Color
        var hairline: Color, control: Color, danger: Color, focus: Color
    }

    struct Sheen: Sendable {
        var top: Color
        /// Where the sheen has faded to nothing, as a fraction of the height.
        var fadeEnd: CGFloat
    }

    struct Ink: Sendable {
        var primary: Color, secondary: Color, disabled: Color, onAccent: Color, onInverse: Color
    }

    struct Accent: Sendable {
        var fill: Color, pressed: Color, strong: Color, tint: Color
    }

    struct Status: Sendable {
        var live: Color, success: Color, warning: Color, danger: Color, offline: Color
        var liveTint: Color, successTint: Color, warningTint: Color, dangerTint: Color, neutralTint: Color

        func color(for tone: StatusTone) -> Color {
            switch tone {
            case .live: return live
            case .success: return success
            case .warning: return warning
            case .danger: return danger
            case .neutral: return offline
            }
        }

        func tint(for tone: StatusTone) -> Color {
            switch tone {
            case .live: return liveTint
            case .success: return successTint
            case .warning: return warningTint
            case .danger: return dangerTint
            case .neutral: return neutralTint
            }
        }
    }

    struct Track: Sendable {
        var progress: Color
    }

    /// 30, 26, 32, 22, 16, 12, 8
    struct Radius: Sendable {
        var panel: CGFloat, compactPanel: CGFloat, sheet: CGFloat, card: CGFloat, field: CGFloat, tile: CGFloat, small: CGFloat
    }

    /// 2, 4, 8, 12, 16, 20, 24, 32; 16, 16, 16, 12, 12, 8, 12
    struct Space: Sendable {
        var half: CGFloat, s1: CGFloat, s2: CGFloat, s3: CGFloat, s4: CGFloat, s5: CGFloat, s6: CGFloat, s8: CGFloat
        var screenInset: CGFloat, panelInset: CGFloat, cardInset: CGFloat, panelGap: CGFloat, groupGap: CGFloat
        var inlineGap: CGFloat, controlGap: CGFloat
    }

    /// 44, 52, 44, 64, 52, 52, 64, 48, 28, 36; 740
    struct Size: Sendable {
        var minimumTarget: CGFloat, largeButton: CGFloat, regularButton: CGFloat, sessionControl: CGFloat
        var clusterCircle: CGFloat, row: CGFloat, twoLineRow: CGFloat, field: CGFloat, chip: CGFloat, iconTile: CGFloat
        var compactHeightThreshold: CGFloat
    }

    struct Shadow: Sendable {
        var color: Color
        var radius: CGFloat
        var y: CGFloat
    }

    struct Shadows: Sendable {
        var panel: Shadow, raised: Shadow, floating: Shadow, accent: Shadow
    }

    struct Motion: Sendable {
        var press: Animation, fast: Animation, base: Animation, emphasis: Animation, stream: Animation
        var pulseSeconds: Double, pressedScale: CGFloat
    }

    struct Opacity: Sendable {
        var disabled: Double, caretLow: Double
    }
}

extension EnvironmentValues {
    /// The active theme. Daylight unless a parent view says otherwise.
    @Entry var theme: Theme = .daylight
}

// MARK: - Tokens beside the structure above

// The structure above is the one the screens were written against, so it is left as it is. What the
// components need beyond it hangs off the theme here. Daylight is the only theme and these values do not
// change with an accessibility setting, so each is one constant; a second theme would make them stored
// members of `Theme`.
extension Theme {
    /// Component tokens, the third layer of design system 2.1: a value that belongs to one component and
    /// is not on a shared scale. Lengths are in points.
    struct Component: Sendable {
        // Strokes
        /// Every outline and hairline.
        var stroke: CGFloat
        /// The outline of a focused field.
        var focusStroke: CGFloat
        /// The hollow ring of a neutral chip.
        var ringStroke: CGFloat

        // Buttons and session controls
        var quietButtonInset: CGFloat
        var sessionControlInset: CGFloat
        var sessionControlGap: CGFloat
        var sessionControlMinWidth: CGFloat
        var sessionControlMaxWidth: CGFloat
        /// How far the label of a session control may shrink to stay inside its fixed height.
        var sessionControlLabelShrink: CGFloat

        // Chips
        var chipInset: CGFloat
        var chipGap: CGFloat
        var chipDot: CGFloat

        // Rows and panels
        var rowInset: CGFloat
        var detailRow: CGFloat
        var compactPanelInset: CGFloat

        // Fields
        var fieldLabelGap: CGFloat
        var multilineField: CGFloat

        // Spinner and progress
        var spinnerStroke: CGFloat
        var smallSpinnerStroke: CGFloat
        /// Sizes at or below this take the thinner stroke.
        var smallSpinner: CGFloat
        /// The part of the circle that is drawn, 0 to 1.
        var spinnerArc: CGFloat
        var buttonSpinner: CGFloat
        var spinSeconds: Double
        var progressBar: CGFloat

        // Empty state and toast
        var emptyStateTile: CGFloat
        var emptyStateWidth: CGFloat
        var emptyStateInset: CGFloat
        var toast: CGFloat
        var toastTravel: CGFloat

        // Level meter, size S
        var meterBar: CGFloat
        var meterGap: CGFloat
        var meter: CGFloat
        /// One weight per bar, left to right.
        var meterWeights: [CGFloat]
        var meterAttack: Animation
        var meterRelease: Animation
        var meterInterval: Double
        /// Under Reduce Motion the meter moves four times a second.
        var meterCalmInterval: Double

        // Transcript
        var caret: CGFloat
        var caretGap: CGFloat
        var transcriptLineSpacing: CGFloat
        /// One half of the live pulse: from full opacity to `opacity.caretLow`, or back.
        var pulse: Animation

        // Chrome and brand
        /// How far content fades out above floating controls on the fallback path.
        var scrollFade: CGFloat
        var brandMark: CGFloat
        var compactBrandMark: CGFloat
        /// From this height up the in-app mark is drawn with its gradient, gloss and rim.
        var detailedBrandMark: CGFloat
    }

    /// State overlays (design system 2.3), drawn on top of whatever is underneath.
    struct StateOverlay: Sendable {
        /// Pressed rows and quiet buttons on a panel or a card. It only ever lightens.
        var pressed: Color
    }

    /// The colours of the in-app mark (design system 8.2).
    struct Brand: Sendable {
        var bubbleTop: Color, bubbleMiddle: Color, bubbleBottom: Color
        /// Where the middle colour sits, as a fraction of the bubble's height.
        var bubbleMiddleStop: CGFloat
        var bars: Color
        var glossTop: Color
        var rimTop: Color, rimBottom: Color
    }

    var component: Component { .daylight }
    var state: StateOverlay { .daylight }
    var brand: Brand { .daylight }
}

extension Color {
    /// An sRGB colour from a 24-bit value such as `0x0B57D0`. Only the theme definition uses it.
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
