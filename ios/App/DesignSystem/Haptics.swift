import SwiftUI

/// The haptic events of design system 7.2. They follow the system setting; the app has no setting of its own.
///
/// Haptics confirm what the user cannot look at, quietly enough to go unnoticed by the other person in the
/// conversation. Fire one when the state actually changes, not on the tap, and never per streamed chunk,
/// per transcript line or on a loop.
enum Haptic: Equatable, Sendable {
    case listeningStarted, listeningStopped, pauseToggled, answerArrived, failure, warning, success

    fileprivate var feedback: SensoryFeedback {
        switch self {
        case .listeningStarted: return .impact(weight: .medium)
        case .listeningStopped: return .impact(flexibility: .rigid, intensity: 0.7)
        case .pauseToggled: return .impact(weight: .light)
        case .answerArrived: return .impact(flexibility: .soft, intensity: 0.6)
        case .failure: return .error
        case .warning: return .warning
        case .success: return .success
        }
    }
}

extension View {
    /// Sensory feedback for the event whenever `trigger` changes.
    func haptic<T: Equatable>(_ haptic: Haptic, trigger: T) -> some View {
        sensoryFeedback(haptic.feedback, trigger: trigger)
    }
}
