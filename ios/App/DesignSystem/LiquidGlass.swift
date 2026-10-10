import SwiftUI

// The only file in the app that names iOS 26 Liquid Glass symbols. CI fails if one appears elsewhere
// (scripts/ci/lint_swift.sh).
//
// The app is built with the iOS 26 SDK and deploys back to iOS 18.0. Everything below is called the same
// way on both systems: on iOS 26 it uses the system material, and before that it draws the fallback from
// `docs/design/01-design-system.md` section 3.3. Rules that keep this compiling for iOS 18:
//
// 1. Every iOS 26 symbol sits inside `if #available(iOS 26.0, *)`. No iOS 26 type appears in a signature.
// 2. The two branches of an availability check are never merged with `?:`; their types differ.
// 3. Nothing is drawn on top of native glass: no stroke, shadow, sheen or opacity. Those tokens belong to
//    the fallback only.
//
// Glass is for the chrome layer (controls that float above content). Panels, cards and rows are not glass.
// Only the regular variant is used, and tint is reserved for the one prominent button.

/// Facts about the glass path in use.
enum LiquidGlass {
    /// `true` when the system draws Liquid Glass (iOS 26 and later), `false` when the fallback is drawn.
    static var isNative: Bool {
        if #available(iOS 26.0, *) {
            return true
        }
        return false
    }
}

// MARK: - Surfaces

extension View {
    /// Chrome-layer glass behind this view. Native on iOS 26, built from a material and tokens before that.
    ///
    /// Apply it after the view's own padding and appearance modifiers. Pass `interactive: true` only for
    /// surfaces that respond to touch; the system then supplies the pressed state. On iOS 18 this modifier
    /// draws no pressed state, so use `LCGlassButton` for anything that is tapped.
    func daylightChrome<S: InsettableShape>(in shape: S, interactive: Bool = false) -> some View {
        modifier(DaylightChrome(shape: shape, isInteractive: interactive))
    }

    /// Gives a glass surface an identity so that it morphs between states inside one `LCGlassContainer`.
    /// Does nothing on iOS 18.
    @ViewBuilder
    func lcGlassID(_ id: some Hashable & Sendable, in namespace: Namespace.ID) -> some View {
        if #available(iOS 26.0, *) {
            glassEffectID(id, in: namespace)
        } else {
            self
        }
    }
}

private struct DaylightChrome<S: InsettableShape>: ViewModifier {
    let shape: S
    let isInteractive: Bool

    func body(content: Content) -> some View {
        if #available(iOS 26.0, *) {
            content.glassEffect(.regular.interactive(isInteractive), in: shape)
        } else {
            content.background { FallbackChrome(shape: shape) }
        }
    }
}

/// Groups nearby glass surfaces so that the system renders and morphs them together on iOS 26.
/// A transparent wrapper on iOS 18. Lay the surfaces out yourself; this adds no layout.
struct LCGlassContainer<Content: View>: View {
    private let spacing: CGFloat?
    private let content: Content

    init(spacing: CGFloat? = nil, @ViewBuilder content: () -> Content) {
        self.spacing = spacing
        self.content = content()
    }

    var body: some View {
        if #available(iOS 26.0, *) {
            GlassEffectContainer(spacing: spacing) { content }
        } else {
            content
        }
    }
}

// MARK: - Buttons

/// A capsule button for the chrome layer.
///
/// On iOS 26 it is a system glass button. On iOS 18 the regular button is fallback chrome and the prominent
/// button is a solid accent capsule. The label colour is set here on both paths, so pass an unstyled label.
/// At most one prominent button should be visible at a time.
struct LCGlassButton<Label: View>: View {
    private let isProminent: Bool
    private let action: @MainActor () -> Void
    private let label: Label

    @Environment(\.theme) private var theme

    init(prominent: Bool = false, action: @escaping @MainActor () -> Void, @ViewBuilder label: () -> Label) {
        self.isProminent = prominent
        self.action = action
        self.label = label()
    }

    var body: some View {
        if #available(iOS 26.0, *) {
            if isProminent {
                Button(action: action) { label.foregroundStyle(theme.ink.onAccent) }
                    .buttonStyle(.glassProminent)
                    .buttonBorderShape(.capsule)
                    .controlSize(.large)
                    .tint(theme.accent.fill)
            } else {
                Button(action: action) { label.foregroundStyle(theme.ink.primary) }
                    .buttonStyle(.glass)
                    .buttonBorderShape(.capsule)
                    .controlSize(.large)
            }
        } else {
            Button(action: action) { label }
                .buttonStyle(FallbackGlassButtonStyle(isProminent: isProminent))
        }
    }
}

// MARK: - Bars and scrolling

extension View {
    /// Pins `bar` above the bottom safe area. On iOS 26 the scroll edge effect extends under it.
    func lcBottomBar<Bar: View>(@ViewBuilder _ bar: () -> Bar) -> some View {
        modifier(LCBottomBar(bar: bar()))
    }

    /// Chooses the scroll edge effect under bars on iOS 26. Does nothing on iOS 18.
    @ViewBuilder
    func lcScrollEdge(hard: Bool = false, for edges: Edge.Set) -> some View {
        if #available(iOS 26.0, *) {
            scrollEdgeEffectStyle(hard ? ScrollEdgeEffectStyle.hard : ScrollEdgeEffectStyle.soft, for: edges)
        } else {
            self
        }
    }

    /// Lets the tab bar shrink while content scrolls down on iOS 26. Does nothing on iOS 18.
    @ViewBuilder
    func lcTabBarMinimizesOnScroll() -> some View {
        if #available(iOS 26.0, *) {
            tabBarMinimizeBehavior(.onScrollDown)
        } else {
            self
        }
    }
}

private struct LCBottomBar<Bar: View>: ViewModifier {
    let bar: Bar

    func body(content: Content) -> some View {
        if #available(iOS 26.0, *) {
            content.safeAreaBar(edge: .bottom) { bar }
        } else {
            content.safeAreaInset(edge: .bottom) { bar }
        }
    }
}

/// A fixed gap that splits toolbar items into separate glass groups on iOS 26. Empty on iOS 18.
struct LCToolbarFixedSpacer: ToolbarContent {
    var placement: ToolbarItemPlacement = .automatic

    var body: some ToolbarContent {
        if #available(iOS 26.0, *) {
            ToolbarSpacer(.fixed, placement: placement)
        }
    }
}

// MARK: - Fallback drawing (iOS 18 to 25)

/// Floating chrome built from a system material and tokens.
///
/// Bottom to top: material with the floating shadow, the floating fill, an optional sheen, the edge stroke.
/// With Reduce Transparency on, the material and translucent fill give way to one opaque fill.
private struct FallbackChrome<S: InsettableShape>: View {
    let shape: S
    var showsSheen = true
    var isPressed = false

    @Environment(\.theme) private var theme
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency

    var body: some View {
        let shadow = theme.shadow.floating

        ZStack {
            if reduceTransparency {
                shape
                    .fill(isPressed ? theme.surface.raisedPressed : theme.surface.floatingSolid)
                    .shadow(color: shadow.color, radius: shadow.radius, y: shadow.y)
            } else {
                shape
                    .fill(.ultraThinMaterial)
                    .shadow(color: shadow.color, radius: shadow.radius, y: shadow.y)
                shape
                    .fill(isPressed ? theme.surface.raisedPressed : theme.surface.floating)
                if showsSheen {
                    shape.fill(sheen)
                }
            }
            shape.strokeBorder(edge, lineWidth: 1)
        }
    }

    private var sheen: LinearGradient {
        LinearGradient(
            stops: [
                Gradient.Stop(color: theme.sheen.top, location: 0),
                Gradient.Stop(color: theme.sheen.top.opacity(0), location: theme.sheen.fadeEnd)
            ],
            startPoint: .top,
            endPoint: .bottom
        )
    }

    private var edge: LinearGradient {
        LinearGradient(
            colors: [theme.stroke.edgeTop, theme.stroke.edgeBottom],
            startPoint: .top,
            endPoint: .bottom
        )
    }
}

/// The iOS 18 look of `LCGlassButton`. The sheen is left off: the design system keeps it for panels and bars.
private struct FallbackGlassButtonStyle: ButtonStyle {
    let isProminent: Bool

    @Environment(\.theme) private var theme
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(isProminent ? theme.ink.onAccent : theme.ink.primary)
            .padding(.horizontal, 24)
            .frame(minHeight: 52)
            .background {
                if isProminent {
                    Capsule()
                        .fill(configuration.isPressed ? theme.accent.pressed : theme.accent.fill)
                        .shadow(
                            color: theme.shadow.accent.color,
                            radius: theme.shadow.accent.radius,
                            y: theme.shadow.accent.y
                        )
                    Capsule()
                        .strokeBorder(theme.stroke.onAccent, lineWidth: 1)
                } else {
                    FallbackChrome(shape: Capsule(), showsSheen: false, isPressed: configuration.isPressed)
                }
            }
            .contentShape(Capsule())
            .scaleEffect(configuration.isPressed ? theme.motion.pressedScale : 1)
            .opacity(isEnabled ? 1 : theme.opacity.disabled)
            .animation(theme.motion.press, value: configuration.isPressed)
    }
}
