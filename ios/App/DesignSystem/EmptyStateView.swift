import SwiftUI

/// What a screen shows when it has nothing to list (design system 4.14).
struct EmptyStateView: View {
    private let systemImage: String
    private let title: String
    private let message: String

    init(systemImage: String, title: String, message: String) {
        self.systemImage = systemImage
        self.title = title
        self.message = message
    }

    var body: some View {
        // STUB(WP-05)
        VStack {
            Image(systemName: systemImage)
                .accessibilityHidden(true)
            Text(title).lcText(.headline)
            Text(message).lcText(.body)
        }
    }
}
