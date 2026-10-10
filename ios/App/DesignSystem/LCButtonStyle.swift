import SwiftUI

/// The look of `LCButton`, for `NavigationLink` and `Link` labels too.
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
        // STUB(WP-05)
        configuration.label
            .lcText(.label)
            .frame(maxWidth: fullWidth ? .infinity : nil)
    }
}
