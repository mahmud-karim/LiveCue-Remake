import SwiftUI

/// A determinate progress bar; `value` runs from 0 to 1.
struct LCProgressBar: View {
    private let value: Double
    private let failed: Bool

    init(value: Double, failed: Bool = false) {
        self.value = value
        self.failed = failed
    }

    var body: some View {
        // STUB(WP-05)
        ProgressView(value: min(max(value, 0), 1))
    }
}
