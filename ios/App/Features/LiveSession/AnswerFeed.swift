import Accessibility
import LiveCueCore
import SwiftUI

/// The answer feed `assistant-feed`: the error card, the answer in progress, the latest answer and the
/// earlier ones.
///
/// It is the one region of the live screen that scrolls, and it goes back to the top when an answer
/// lands, when an Assist starts and when an error appears. At the accessibility text sizes the whole
/// screen scrolls instead, and the feed is laid out in that column (`scrolls: false`).
struct AnswerFeed: View {
    private let scrolls: Bool

    @Environment(LiveSessionViewModel.self) private var model
    @Environment(\.theme) private var theme

    @State private var position = ScrollPosition(edge: .top)

    init(scrolls: Bool = true) {
        self.scrolls = scrolls
    }

    var body: some View {
        if scrolls {
            ScrollView {
                AnswerFeedContent()
                    .padding(.horizontal, theme.space.screenInset)
                    .padding(.top, theme.space.s2)
                    .padding(.bottom, theme.space.s6)
            }
            .scrollPosition($position)
            .accessibilityIdentifier("assistant-feed")
            // The scroll view reaches the screen edges and a little into the gap above it, so that the
            // shadow of a card is not cut off; its content keeps the column's insets.
            .padding(.horizontal, -theme.space.screenInset)
            .padding(.top, -theme.space.s2)
            .onChange(of: model.feedScrollTrigger) { _, _ in
                scrollToTop()
            }
        } else {
            AnswerFeedContent()
                .accessibilityElement(children: .contain)
                .accessibilityIdentifier("assistant-feed")
        }
    }

    private func scrollToTop() {
        withAnimation(theme.motion.base) {
            position.scrollTo(edge: .top)
        }
    }
}

/// What the feed holds, newest first.
private struct AnswerFeedContent: View {
    @Environment(LiveSessionViewModel.self) private var model
    @Environment(\.theme) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: theme.space.panelGap) {
            if let error = model.errorText {
                AssistErrorCard(message: error, canRetry: model.canRetry, onRetry: retry)
            }
            if let state = model.inProgress {
                InProgressCard(state: state)
            }
            if let turn = model.latestTurn {
                latestCard(turn)
            }
            if !model.earlierTurns.isEmpty {
                Panel("Earlier answers") {
                    ForEach(model.earlierTurns) { turn in
                        earlierCard(turn)
                    }
                }
            }
            if model.showsEmptyState {
                EmptyStateView(
                    systemImage: "sparkles",
                    title: "Your assistant",
                    message: "Tap Assist when you want help with a question. Answers appear here while the conversation keeps going."
                )
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .onChange(of: model.latestTurn?.id) { _, _ in
            announceLatestAnswer()
        }
    }

    /// The latest card is a new view for every new answer, so its disclosures start closed.
    private func latestCard(_ turn: AssistantTurn) -> some View {
        AnswerCard(
            turn: turn,
            isLatest: true,
            canRetry: model.canRetry,
            onRetry: retry,
            onCopy: { model.copy(turn) }
        )
        .id(turn.id)
    }

    private func earlierCard(_ turn: AssistantTurn) -> some View {
        AnswerCard(turn: turn, isLatest: false, canRetry: false, onRetry: {}, onCopy: { model.copy(turn) })
    }

    private func retry() {
        Task { await model.retry() }
    }

    /// A finished answer is announced once, in full (screens 4.8).
    private func announceLatestAnswer() {
        guard let answer = model.latestTurn?.answer, !answer.isEmpty else {
            return
        }
        AccessibilityNotification.Announcement(AttributedString(answer)).post()
    }
}
