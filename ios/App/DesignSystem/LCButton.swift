import SwiftUI

/// A text button (design system 4.1 and 4.2).
struct LCButton: View {
    private let title: String
    private let systemImage: String?
    private let kind: LCButtonKind
    private let size: LCButtonSize
    private let fullWidth: Bool
    private let isBusy: Bool
    private let busyTitle: String?
    private let action: @MainActor () -> Void

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
        // STUB(WP-05)
        Button(action: action) {
            Text(isBusy ? (busyTitle ?? title) : title)
        }
        .buttonStyle(LCButtonStyle(kind: kind, size: size, fullWidth: fullWidth))
        .disabled(isBusy)
    }
}
