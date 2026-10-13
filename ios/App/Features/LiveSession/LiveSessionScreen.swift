import SwiftUI

/// The live session screen (screens 4). It replaces the tabs while a session exists.
///
/// Five regions, top to bottom: the status panel, the session notice, the transcript panel, the answer
/// feed and the control cluster. Only the feed scrolls, under the pinned cluster. At the accessibility
/// text sizes the whole column scrolls and the cluster stays pinned.
struct LiveSessionScreen: View {
    @Environment(LiveSessionViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    @State private var containerHeight: CGFloat = 0
    /// Counts the notices that appeared: the trigger of the warning haptic.
    @State private var noticeCount = 0

    var body: some View {
        ScreenScaffold(scrolls: dynamicTypeSize.isAccessibilitySize) {
            LiveStatusPanel(isCompact: isCompact)
            SessionNoticeBanner()
            LiveTranscriptPanel(isCompact: isCompact, expandedHeight: transcriptHeight)
            AnswerFeed(scrolls: !dynamicTypeSize.isAccessibilitySize)
        }
        .lcBottomBar {
            SessionControlCluster(
                isPaused: model.isPaused,
                isAssisting: model.isAssisting,
                isTransitioning: model.isTransitioning,
                onPauseResume: { Task { await model.togglePause() } },
                onAssist: { Task { await model.assist() } },
                onStop: { Task { await model.stop() } }
            )
        }
        .toolbar(.hidden, for: .navigationBar)
        .onContainerHeightChange { height in
            containerHeight = height
        }
        .onChange(of: model.notice) { _, notice in
            if notice != nil {
                noticeCount += 1
            }
        }
        .modifier(LiveSessionHaptics(noticeCount: noticeCount))
    }

    private var isCompact: Bool {
        theme.isCompactHeight(containerHeight)
    }

    private var transcriptHeight: CGFloat {
        LiveLayout.transcriptHeight(containerHeight: containerHeight)
    }
}

/// The haptics of the live screen (design system 7.2): each fires when the state actually changes.
private struct LiveSessionHaptics: ViewModifier {
    let noticeCount: Int

    @Environment(LiveSessionViewModel.self) private var model
    @Environment(SessionController.self) private var session

    func body(content: Content) -> some View {
        content
            .haptic(.listeningStarted, trigger: session.listeningStartedCount)
            .haptic(.pauseToggled, trigger: model.isPaused)
            .haptic(.answerArrived, trigger: session.answerArrivedCount)
            .haptic(.failure, trigger: session.assistFailedCount)
            .haptic(.warning, trigger: noticeCount)
    }
}
