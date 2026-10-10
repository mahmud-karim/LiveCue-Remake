import SwiftUI

/// An indeterminate progress ring.
struct Spinner: View {
    private let size: CGFloat

    init(size: CGFloat = 20) {
        self.size = size
    }

    var body: some View {
        // STUB(WP-05)
        ProgressView()
            .frame(width: size, height: size)
    }
}
