import SwiftUI

/// A button that shows only a symbol (design system 4.3). `label` is what assistive technology reads: say
/// the action ("Copy answer"), not the picture.
///
/// The target is at least 44 by 44. `raised` and `quiet` are for content; `chrome` floats above content
/// and is glass.
struct IconButton: View {
    enum Style: Equatable, Sendable {
        case raised, quiet, chrome
    }

    private let systemImage: String
    private let label: String
    private let style: Style
    private let isBusy: Bool
    private let action: @MainActor () -> Void

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(
        systemImage: String,
        label: String,
        style: Style = .quiet,
        isBusy: Bool = false,
        action: @escaping @MainActor () -> Void
    ) {
        self.systemImage = systemImage
        self.label = label
        self.style = style
        self.isBusy = isBusy
        self.action = action
    }

    var body: some View {
        switch style {
        case .chrome:
            LCGlassButton(shape: .circle, action: perform) {
                symbol.frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            .frame(width: theme.size.minimumTarget, height: theme.size.minimumTarget)
            .accessibilityLabel(label)
        case .raised:
            Button(action: perform) { symbol }
                .buttonStyle(LCIconButtonStyle(isRaised: true))
                .accessibilityLabel(label)
        case .quiet:
            Button(action: perform) { symbol }
                .buttonStyle(LCIconButtonStyle(isRaised: false))
                .accessibilityLabel(label)
        }
    }

    /// A busy button keeps its full appearance and ignores input, so it is not disabled.
    private func perform() {
        if !isBusy {
            action()
        }
    }

    @ViewBuilder
    private var symbol: some View {
        if isBusy {
            Spinner(size: dynamicTypeSize.lcScaled(theme.component.buttonSpinner))
        } else {
            LCGlyph(systemImage, size: .regular, weight: .medium)
        }
    }
}

private struct LCIconButtonStyle: ButtonStyle {
    let isRaised: Bool

    func makeBody(configuration: Configuration) -> some View {
        LCIconButtonFace(configuration: configuration, isRaised: isRaised)
    }
}

private struct LCIconButtonFace: View {
    let configuration: ButtonStyleConfiguration
    let isRaised: Bool

    @Environment(\.theme) private var theme
    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        configuration.label
            .foregroundStyle(theme.ink.primary)
            .frame(minWidth: theme.size.minimumTarget, minHeight: theme.size.minimumTarget)
            .background {
                if isRaised {
                    LCRaisedSurface(shape: Circle(), isPressed: configuration.isPressed, castsShadow: isEnabled)
                } else {
                    Circle()
                        .fill(theme.state.pressed)
                        .opacity(configuration.isPressed ? 1 : 0)
                }
            }
            .contentShape(Circle())
            .scaleEffect(configuration.isPressed && isRaised ? theme.motion.pressedScale : 1)
            .opacity(isEnabled ? 1 : theme.opacity.disabled)
            .animation(theme.motion.press, value: configuration.isPressed)
    }
}
