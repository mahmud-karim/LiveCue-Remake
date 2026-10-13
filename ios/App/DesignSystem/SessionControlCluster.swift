import SwiftUI

/// The live control cluster (design system 4.17). It carries its own identifiers and labels:
/// `pause-session` ("Pause" / "Resume"), `assist-button` ("Assist"; busy "Replying…"), `end-session`
/// ("Stop conversation"). All three are disabled while `isAssisting` or `isTransitioning`; Stop shows a
/// spinner while transitioning.
///
/// Three chrome controls, 12 apart and centred on each other: a 52 glass circle that pauses or resumes,
/// the 64 high accent capsule for Assist, which takes the width that is left, and a 52 glass circle that
/// stops. Assist is the hero here, so it has the accent; the red stop glyph is the only red. Place the
/// cluster with `lcBottomBar`. It keeps its sizes at every text size.
struct SessionControlCluster: View {
    private let isPaused: Bool
    private let isAssisting: Bool
    private let isTransitioning: Bool
    private let onPauseResume: @MainActor () -> Void
    private let onAssist: @MainActor () -> Void
    private let onStop: @MainActor () -> Void

    @Environment(\.theme) private var theme

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
        // One container, so that the three surfaces are rendered together on iOS 26.
        LCGlassContainer(spacing: theme.space.s2) {
            HStack(alignment: .center, spacing: theme.space.controlGap) {
                pauseButton
                assistButton
                stopButton
            }
        }
        .disabled(isAssisting || isTransitioning)
        .dynamicTypeSize(...DynamicTypeSize.accessibility2)
    }

    private var pauseButton: some View {
        LCGlassButton(shape: .circle, action: onPauseResume) {
            LCGlyph(isPaused ? "play.fill" : "pause.fill", size: .large)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(width: theme.size.clusterCircle, height: theme.size.clusterCircle)
        .dynamicTypeSize(...DynamicTypeSize.lcRoundControlLimit)
        .accessibilityLabel(pauseTitle)
        .accessibilityIdentifier("pause-session")
        .accessibilityShowsLargeContentViewer {
            Label(pauseTitle, systemImage: isPaused ? "play.fill" : "pause.fill")
        }
    }

    private var assistButton: some View {
        LCGlassButton(prominent: true, action: onAssist) {
            // The label gives way to its glyph when larger text leaves no room for the word.
            ViewThatFits(in: .horizontal) {
                HStack(spacing: theme.component.sessionControlGap) {
                    assistGlyph
                    Text(verbatim: assistTitle)
                        .font(TextRole.headline.font)
                        .lineLimit(1)
                }
                assistGlyph
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(height: theme.size.sessionControl)
        .accessibilityLabel(assistTitle)
        .accessibilityIdentifier("assist-button")
        .accessibilityShowsLargeContentViewer {
            Label(assistTitle, systemImage: "sparkles")
        }
    }

    private var stopButton: some View {
        LCGlassButton(shape: .circle, action: onStop) {
            stopGlyph
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(width: theme.size.clusterCircle, height: theme.size.clusterCircle)
        .dynamicTypeSize(...DynamicTypeSize.lcRoundControlLimit)
        .accessibilityLabel("Stop conversation")
        .accessibilityIdentifier("end-session")
        .accessibilityShowsLargeContentViewer {
            Label("Stop conversation", systemImage: "stop.fill")
        }
    }

    @ViewBuilder
    private var assistGlyph: some View {
        if isAssisting {
            Spinner()
        } else {
            LCGlyph("sparkles", size: .large)
        }
    }

    @ViewBuilder
    private var stopGlyph: some View {
        if isTransitioning {
            Spinner()
        } else {
            LCGlyph("stop.fill", size: .large)
                .foregroundStyle(theme.status.danger)
        }
    }

    private var pauseTitle: String {
        isPaused ? "Resume" : "Pause"
    }

    private var assistTitle: String {
        isAssisting ? "Replying…" : "Assist"
    }
}
