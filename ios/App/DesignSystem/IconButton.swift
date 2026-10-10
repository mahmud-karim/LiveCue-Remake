import SwiftUI

/// A button that shows only a symbol (design system 4.3). `label` is what assistive technology reads.
struct IconButton: View {
    enum Style: Equatable, Sendable {
        case raised, quiet, chrome
    }

    private let systemImage: String
    private let label: String
    private let style: Style
    private let isBusy: Bool
    private let action: @MainActor () -> Void

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
        // STUB(WP-05)
        Button(action: action) {
            Image(systemName: systemImage)
        }
        .accessibilityLabel(label)
        .disabled(isBusy)
    }
}
