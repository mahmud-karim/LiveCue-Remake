import SwiftUI

/// A text button (design system 4.1 and 4.2).
///
/// A screen shows one primary button at a time. A quiet button is accent text with no fill; give it an
/// icon, so that colour is not the only thing that marks it as an action (rule C-3).
struct LCButton: View {
    private let title: String
    private let systemImage: String?
    private let kind: LCButtonKind
    private let size: LCButtonSize
    private let fullWidth: Bool
    private let isBusy: Bool
    private let busyTitle: String?
    private let action: @MainActor () -> Void

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    /// Busy: a spinner replaces the icon, the title becomes `busyTitle` when given, input is ignored, width is kept.
    init(
        _ title: String,
        systemImage: String? = nil,
        kind: LCButtonKind = .primary,
        size: LCButtonSize = .regular,
        fullWidth: Bool = false,
        isBusy: Bool = false,
        busyTitle: String? = nil,
        action: @escaping @MainActor () -> Void
    ) {
        self.title = title
        self.systemImage = systemImage
        self.kind = kind
        self.size = size
        self.fullWidth = fullWidth
        self.isBusy = isBusy
        self.busyTitle = busyTitle
        self.action = action
    }

    var body: some View {
        Button(action: perform) {
            // The resting label always takes part in layout, so the width does not change while busy.
            restingLabel
                .opacity(isBusy ? 0 : 1)
                .overlay {
                    if isBusy {
                        busyLabel
                    }
                }
        }
        .buttonStyle(LCButtonStyle(kind: kind, size: size, fullWidth: fullWidth))
        .accessibilityLabel(isBusy ? (busyTitle ?? title) : title)
    }

    /// A busy button keeps its full appearance and ignores input, so it is not disabled.
    private func perform() {
        if !isBusy {
            action()
        }
    }

    private var restingLabel: some View {
        HStack(spacing: theme.space.inlineGap) {
            if let systemImage {
                LCGlyph(systemImage, size: .regular)
            }
            Text(verbatim: title)
        }
    }

    private var busyLabel: some View {
        HStack(spacing: theme.space.inlineGap) {
            Spinner(size: dynamicTypeSize.lcScaled(theme.component.buttonSpinner))
            Text(verbatim: busyTitle ?? title)
                .lineLimit(1)
        }
        .fixedSize()
    }
}
