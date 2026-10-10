import SwiftUI

/// The look of `LCButton`, for `NavigationLink` and `Link` labels too (design system 4.1 and 4.2, content
/// placement).
///
/// The sizes are minimum heights: larger text makes a button taller, and a label that no longer fits on
/// one line wraps. Pressing changes the fill and scales the button to 0.97. A disabled button is dimmed
/// as a whole and casts no shadow.
struct LCButtonStyle: ButtonStyle {
    private let kind: LCButtonKind
    private let size: LCButtonSize
    private let fullWidth: Bool

    init(kind: LCButtonKind, size: LCButtonSize = .regular, fullWidth: Bool = false) {
        self.kind = kind
        self.size = size
        self.fullWidth = fullWidth
    }

    func makeBody(configuration: Configuration) -> some View {
        LCButtonFace(configuration: configuration, kind: kind, size: size, fullWidth: fullWidth)
    }
}

private struct LCButtonFace: View {
    let configuration: ButtonStyleConfiguration
    let kind: LCButtonKind
    let size: LCButtonSize
    let fullWidth: Bool

    @Environment(\.theme) private var theme
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        configuration.label
            .lcText(size == .large ? .headline : .label, color: ink)
            .multilineTextAlignment(.center)
            .padding(.horizontal, horizontalInset)
            .padding(.vertical, theme.space.s2)
            .frame(maxWidth: fullWidth ? .infinity : nil, minHeight: height)
            .background { face }
            .contentShape(shape)
            .scaleEffect(scale)
            .opacity(isEnabled ? 1 : theme.opacity.disabled)
            .animation(theme.motion.press, value: configuration.isPressed)
    }

    /// A pressed button shrinks a little. A quiet button has no shape to shrink, and with Reduce Motion
    /// the change of fill is the whole pressed state.
    private var scale: CGFloat {
        configuration.isPressed && kind != .quiet && !reduceMotion ? theme.motion.pressedScale : 1
    }

    @ViewBuilder
    private var face: some View {
        switch kind {
        case .primary:
            LCAccentSurface(shape: shape, isPressed: configuration.isPressed, castsShadow: isEnabled)
        case .secondary:
            LCRaisedSurface(shape: shape, isPressed: configuration.isPressed, castsShadow: isEnabled)
        case .destructive:
            // Red text always sits on a fill the app controls.
            LCRaisedSurface(
                shape: shape,
                isPressed: configuration.isPressed,
                castsShadow: isEnabled,
                outline: theme.stroke.danger
            )
        case .quiet:
            // Nothing at rest, so it is safe to repeat in rows.
            shape
                .fill(theme.state.pressed)
                .opacity(configuration.isPressed ? 1 : 0)
        }
    }

    private var ink: Color {
        switch kind {
        case .primary: return theme.ink.onAccent
        case .secondary: return theme.ink.primary
        case .destructive: return theme.status.danger
        case .quiet: return theme.accent.fill
        }
    }

    private var height: CGFloat {
        size == .large ? theme.size.largeButton : theme.size.regularButton
    }

    private var horizontalInset: CGFloat {
        if kind == .quiet {
            return theme.component.quietButtonInset
        }
        return size == .large ? theme.space.s6 : theme.space.s5
    }

    private var shape: LCPill {
        LCPill(radius: height / 2)
    }
}

/// The accent fill of the one primary action: `accent.fill`, `stroke.onAccent` and `shadow.accent`.
struct LCAccentSurface<S: InsettableShape>: View {
    let shape: S
    var isPressed = false
    var castsShadow = true

    @Environment(\.theme) private var theme

    var body: some View {
        let shadow = theme.shadow.accent

        ZStack {
            shape
                .fill(isPressed ? theme.accent.pressed : theme.accent.fill)
                .shadow(color: shadow.color.opacity(castsShadow ? 1 : 0), radius: shadow.radius, y: shadow.y)
            shape.strokeBorder(theme.stroke.onAccent, lineWidth: theme.component.stroke)
        }
    }
}
