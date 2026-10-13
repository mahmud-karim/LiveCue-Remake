import SwiftUI

/// The live pulse (design system 7.1): opacity from 1 to `opacity.caretLow` and back, once every
/// `motion.pulse`. It is the only looping motion in the app besides the spinner, and it runs only while
/// something is live. With Reduce Motion the view stays at full opacity.
private struct LCPulse: ViewModifier {
    let isActive: Bool

    @Environment(\.theme) private var theme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        if isActive && !reduceMotion {
            content.phaseAnimator([false, true]) { view, isLow in
                view.opacity(isLow ? theme.opacity.caretLow : 1)
            } animation: { _ in
                theme.component.pulse
            }
        } else {
            content
        }
    }
}

extension View {
    func lcPulse(_ isActive: Bool = true) -> some View {
        modifier(LCPulse(isActive: isActive))
    }
}
