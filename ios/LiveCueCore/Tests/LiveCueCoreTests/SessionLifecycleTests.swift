import Foundation
import LiveCueCore
import XCTest

/// The title rule and the recovery of a session cut off by an app kill.
final class SessionLifecycleTests: XCTestCase {
    // MARK: - Title rule (AC-IOS-076)

    func testTitleWithoutSegmentsIsConversation() {
        XCTAssertEqual(Session().finalTitle(), "Conversation")
        XCTAssertEqual(CoreFixtures.session(segments: []).finalTitle(), "Conversation")
    }

    func testTitleIsTheFirstSegmentWhenItIsShort() {
        let session = CoreFixtures.session(segments: ["What is the main advantage?", "A second segment."])
        XCTAssertEqual(session.finalTitle(), "What is the main advantage?")
    }

    func testTitleIsTheFirstFortyEightCharactersOfTheFirstSegment() {
        let text = "What is the main advantage of local transcription over the cloud?"
        XCTAssertEqual(text.count, 65)
        let session = CoreFixtures.session(segments: [text, "A second segment."])

        let title = session.finalTitle()
        XCTAssertEqual(title.count, 48)
        XCTAssertEqual(title, "What is the main advantage of local transcriptio")
        XCTAssertTrue(text.hasPrefix(title))

        let exact = String(repeating: "a", count: 48)
        XCTAssertEqual(CoreFixtures.session(segments: [exact]).finalTitle(), exact)
        XCTAssertEqual(CoreFixtures.session(segments: [exact + "b"]).finalTitle(), exact)
    }

    func testTitleCountsCharactersNotBytes() {
        // Each flag is two scalars and eight bytes of UTF-8, and one character.
        let flag = "\u{1F1E7}\u{1F1E9}"
        XCTAssertEqual(flag.count, 1)
        let session = CoreFixtures.session(segments: [String(repeating: flag, count: 60)])

        let title = session.finalTitle()
        XCTAssertEqual(title.count, 48)
        XCTAssertEqual(title, String(repeating: flag, count: 48))
        XCTAssertEqual(title.utf8.count, 48 * 8)
    }

    func testTitleDoesNotDependOnTheStoredTitleOrOnLaterSegments() {
        var session = CoreFixtures.session(segments: ["First.", "Second."])
        session.title = "Something else"
        XCTAssertEqual(session.finalTitle(), "First.")
        XCTAssertEqual(session.title, "Something else", "finalTitle() does not change the session.")
    }

    // MARK: - Recovery (IOS-B2)

    func testAnInterruptedSessionIsClosedAtItsLastSave() throws {
        var session = CoreFixtures.session(segments: ["What is the main advantage of local transcription over the cloud?", "Next."])
        XCTAssertEqual(session.title, Session.defaultTitle)
        XCTAssertNil(session.endedAt)

        var usage = TranscriptionUsage()
        let finished = UUID()
        let open = UUID()
        let failed = UUID()
        usage.begin(finished)
        usage.update(finished, sentMs: 5_000, processedMs: 5_000, hasTranscript: true)
        usage.finish(finished, completed: true)
        usage.begin(failed)
        usage.finish(failed, completed: false)
        usage.begin(open)
        usage.update(open, sentMs: 12_000, processedMs: 9_000, hasTranscript: true)
        session.transcriptionUsage = usage

        let lastSaved = session.startedAt.addingTimeInterval(42.5)
        let closed = session.closedAfterInterruption(at: lastSaved)

        XCTAssertEqual(closed.endedAt, lastSaved)
        XCTAssertEqual(closed.title, "What is the main advantage of local transcriptio")
        XCTAssertEqual(closed.title, session.finalTitle())

        let streams = try XCTUnwrap(closed.transcriptionUsage).streams
        XCTAssertEqual(streams.map(\.id), [finished, failed, open])
        XCTAssertEqual(
            streams.map(\.state),
            [SpeechStreamUsage.completed, SpeechStreamUsage.interrupted, SpeechStreamUsage.interrupted]
        )
        XCTAssertEqual(streams[2].sentMs, 12_000)
        XCTAssertEqual(streams[2].processedMs, 9_000)
        XCTAssertTrue(streams[2].hasTranscript)
        XCTAssertTrue(try XCTUnwrap(closed.transcriptionUsage).isIncomplete)
        XCTAssertEqual(try XCTUnwrap(closed.transcriptionUsage).estimatedSeconds, 5 + 0 + 9)

        // Nothing else moved.
        XCTAssertEqual(closed.id, session.id)
        XCTAssertEqual(closed.startedAt, session.startedAt)
        XCTAssertEqual(closed.segments, session.segments)
        XCTAssertEqual(closed.assistantTurns, session.assistantTurns)
        XCTAssertEqual(closed.memory, session.memory)
        XCTAssertEqual(closed.notes, session.notes)

        // The session passed in is a value: it is not changed.
        XCTAssertNil(session.endedAt)
        XCTAssertEqual(session.transcriptionUsage?.streams.last?.state, SpeechStreamUsage.streaming)
    }

    func testAnInterruptedSessionWithoutSegmentsOrUsageIsClosedToo() {
        let session = Session(startedAt: Date(timeIntervalSince1970: 1_790_000_000))
        let lastSaved = Date(timeIntervalSince1970: 1_790_000_003)

        let closed = session.closedAfterInterruption(at: lastSaved)
        XCTAssertEqual(closed.endedAt, lastSaved)
        XCTAssertEqual(closed.title, "Conversation")
        XCTAssertNil(closed.transcriptionUsage)
        XCTAssertEqual(closed.id, session.id)
    }

    func testASessionThatAlreadyEndedIsReturnedUnchanged() {
        var session = Session(
            title: "Kept title",
            startedAt: Date(timeIntervalSince1970: 1_790_000_000),
            endedAt: Date(timeIntervalSince1970: 1_790_000_100),
            segments: [TranscriptSegment(text: "First words.", startSeconds: 0, endSeconds: 1)]
        )
        var usage = TranscriptionUsage()
        let open = UUID()
        usage.begin(open)
        session.transcriptionUsage = usage

        let result = session.closedAfterInterruption(at: Date(timeIntervalSince1970: 1_790_000_999))
        XCTAssertEqual(result, session)
        XCTAssertEqual(result.endedAt, Date(timeIntervalSince1970: 1_790_000_100))
        XCTAssertEqual(result.title, "Kept title")
        XCTAssertEqual(result.transcriptionUsage?.streams.first?.state, SpeechStreamUsage.streaming)
    }

    func testClosingTwiceChangesNothingTheSecondTime() {
        let session = CoreFixtures.session(segments: ["Hello."])
        let once = session.closedAfterInterruption(at: Date(timeIntervalSince1970: 1_790_000_010))
        let twice = once.closedAfterInterruption(at: Date(timeIntervalSince1970: 1_790_000_999))
        XCTAssertEqual(twice, once)
    }

    func testTheClosedSessionRoundTripsThroughLiveCueJSON() throws {
        var session = CoreFixtures.session(segments: ["Hello."])
        var usage = TranscriptionUsage(provider: "nemotron")
        usage.begin(UUID())
        session.transcriptionUsage = usage

        // A save time between two milliseconds is stored as the nearest one.
        let closed = session.closedAfterInterruption(at: Date(timeIntervalSince1970: 1_790_000_010.123_456_7))
        XCTAssertEqual(closed.endedAt, Date(timeIntervalSince1970: 1_790_000_010.123))

        let reloaded = try LiveCueJSON.decode(Session.self, from: LiveCueJSON.encode(closed))
        XCTAssertEqual(reloaded, closed)
        XCTAssertEqual(reloaded.closedAfterInterruption(at: Date(timeIntervalSince1970: 1_790_000_999)), closed)
    }

    // MARK: - Benchmark transcript

    func testSpaceJoinedTranscript() {
        XCTAssertEqual(Session().spaceJoinedTranscript, "")
        XCTAssertEqual(CoreFixtures.session(segments: ["Only one."]).spaceJoinedTranscript, "Only one.")
        XCTAssertEqual(
            CoreFixtures.session(segments: ["First words.", "Second words.", "Third."]).spaceJoinedTranscript,
            "First words. Second words. Third."
        )
    }
}
