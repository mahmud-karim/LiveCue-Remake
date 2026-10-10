import Foundation
import LiveCueCore

/// What Session detail shows. Pure, so it is unit-tested without a view.
struct SessionDetailContent: Equatable {
    struct Fact: Equatable, Identifiable {
        let id: String
        let key: String
        let value: String
    }

    let title: String
    /// Empty when no usage was recorded.
    let usageFacts: [Fact]
    /// S-14 or S-15, Meta only.
    let usageCaption: String?
    let summary: String?
    let keyPoints: [String]
    let actionItems: [String]
    let segments: [TranscriptSegment]
    let turns: [AssistantTurn]

    init(session: Session) {
        // STUB(WP-11)
        title = session.title
        usageFacts = []
        usageCaption = nil
        summary = nil
        keyPoints = []
        actionItems = []
        segments = session.segments
        turns = session.assistantTurns
    }
}
