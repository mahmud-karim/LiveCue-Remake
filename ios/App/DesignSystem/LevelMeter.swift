import SwiftUI

/// The audio level visualiser, size S of design system 4.21. Hidden from accessibility.
struct LevelMeter: View {
    private let level: Float
    private let state: LevelMeterState

    init(level: Float, state: LevelMeterState) {
        self.level = level
        self.state = state
    }

    var body: some View {
        // STUB(WP-05)
        ProgressView(value: Double(min(max(level, 0), 1)))
            .accessibilityHidden(true)
    }
}
