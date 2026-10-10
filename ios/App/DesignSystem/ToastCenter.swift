import Observation
import SwiftUI

/// The one toast of the app. A toast is visible for 4 s; a new toast replaces the current one.
@MainActor @Observable final class ToastCenter {
    private(set) var message: String? = nil

    private let clock: any AppClock

    init(clock: any AppClock) {
        self.clock = clock
    }

    func show(_ text: String) {
        // STUB(WP-05)
        message = text
    }
}

/// Shows the current toast. It reads `ToastCenter` from the environment and is placed by `RootView`.
struct ToastOverlay: View {
    @Environment(ToastCenter.self) private var toasts

    init() {}

    var body: some View {
        // STUB(WP-05)
        if let message = toasts.message {
            Text(message).lcText(.label)
        }
    }
}
