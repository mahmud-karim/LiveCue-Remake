import LiveCueCore
import SwiftUI
import XCTest
@testable import LiveCueRemake

/// History rows, their usage labels and the confirmed deletion (AC-IOS-081, 083 as changed, 086, 088).
final class HistoryViewModelTests: XCTestCase {
    private let start = Date(timeIntervalSince1970: 1_790_000_000)

    /// Usage with one stream of `seconds` of audio that was heard and reported.
    private func usage(provider: String, seconds: Double, completed: Bool = true) -> TranscriptionUsage {
        var usage = TranscriptionUsage(provider: provider)
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: seconds * 1_000, processedMs: seconds * 1_000, hasTranscript: true)
        usage.finish(stream, completed: completed)
        return usage
    }

    private func session(
        _ title: String,
        minutesAgo: Double = 0,
        segments: Int = 2,
        turns: Int = 1,
        usage: TranscriptionUsage? = nil
    ) -> Session {
        let startedAt = start.addingTimeInterval(-minutesAgo * 60)
        var session = Session(
            title: title,
            startedAt: startedAt,
            endedAt: startedAt.addingTimeInterval(300),
            segments: (0..<segments).map { index in
                TranscriptSegment(text: "Segment \(index)", startSeconds: Double(index), endSeconds: Double(index) + 1)
            },
            assistantTurns: (0..<turns).map { index in
                AssistantTurn(detectedQuestion: "Question \(index)", answer: "Answer \(index)", details: "")
            }
        )
        session.transcriptionUsage = usage
        return session
    }

    // MARK: - One row

    func testARowShowsTitleDateAndCounts() {
        let stored = session("Launch plan review", segments: 12, turns: 3)

        let row = HistoryRowContent(session: stored)

        XCTAssertEqual(row.id, stored.id)
        XCTAssertEqual(row.title, "Launch plan review")
        XCTAssertEqual(row.dateText, stored.startedAt.formatted(date: .abbreviated, time: .shortened))
        XCTAssertFalse(row.dateText.isEmpty)
        XCTAssertEqual(row.countsText, "12 transcript segments · 3 assists")
    }

    func testASessionWithoutUsageShowsNoUsageLabel() {
        let row = HistoryRowContent(session: session("No usage"))

        XCTAssertNil(row.usage, "Never a made-up zero.")
    }

    func testAMetaSessionShowsItsEstimatedCost() {
        let complete = HistoryRowContent(session: session("Meta", usage: usage(provider: "meta", seconds: 252)))
        XCTAssertEqual(complete.usage, .meta("Meta Muse · cloud · $0.01260 USD"))

        let nothingHeard = HistoryRowContent(session: session("Meta", usage: TranscriptionUsage(provider: "meta")))
        XCTAssertEqual(nothingHeard.usage, .meta("Meta Muse · cloud · $0.00000 USD"))
    }

    func testAnInterruptedStreamAppendsIncomplete() {
        let meta = HistoryRowContent(
            session: session("Meta", usage: usage(provider: "meta", seconds: 62, completed: false))
        )
        XCTAssertEqual(meta.usage, .meta("Meta Muse · cloud · $0.00310 USD · incomplete"))

        let local = HistoryRowContent(
            session: session("PC", usage: usage(provider: "nemotron", seconds: 62, completed: false))
        )
        XCTAssertEqual(local.usage, .local("Nemotron 3.5 · PC · Local · incomplete"))
    }

    func testEveryOtherProviderIsLocalWithoutACost() {
        let expected: [(provider: String, text: String)] = [
            ("nemotron", "Nemotron 3.5 · PC · Local"),
            ("qwen3", "Qwen3 1.7B · PC · Local"),
            ("voz", "Voz · iPhone · Local"),
            ("parakeet", "Parakeet · iPhone · Local"),
            ("a-provider-this-build-does-not-know", "On-device · Local")
        ]
        for (provider, text) in expected {
            let row = HistoryRowContent(session: session(provider, usage: usage(provider: provider, seconds: 120)))

            XCTAssertEqual(row.usage, .local(text))
            XCTAssertFalse(text.contains("$"), "No currency for \(provider).")
            XCTAssertFalse(text.contains("USD"))
        }

        // An on-device session records an empty usage object: still "Local".
        let empty = HistoryRowContent(session: session("Voz", usage: TranscriptionUsage(provider: "voz")))
        XCTAssertEqual(empty.usage, .local("Voz · iPhone · Local"))
    }

    // MARK: - The list

    @MainActor
    func testWithoutSessionsTheListIsEmpty() async {
        let owners = TestOwners()
        let model = HistoryViewModel(history: owners.history)

        await owners.history.reload()

        XCTAssertTrue(model.isEmpty)
        XCTAssertEqual(model.rows, [])
        XCTAssertNil(model.pendingDeleteID)
    }

    @MainActor
    func testRowsAreNewestFirst() async throws {
        let owners = TestOwners()
        let model = HistoryViewModel(history: owners.history)
        let oldest = session("Oldest", minutesAgo: 300)
        let newest = session("Newest", minutesAgo: 5, usage: usage(provider: "meta", seconds: 30))
        let middle = session("Middle", minutesAgo: 60, usage: usage(provider: "parakeet", seconds: 0))
        for stored in [oldest, newest, middle] {
            try await owners.sessions.save(stored, savedAt: start)
        }

        await owners.history.reload()

        XCTAssertFalse(model.isEmpty)
        XCTAssertEqual(model.rows.map(\.title), ["Newest", "Middle", "Oldest"])
        XCTAssertEqual(model.rows.map(\.id), [newest.id, middle.id, oldest.id])
        XCTAssertEqual(model.rows, [newest, middle, oldest].map { HistoryRowContent(session: $0) })
    }

    // MARK: - Delete (AC-IOS-083 as changed)

    @MainActor
    func testDeletingAsksFirstAndCancelKeepsTheRow() async throws {
        let owners = TestOwners()
        let model = HistoryViewModel(history: owners.history)
        let kept = session("Kept")
        try await owners.sessions.save(kept, savedAt: start)
        await owners.history.reload()

        model.requestDelete(kept.id)
        XCTAssertEqual(model.pendingDeleteID, kept.id)
        XCTAssertEqual(model.rows.map(\.id), [kept.id], "Nothing is deleted before the confirmation.")
        var stillStored = await owners.sessions.contains(id: kept.id)
        XCTAssertTrue(stillStored)

        model.cancelDelete()
        XCTAssertNil(model.pendingDeleteID)
        XCTAssertEqual(model.rows.map(\.id), [kept.id])
        stillStored = await owners.sessions.contains(id: kept.id)
        XCTAssertTrue(stillStored)
    }

    @MainActor
    func testConfirmingRemovesTheSessionForGood() async throws {
        let owners = TestOwners()
        let model = HistoryViewModel(history: owners.history)
        let kept = session("Kept", minutesAgo: 10)
        let deleted = session("Deleted", minutesAgo: 1)
        try await owners.sessions.save(kept, savedAt: start)
        try await owners.sessions.save(deleted, savedAt: start)
        await owners.history.reload()
        XCTAssertEqual(model.rows.map(\.title), ["Deleted", "Kept"])

        model.requestDelete(deleted.id)
        await model.confirmDelete()

        XCTAssertNil(model.pendingDeleteID)
        XCTAssertEqual(model.rows.map(\.title), ["Kept"])
        let stillStored = await owners.sessions.contains(id: deleted.id)
        XCTAssertFalse(stillStored)
        let keptStored = await owners.sessions.contains(id: kept.id)
        XCTAssertTrue(keptStored)
    }

    @MainActor
    func testConfirmingWithoutARequestDeletesNothing() async throws {
        let owners = TestOwners()
        let model = HistoryViewModel(history: owners.history)
        let kept = session("Kept")
        try await owners.sessions.save(kept, savedAt: start)
        await owners.history.reload()

        await model.confirmDelete()

        XCTAssertEqual(model.rows.map(\.id), [kept.id])
    }

    // MARK: - The screen

    /// The list with conversations, hosted in a window: its usage labels carry their identifiers, and a
    /// picture of it is kept with the test results.
    @MainActor
    func testTheListOfConversationsIsDrawn() async throws {
        let owners = TestOwners()
        let model = HistoryViewModel(history: owners.history)
        let stored = [
            session("Launch plan review and what to cut for March", minutesAgo: 30, segments: 12, turns: 3, usage: usage(provider: "meta", seconds: 252)),
            session("Onboarding flow feedback with design", minutesAgo: 1_020, segments: 31, turns: 5, usage: usage(provider: "nemotron", seconds: 600)),
            session("New conversation", minutesAgo: 1_360, segments: 4, turns: 0, usage: usage(provider: "meta", seconds: 62, completed: false)),
            session("Weekly planning", minutesAgo: 4_300, segments: 18, turns: 2, usage: usage(provider: "parakeet", seconds: 0)),
            session("A conversation without recorded usage", minutesAgo: 6_000, segments: 1, turns: 0)
        ]
        for one in stored {
            try await owners.sessions.save(one, savedAt: start)
        }
        await owners.history.reload()

        let report = ScreensBHost.show(
            NavigationStack {
                HistoryScreen()
            }
            .environment(model)
            .environment(owners.app),
            pictureNamed: "wp11-history-items",
            in: self
        )

        XCTAssertEqual(model.rows.count, 5)
        // Where the simulator builds the accessibility tree of a hosted view, the labels are in it.
        if report.labels.contains(where: { $0.contains("Launch plan review") }) {
            XCTAssertTrue(report.identifiers.contains("history-transcription-cost"), "Identifiers: \(report.identifiers)")
            XCTAssertTrue(report.identifiers.contains("history-local-transcription"), "Identifiers: \(report.identifiers)")
        }
    }
}
