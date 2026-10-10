import SwiftUI

/// The live control cluster (design system 4.17). It carries its own identifiers and labels:
/// `pause-session` ("Pause" / "Resume"), `assist-button` ("Assist"; busy "Replying…"), `end-session`
/// ("Stop conversation"). All three are disabled while `isAssisting` or `isTransitioning`; Stop shows a
/// spinner while transitioning.
struct SessionControlCluster: View {
    private let isPaused: Bool
    private let isAssisting: Bool
    private let isTransitioning: Bool
    private let onPauseResume: @MainActor () -> Void
    private let onAssist: @MainActor () -> Void
    private let onStop: @MainActor () -> Void

    init(
        isPaused: Bool,
        isAssisting: Bool,
        isTransitioning: Bool,
        onPauseResume: @escaping @MainActor () -> Void,
        onAssist: @escaping @MainActor () -> Void,
        onStop: @escaping @MainActor () -> Void
    ) {
        self.isPaused = isPaused
        self.isAssisting = isAssisting
        self.isTransitioning = isTransitioning
        self.onPauseResume = onPauseResume
        self.onAssist = onAssist
        self.onStop = onStop
    }

    var body: some View {
        // STUB(WP-05)
        HStack {
            Button(action: onPauseResume) {
                Text(verbatim: pauseTitle)
            }
            .accessibilityIdentifier("pause-session")
            Button(action: onAssist) {
                Text(verbatim: assistTitle)
            }
            .accessibilityIdentifier("assist-button")
            Button(action: onStop) {
                Text(verbatim: "Stop conversation")
            }
            .accessibilityIdentifier("end-session")
        }
        .disabled(isAssisting || isTransitioning)
    }

    private var pauseTitle: String {
        isPaused ? "Resume" : "Pause"
    }

    private var assistTitle: String {
        isAssisting ? "Replying…" : "Assist"
    }
}
