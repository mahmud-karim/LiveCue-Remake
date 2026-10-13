import LiveCueCore
import SwiftUI
import XCTest
@testable import LiveCueRemake

/// Session detail: usage rows and sections only with content (AC-IOS-084, 086, 088).
final class SessionDetailContentTests: XCTestCase {
    private let completeCaption = "Calculated from reported audio usage; not a confirmed charge. Credits and billing adjustments may change your bill."
    private let incompleteCaption = "Connection ended before all usage could be confirmed. This is an incomplete estimate, not a Meta invoice."

    /// Usage with one stream of `seconds` of audio that was heard and reported.
    private func usage(
        provider: String,
        seconds: Double,
        completed: Bool = true,
        firstTextMs: Double? = nil,
        finalizationMs: Double? = nil
    ) -> TranscriptionUsage {
        var usage = TranscriptionUsage(provider: provider)
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: seconds * 1_000, processedMs: seconds * 1_000, hasTranscript: true)
        usage.finish(stream, completed: completed)
        usage.firstTextMs = firstTextMs
        usage.finalizationMs = finalizationMs
        return usage
    }

    private func session(
        title: String = "Launch plan review",
        segments: [String] = [],
        turns: [AssistantTurn] = [],
        notes: SessionNotes = SessionNotes(),
        usage: TranscriptionUsage? = nil
    ) -> Session {
        var session = Session(
            title: title,
            segments: segments.enumerated().map { index, text in
                TranscriptSegment(text: text, startSeconds: Double(index), endSeconds: Double(index) + 1)
            },
            assistantTurns: turns,
            notes: notes
        )
        session.transcriptionUsage = usage
        return session
    }

    private func keys(_ content: SessionDetailContent) -> [String] {
        content.usageFacts.map(\.key)
    }

    private func values(_ content: SessionDetailContent) -> [String] {
        content.usageFacts.map(\.value)
    }

    // MARK: - Sections only with content (AC-IOS-084, 088)

    func testAnEmptySessionHasNoSections() {
        let content = SessionDetailContent(session: session())

        XCTAssertEqual(content.title, "Launch plan review")
        XCTAssertEqual(content.usageFacts, [], "No usage was recorded, so there is no usage section.")
        XCTAssertNil(content.usageCaption)
        XCTAssertNil(content.summary)
        XCTAssertEqual(content.keyPoints, [])
        XCTAssertEqual(content.actionItems, [])
        XCTAssertEqual(content.segments, [])
        XCTAssertEqual(content.turns, [])
    }

    func testEverySectionWithContentIsFilled() {
        let turns = [
            AssistantTurn(detectedQuestion: "Can we still ship on March 10?", answer: "Yes, if two items move.", details: "Details."),
            AssistantTurn(detectedQuestion: AssistantTurn.latestConversation, answer: "A second answer.", details: "")
        ]
        let notes = SessionNotes(
            summary: "The team agreed to keep March 10.",
            keyPoints: ["Onboarding is the main risk.", "Two items move to the next sprint."],
            actionItems: ["Confirm the remaining screens by Friday."]
        )
        let stored = session(segments: ["First.", "Second."], turns: turns, notes: notes)

        let content = SessionDetailContent(session: stored)

        XCTAssertEqual(content.summary, "The team agreed to keep March 10.")
        XCTAssertEqual(content.keyPoints, ["Onboarding is the main risk.", "Two items move to the next sprint."])
        XCTAssertEqual(content.actionItems, ["Confirm the remaining screens by Friday."])
        XCTAssertEqual(content.segments, stored.segments)
        XCTAssertEqual(content.segments.map(\.text), ["First.", "Second."])
        XCTAssertEqual(content.turns, turns)
    }

    func testNotesWithoutTextCountAsNoContent() {
        let notes = SessionNotes(summary: "  \n", keyPoints: ["", "  ", "A real point."], actionItems: ["", "\n"])

        let content = SessionDetailContent(session: session(notes: notes))

        XCTAssertNil(content.summary)
        XCTAssertEqual(content.keyPoints, ["A real point."])
        XCTAssertEqual(content.actionItems, [])
    }

    // MARK: - Usage (AC-IOS-086)

    func testCompleteMetaUsageShowsEveryRowAndTheEstimateCaption() {
        let recorded = usage(provider: "meta", seconds: 252, firstTextMs: 1_200, finalizationMs: 410)

        let content = SessionDetailContent(session: session(usage: recorded))

        XCTAssertEqual(
            content.usageFacts,
            [
                SessionDetailContent.Fact(id: "cost", key: "Estimated cost", value: "$0.01260 USD"),
                SessionDetailContent.Fact(id: "provider", key: "Provider", value: "Meta Muse · cloud"),
                SessionDetailContent.Fact(id: "first-words", key: "First words from stream start", value: "1.20 s"),
                SessionDetailContent.Fact(id: "finalization", key: "Last stream finalization", value: "0.41 s"),
                SessionDetailContent.Fact(id: "reported", key: "Processed audio reported", value: "252 seconds"),
                SessionDetailContent.Fact(id: "streams", key: "Streams", value: "1"),
                SessionDetailContent.Fact(id: "rate", key: "Rate saved with session", value: "$0.18 / hour")
            ]
        )
        XCTAssertEqual(content.usageCaption, completeCaption)
        XCTAssertEqual(Set(content.usageFacts.map(\.id)).count, content.usageFacts.count, "The row ids are unique.")
    }

    func testIncompleteMetaUsageShowsTheIncompleteCaption() {
        let recorded = usage(provider: "meta", seconds: 62, completed: false)

        let content = SessionDetailContent(session: session(usage: recorded))

        XCTAssertEqual(content.usageCaption, incompleteCaption)
        // The timings are not known, so their rows are not shown.
        XCTAssertEqual(
            keys(content),
            ["Estimated cost", "Provider", "Processed audio reported", "Streams", "Rate saved with session"]
        )
        XCTAssertEqual(values(content), ["$0.00310 USD", "Meta Muse · cloud", "62 seconds", "1", "$0.18 / hour"])
    }

    func testPCLocalUsageShowsProviderAndTimingsButNoCost() {
        let recorded = usage(provider: "qwen3", seconds: 600, completed: false, firstTextMs: 850, finalizationMs: 2_340)

        let content = SessionDetailContent(session: session(usage: recorded))

        XCTAssertEqual(keys(content), ["Provider", "First words from stream start", "Last stream finalization"])
        XCTAssertEqual(values(content), ["Qwen3 1.7B · PC", "0.85 s", "2.34 s"])
        XCTAssertNil(content.usageCaption, "The captions are about Meta's bill.")
        XCTAssertFalse(values(content).contains { $0.contains("$") })
    }

    func testOnDeviceUsageShowsOnlyTheProvider() {
        let voz = SessionDetailContent(session: session(usage: TranscriptionUsage(provider: "voz")))
        XCTAssertEqual(keys(voz), ["Provider"])
        XCTAssertEqual(values(voz), ["Voz · iPhone"])
        XCTAssertNil(voz.usageCaption)

        let unknown = SessionDetailContent(
            session: session(usage: TranscriptionUsage(provider: "a-provider-this-build-does-not-know"))
        )
        XCTAssertEqual(values(unknown), ["On-device"])
    }

    // MARK: - The screen

    /// A full session, hosted in a window: a picture of it is kept with the test results, and a link a
    /// model wrote is read as its text and its address, never as a link (1.1.0).
    @MainActor
    func testTheSectionsAreDrawn() async {
        let hostile = "[label](https://pc.example.test/x) https://pc.example.test/y"
        let turns = [
            AssistantTurn(
                detectedQuestion: "Can we still ship on March 10?",
                answer: "Yes. Move the analytics dashboard and the referral banner to the next sprint.",
                details: "Onboarding needs one more week of design."
            ),
            AssistantTurn(detectedQuestion: AssistantTurn.latestConversation, answer: hostile, details: "")
        ]
        let notes = SessionNotes(
            summary: "The team reviewed the launch plan and agreed to keep March 10 by moving two items to the next sprint.",
            keyPoints: ["Onboarding is the main risk; design needs one more week.", "Analytics dashboard and referral banner move to the next sprint."],
            actionItems: ["Confirm remaining onboarding screens with design by Friday."]
        )
        let stored = session(
            title: "Launch plan review and what to cut for March",
            segments: [
                "Thanks for making time. I wanted to go through the launch plan before Thursday.",
                "The main question is whether we can still ship on March 10."
            ],
            turns: turns,
            notes: notes,
            usage: usage(provider: "meta", seconds: 252, firstTextMs: 1_200, finalizationMs: 410)
        )
        let content = SessionDetailContent(session: stored)

        let report = ScreensBHost.show(
            NavigationStack {
                ScreenScaffold {
                    SessionDetailSections(content: content)
                }
                .navigationTitle(content.title)
                .navigationBarTitleDisplayMode(.inline)
            },
            pictureNamed: "wp11-session-detail",
            in: self
        )

        XCTAssertEqual(report.linkCount, 0, "Nothing a model wrote becomes a link.")
        let shown = "label (https://pc.example.test/x) https://pc.example.test/y"
        XCTAssertEqual(AnswerMarkup.plainText(hostile), shown)
        if report.labels.contains(where: { $0.contains("pc.example.test") }) {
            XCTAssertTrue(report.labels.contains { $0.contains(shown) }, "Labels: \(report.labels)")
        }
    }
}
