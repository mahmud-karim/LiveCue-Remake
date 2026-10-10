import SwiftUI

/// The haptic events of design system 7.2. They follow the system setting; the app has no setting of its own.
enum Haptic: Equatable, Sendable {
    case listeningStarted, listeningStopped, pauseToggled, answerArrived, failure, warning, success
}

extension View {
    /// Sensory feedback for the event whenever `trigger` changes.
    func haptic<T: Equatable>(_ haptic: Haptic, trigger: T) -> some View {
        // STUB(WP-05)
        self
    }
}
