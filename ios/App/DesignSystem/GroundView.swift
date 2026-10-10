import SwiftUI

/// The opaque ground behind every screen: a base colour and four soft blooms.
///
/// Geometry follows `docs/design/01-design-system.md` section 2.2. Bloom centres are fractions of the
/// full screen, safe areas included; radii are fractions of the longer side, so blooms stay circular.
struct GroundView: View {
    @Environment(\.theme) private var theme

    var body: some View {
        GeometryReader { proxy in
            let longerSide = max(proxy.size.width, proxy.size.height)

            ZStack {
                theme.ground.base

                ForEach(Array(theme.ground.blooms.enumerated()), id: \.offset) { _, bloom in
                    RadialGradient(
                        stops: [
                            Gradient.Stop(color: bloom.color, location: 0),
                            Gradient.Stop(color: bloom.color.opacity(0), location: theme.ground.bloomFadeEnd)
                        ],
                        center: bloom.center,
                        startRadius: 0,
                        endRadius: bloom.radius * longerSide
                    )
                }
            }
        }
        .ignoresSafeArea()
        .accessibilityHidden(true)
    }
}
