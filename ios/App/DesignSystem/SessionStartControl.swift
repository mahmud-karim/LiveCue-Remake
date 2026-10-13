import SwiftUI

/// The Start control of Home (design system 4.17): a 64 high accent capsule that floats above the tab bar.
/// The caller sets its accessibility identifier.
///
/// It is chrome: tinted Liquid Glass on iOS 26 and a solid accent capsule before, the one accent-filled
/// control on the screen. Place it with `lcBottomBar`. The label says what pressing will do. While busy it
/// shows a spinner in place of the glyph, keeps its size and its full appearance, and ignores input; pass
/// the progressive title ("Connecting…") as `title`. It keeps its height at every text size: the label
/// stops growing at the second accessibility size and offers the Large Content Viewer instead.
struct SessionStartControl: View {
    private let title: String
    private let systemImage: String
    private let isBusy: Bool
    private let action: @MainActor () -> Void

    @Environment(\.theme) private var theme

    init(title: String, systemImage: String, isBusy: Bool = false, action: @escaping @MainActor () -> Void) {
        self.title = title
        self.systemImage = systemImage
        self.isBusy = isBusy
        self.action = action
    }

    var body: some View {
        LCGlassButton(prominent: true, action: perform) {
            HStack(spacing: theme.component.sessionControlGap) {
                if isBusy {
                    Spinner()
                } else {
                    LCGlyph(systemImage, size: .large)
                }
                // Two lines at most, a little smaller if that is what it takes to stay inside the capsule.
                Text(verbatim: title)
                    .font(TextRole.headline.font)
                    .lineLimit(2)
                    .minimumScaleFactor(theme.component.sessionControlLabelShrink)
                    .multilineTextAlignment(.center)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(
            minWidth: theme.component.sessionControlMinWidth,
            maxWidth: theme.component.sessionControlMaxWidth,
            minHeight: theme.size.sessionControl,
            maxHeight: theme.size.sessionControl
        )
        .frame(maxWidth: .infinity)
        .dynamicTypeSize(...DynamicTypeSize.accessibility2)
        .accessibilityShowsLargeContentViewer {
            Label(title, systemImage: systemImage)
        }
    }

    private func perform() {
        if !isBusy {
            action()
        }
    }
}
