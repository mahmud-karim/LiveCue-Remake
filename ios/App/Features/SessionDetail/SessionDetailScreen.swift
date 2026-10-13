import LiveCueCore
import SwiftUI

/// Session detail (screens 13): one stored conversation. Read-only.
struct SessionDetailScreen: View {
    private let sessionID: UUID

    @Environment(HistoryModel.self) private var history

    init(sessionID: UUID) {
        self.sessionID = sessionID
    }

    var body: some View {
        let content = history.session(id: sessionID).map { SessionDetailContent(session: $0) }

        ScreenScaffold {
            if let content {
                SessionDetailSections(content: content)
            }
        }
        .navigationTitle(content?.title ?? "Conversation")
        .navigationBarTitleDisplayMode(.inline)
    }
}

/// One panel per section, each only if it has content.
///
/// Summary, key points, action items, the detected question, the answer and the details were written by
/// a model. They are drawn as they are: nothing in them is read as formatting or becomes a link.
struct SessionDetailSections: View {
    private let content: SessionDetailContent

    init(content: SessionDetailContent) {
        self.content = content
    }

    var body: some View {
        if !content.usageFacts.isEmpty {
            Panel("Transcription usage") {
                ForEach(content.usageFacts) { (fact: SessionDetailContent.Fact) in
                    DetailRow(fact.key, value: fact.value)
                }
                if let caption = content.usageCaption {
                    CaptionText(caption)
                }
            }
        }
        if let summary = content.summary {
            Panel("Summary") {
                SessionDetailText(text: summary)
            }
        }
        if !content.keyPoints.isEmpty {
            Panel("Key points") {
                SessionDetailList(items: content.keyPoints)
            }
        }
        if !content.actionItems.isEmpty {
            Panel("Action items") {
                SessionDetailList(items: content.actionItems)
            }
        }
        if !content.segments.isEmpty {
            Panel("Transcript") {
                ForEach(content.segments) { (segment: TranscriptSegment) in
                    TranscriptLine(segment.text)
                }
            }
        }
        if !content.turns.isEmpty {
            Panel("Assistant") {
                ForEach(content.turns) { (turn: AssistantTurn) in
                    SessionDetailTurnCard(turn: turn)
                }
            }
        }
    }
}

/// A text of the session in `type.body`, selectable.
private struct SessionDetailText: View {
    let text: String

    var body: some View {
        Text(verbatim: text)
            .lcText(.body)
            .frame(maxWidth: .infinity, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
            .textSelection(.enabled)
    }
}

/// A list of short items, each after a dot in a hanging indent (design system 4.20).
private struct SessionDetailList: View {
    let items: [String]

    @Environment(\.theme) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: theme.space.s1 + theme.space.half) {
            ForEach(Array(items.enumerated()), id: \.offset) { item in
                HStack(alignment: .firstTextBaseline, spacing: 0) {
                    Text(verbatim: "•")
                        .lcText(.body, color: theme.ink.secondary)
                        .frame(width: theme.space.s4, alignment: .leading)
                        .accessibilityHidden(true)
                    SessionDetailText(text: item.element)
                }
            }
        }
    }
}

/// One answer: the detected question, the answer, and the details if any, as a plain card.
private struct SessionDetailTurnCard: View {
    let turn: AssistantTurn

    @Environment(\.theme) private var theme

    var body: some View {
        Card {
            if showsQuestion {
                Text(verbatim: turn.detectedQuestion)
                    .lcText(.label, color: theme.ink.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
            }
            SessionDetailText(text: turn.answer)
            if !turn.details.isEmpty {
                Text(verbatim: turn.details)
                    .lcText(.body, color: theme.ink.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
        }
    }

    /// An OpenRouter answer has no detected question, only a placeholder that is never shown.
    private var showsQuestion: Bool {
        !turn.detectedQuestion.isEmpty && turn.detectedQuestion != AssistantTurn.latestConversation
    }
}
