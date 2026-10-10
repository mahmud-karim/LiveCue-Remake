import Foundation
import LiveCueCore
import XCTest

/// The caption merge state machine: spec 02 section 6 and its acceptance criteria, with CORE-A7 (no stray
/// spaces) and CORE-A8 (a partial goes to the turn it names when that turn is open).
final class StreamTranscriptTests: XCTestCase {
    // MARK: - testCumulativePartialsAndOverlappingTurns (AC-CORE-014 to 019)

    func testCumulativePartialsAndOverlappingTurns() throws {
        var transcript = StreamTranscript()

        // Step 1. AC-CORE-014
        XCTAssertNil(transcript.apply(.speechStart(turn: 1, processedMs: 200)))

        // Steps 2 and 3: cumulative partials replace each other. AC-CORE-015
        XCTAssertNil(transcript.apply(.transcript(turn: nil, text: "First", processedMs: 500)))
        XCTAssertEqual(transcript.partialText, "First")
        XCTAssertNil(transcript.apply(.transcript(turn: nil, text: "First sentence", processedMs: 900)))
        XCTAssertEqual(transcript.partialText, "First sentence")

        // Step 4: `speechEnd` reaches the state machine as `.other`. AC-CORE-016
        XCTAssertNil(transcript.apply(.other))
        XCTAssertEqual(transcript.partialText, "First sentence")

        // Steps 5 and 6: a second turn starts before the first one completed.
        XCTAssertNil(transcript.apply(.speechStart(turn: 2, processedMs: 1_100)))
        XCTAssertNil(transcript.apply(.transcript(turn: nil, text: "Second", processedMs: 1_300)))
        XCTAssertEqual(transcript.partialText, "First sentence Second")

        // Step 7: the out-of-order final keeps its own start; the later partial stays. AC-CORE-017
        let first = try XCTUnwrap(transcript.apply(.speechComplete(turn: 1, text: "First sentence.", processedMs: 1_000)))
        XCTAssertEqual(first.text, "First sentence.")
        XCTAssertEqual(first.startSeconds, 0.2)
        XCTAssertEqual(first.endSeconds, 1.0)
        XCTAssertEqual(transcript.partialText, "Second")

        // Step 8: a second completion of the same turn is ignored. AC-CORE-018
        XCTAssertNil(transcript.apply(.speechComplete(turn: 1, text: "Duplicate.", processedMs: 1_000)))
        XCTAssertEqual(transcript.partialText, "Second")

        // Step 9. AC-CORE-019
        let second = try XCTUnwrap(transcript.apply(.speechComplete(turn: 2, text: "Second.", processedMs: 2_000)))
        XCTAssertEqual(second.text, "Second.")
        XCTAssertEqual(second.startSeconds, 1.1)
        XCTAssertEqual(second.endSeconds, 2.0)
        XCTAssertEqual(transcript.partialText, "")
        XCTAssertNotEqual(first.id, second.id)
    }

    // MARK: - testEmptyFinalDoesNotCreateSegment (AC-CORE-020)

    func testEmptyFinalDoesNotCreateSegment() throws {
        var transcript = StreamTranscript()
        XCTAssertNil(transcript.apply(.speechStart(turn: 1, processedMs: 0)))
        XCTAssertNil(transcript.apply(.speechComplete(turn: 1, text: "  ", processedMs: 100)))

        // The turn still counts as completed: a later final for it is ignored, and so is a partial.
        XCTAssertNil(transcript.apply(.speechComplete(turn: 1, text: "Late words.", processedMs: 200)))
        XCTAssertNil(transcript.apply(.transcript(turn: 1, text: "Late", processedMs: 300)))
        XCTAssertEqual(transcript.partialText, "")
    }

    func testAnAbsentOrWhitespaceFinalCreatesNoSegment() {
        for text in [nil, "", " \n\t ", "\r\n"] as [String?] {
            var transcript = StreamTranscript()
            _ = transcript.apply(.speechStart(turn: 4, processedMs: 0))
            _ = transcript.apply(.transcript(turn: 4, text: "Partial", processedMs: 10))
            XCTAssertNil(transcript.apply(.speechComplete(turn: 4, text: text, processedMs: 100)))
            XCTAssertEqual(transcript.partialText, "", "The partial of a completed turn is removed.")
        }
    }

    // MARK: - Sanitised Meta events (AC-CORE-031, 032)

    func testSanitisedMetaEventsFeedTheStateMachine() throws {
        let key = "synthetic-meta-key"
        let partial = try MetaRealtimeAPI.event(
            """
            {"type":"transcript","turnId":2,"audioProcessedMs":1234,"final":false,"transcript":"Hello synthetic-meta-key","authorization":"synthetic-meta-key","metadata":"synthetic-meta-key"}
            """,
            key: key
        )
        let sanitisedText = try XCTUnwrap(partial.transcript)

        // AC-CORE-031
        var transcript = StreamTranscript()
        XCTAssertNil(transcript.apply(.speechStart(turn: 2, processedMs: 0)))
        XCTAssertNil(transcript.apply(.transcript(turn: 2, text: sanitisedText, processedMs: 1_234)))
        XCTAssertEqual(transcript.partialText, "Hello [redacted]")

        // AC-CORE-032
        let final = try MetaRealtimeAPI.event(
            """
            {"type":"speechComplete","turnId":2,"audioProcessedMs":2000,"transcript":"Hello world."}
            """,
            key: key
        )
        let segment = try XCTUnwrap(transcript.apply(.speechComplete(turn: final.turnId, text: final.transcript, processedMs: 2_000)))
        XCTAssertEqual(segment.text, "Hello world.")
        XCTAssertEqual(transcript.partialText, "")
    }

    func testAMetaEventMapsItselfToTheStateMachineInput() throws {
        let key = "synthetic-meta-key"
        var transcript = StreamTranscript()

        let start = try MetaRealtimeAPI.event(#"{"type":"speechStart","turnId":7,"audioProcessedMs":500}"#, key: key)
        XCTAssertEqual(start.transcriptEvent, .speechStart(turn: 7, processedMs: 500))
        XCTAssertNil(transcript.apply(start.transcriptEvent))

        let partial = try MetaRealtimeAPI.event(#"{"type":"transcript","transcript":"Words"}"#, key: key)
        XCTAssertEqual(partial.transcriptEvent, .transcript(turn: nil, text: "Words", processedMs: 0))
        XCTAssertNil(transcript.apply(partial.transcriptEvent))
        XCTAssertEqual(transcript.partialText, "Words")

        for other in [#"{"type":"speechEnd","turnId":7}"#, #"{"type":"audioProgress","audioProcessedMs":900}"#] {
            let event = try MetaRealtimeAPI.event(other, key: key)
            XCTAssertEqual(event.transcriptEvent, .other)
            XCTAssertNil(transcript.apply(event.transcriptEvent))
            XCTAssertEqual(transcript.partialText, "Words")
        }

        let final = try MetaRealtimeAPI.event(#"{"type":"speechComplete","turnId":7,"audioProcessedMs":1500,"transcript":"Words."}"#, key: key)
        let segment = try XCTUnwrap(transcript.apply(final.transcriptEvent))
        XCTAssertEqual(segment.text, "Words.")
        XCTAssertEqual(segment.startSeconds, 0.5)
        XCTAssertEqual(segment.endSeconds, 1.5)
    }

    // MARK: - testRollingCaptionsDoNotDuplicate (AC-CORE-085 to 089)

    func testRollingCaptionsDoNotDuplicate() throws {
        var transcript = StreamTranscript()

        // AC-CORE-085, 086
        XCTAssertNil(transcript.apply(.speechStart(turn: 1, processedMs: 0)))
        XCTAssertNil(transcript.apply(.transcript(turn: 1, text: "First words", processedMs: 1_000)))
        XCTAssertEqual(transcript.partialText, "First words")

        // AC-CORE-087
        let first = try XCTUnwrap(transcript.apply(.speechComplete(turn: 1, text: "First words.", processedMs: 30_000)))
        XCTAssertEqual(first.text, "First words.")
        XCTAssertEqual(first.startSeconds, 0)
        XCTAssertEqual(first.endSeconds, 30)
        XCTAssertEqual(transcript.partialText, "")

        // AC-CORE-085
        XCTAssertNil(transcript.apply(.speechStart(turn: 2, processedMs: 30_000)))
        XCTAssertNil(transcript.apply(.transcript(turn: 2, text: "Next words", processedMs: 31_000)))
        XCTAssertEqual(transcript.partialText, "Next words")

        // AC-CORE-088, 089
        let second = try XCTUnwrap(transcript.apply(.speechComplete(turn: 2, text: "Next words.", processedMs: 32_000)))
        XCTAssertEqual(second.text, "Next words.")
        XCTAssertEqual(second.startSeconds, 30)
        XCTAssertEqual(second.endSeconds, 32)
        XCTAssertEqual(transcript.partialText, "")
    }

    // MARK: - AC-CORE-S16 as changed by CORE-A8

    func testATranscriptBeforeAnySpeechStartIsIgnored() {
        var transcript = StreamTranscript()
        XCTAssertNil(transcript.apply(.transcript(turn: nil, text: "Too early", processedMs: 100)))
        XCTAssertEqual(transcript.partialText, "")

        // Naming a turn does not open it: only `speechStart` does.
        XCTAssertNil(transcript.apply(.transcript(turn: 1, text: "Too early", processedMs: 100)))
        XCTAssertEqual(transcript.partialText, "")

        // Once the turn starts, nothing of the early text has stuck.
        XCTAssertNil(transcript.apply(.speechStart(turn: 1, processedMs: 200)))
        XCTAssertEqual(transcript.partialText, "")
    }

    func testASpeechCompleteWithNoTurnIsIgnored() {
        var transcript = StreamTranscript()
        _ = transcript.apply(.speechStart(turn: 1, processedMs: 0))
        _ = transcript.apply(.transcript(turn: 1, text: "Words", processedMs: 100))

        XCTAssertNil(transcript.apply(.speechComplete(turn: nil, text: "Words.", processedMs: 200)))
        XCTAssertEqual(transcript.partialText, "Words")
    }

    func testASpeechStartWithNoTurnIsIgnored() {
        var transcript = StreamTranscript()
        XCTAssertNil(transcript.apply(.speechStart(turn: nil, processedMs: 0)))
        XCTAssertNil(transcript.apply(.transcript(turn: nil, text: "Words", processedMs: 100)))
        XCTAssertEqual(transcript.partialText, "", "No turn became active.")
    }

    func testASpeechCompleteForATurnThatNeverStartedStartsAtZero() throws {
        var transcript = StreamTranscript()
        let segment = try XCTUnwrap(transcript.apply(.speechComplete(turn: 9, text: "Only a final.", processedMs: 4_500)))
        XCTAssertEqual(segment.text, "Only a final.")
        XCTAssertEqual(segment.startSeconds, 0)
        XCTAssertEqual(segment.endSeconds, 4.5)

        // And it is completed: it cannot be finalised twice.
        XCTAssertNil(transcript.apply(.speechComplete(turn: 9, text: "Again.", processedMs: 5_000)))
    }

    // MARK: - CORE-A7: no stray spaces

    func testEmptyPartialsAreLeftOutOfTheJoin() {
        var transcript = StreamTranscript()
        for turn in 1...3 {
            _ = transcript.apply(.speechStart(turn: turn, processedMs: Double(turn) * 100))
        }
        _ = transcript.apply(.transcript(turn: 1, text: "One", processedMs: 400))
        _ = transcript.apply(.transcript(turn: 2, text: "", processedMs: 400))
        _ = transcript.apply(.transcript(turn: 3, text: "Three", processedMs: 400))
        XCTAssertEqual(transcript.partialText, "One Three")

        // A partial without text counts as the empty string, and an empty partial replaces a full one.
        _ = transcript.apply(.transcript(turn: 1, text: nil, processedMs: 500))
        XCTAssertEqual(transcript.partialText, "Three")
        _ = transcript.apply(.transcript(turn: 3, text: "", processedMs: 600))
        XCTAssertEqual(transcript.partialText, "")
    }

    func testPartialsAreJoinedInAscendingTurnOrder() {
        var transcript = StreamTranscript()
        _ = transcript.apply(.speechStart(turn: 10, processedMs: 0))
        _ = transcript.apply(.transcript(turn: 10, text: "ten", processedMs: 100))
        _ = transcript.apply(.speechStart(turn: 2, processedMs: 200))
        _ = transcript.apply(.transcript(turn: 2, text: "two", processedMs: 300))
        _ = transcript.apply(.speechStart(turn: 30, processedMs: 400))
        _ = transcript.apply(.transcript(turn: 30, text: "thirty", processedMs: 500))

        XCTAssertEqual(transcript.partialText, "two ten thirty")
    }

    // MARK: - CORE-A8: a partial goes to the turn it names when that turn is open

    func testAPartialGoesToTheTurnItNamesWhenThatTurnIsOpen() throws {
        var transcript = StreamTranscript()
        _ = transcript.apply(.speechStart(turn: 1, processedMs: 0))
        _ = transcript.apply(.speechStart(turn: 2, processedMs: 1_000))

        // Turn 2 is the active one, but the event names turn 1, which is still open.
        _ = transcript.apply(.transcript(turn: 1, text: "First", processedMs: 1_100))
        XCTAssertEqual(transcript.partialText, "First")
        _ = transcript.apply(.transcript(turn: 2, text: "Second", processedMs: 1_200))
        XCTAssertEqual(transcript.partialText, "First Second")

        // Without a turn the active turn takes it, as before.
        _ = transcript.apply(.transcript(turn: nil, text: "Second, longer", processedMs: 1_300))
        XCTAssertEqual(transcript.partialText, "First Second, longer")

        // A turn that never started is not open: the active turn takes the text.
        _ = transcript.apply(.transcript(turn: 7, text: "Second from seven", processedMs: 1_400))
        XCTAssertEqual(transcript.partialText, "First Second from seven")
    }

    func testAPartialForACompletedTurnFallsBackToTheActiveTurnOrIsDropped() throws {
        var transcript = StreamTranscript()
        _ = transcript.apply(.speechStart(turn: 1, processedMs: 0))
        _ = transcript.apply(.speechStart(turn: 2, processedMs: 1_000))
        _ = transcript.apply(.transcript(turn: 1, text: "First", processedMs: 1_100))
        XCTAssertNotNil(transcript.apply(.speechComplete(turn: 1, text: "First.", processedMs: 1_200)))

        // Turn 1 is completed, so the active turn 2 takes a partial that names it.
        _ = transcript.apply(.transcript(turn: 1, text: "Second", processedMs: 1_300))
        XCTAssertEqual(transcript.partialText, "Second")

        // The active turn completes: now nothing is open and every partial is dropped.
        XCTAssertNotNil(transcript.apply(.speechComplete(turn: 2, text: "Second.", processedMs: 1_400)))
        _ = transcript.apply(.transcript(turn: nil, text: "Late", processedMs: 1_500))
        _ = transcript.apply(.transcript(turn: 2, text: "Late", processedMs: 1_500))
        _ = transcript.apply(.transcript(turn: 1, text: "Late", processedMs: 1_500))
        XCTAssertEqual(transcript.partialText, "")
    }

    func testAnOlderOpenTurnStillTakesItsPartialAfterTheActiveTurnCompleted() {
        var transcript = StreamTranscript()
        _ = transcript.apply(.speechStart(turn: 1, processedMs: 0))
        _ = transcript.apply(.speechStart(turn: 2, processedMs: 1_000))
        XCTAssertNotNil(transcript.apply(.speechComplete(turn: 2, text: "Second.", processedMs: 1_100)))

        // The active turn (2) is completed. A partial without a turn is dropped; one naming turn 1 lands.
        _ = transcript.apply(.transcript(turn: nil, text: "Nowhere", processedMs: 1_200))
        XCTAssertEqual(transcript.partialText, "")
        _ = transcript.apply(.transcript(turn: 1, text: "First", processedMs: 1_300))
        XCTAssertEqual(transcript.partialText, "First")
    }

    func testASpeechStartNeverReopensACompletedTurn() throws {
        var transcript = StreamTranscript()
        _ = transcript.apply(.speechStart(turn: 1, processedMs: 0))
        XCTAssertNotNil(transcript.apply(.speechComplete(turn: 1, text: "Done.", processedMs: 900)))

        // The provider starts the same turn again: it stays completed, so it takes no partial and no
        // second final.
        XCTAssertNil(transcript.apply(.speechStart(turn: 1, processedMs: 1_000)))
        _ = transcript.apply(.transcript(turn: 1, text: "Again", processedMs: 1_100))
        _ = transcript.apply(.transcript(turn: nil, text: "Again", processedMs: 1_100))
        XCTAssertEqual(transcript.partialText, "")
        XCTAssertNil(transcript.apply(.speechComplete(turn: 1, text: "Again.", processedMs: 1_200)))
    }

    func testASecondSpeechStartOfAnOpenTurnRecordsItsStartAgain() throws {
        var transcript = StreamTranscript()
        _ = transcript.apply(.speechStart(turn: 1, processedMs: 500))
        _ = transcript.apply(.transcript(turn: 1, text: "Words", processedMs: 700))
        _ = transcript.apply(.speechStart(turn: 1, processedMs: 2_000))

        // The partial stays; the segment starts where the turn was last started.
        XCTAssertEqual(transcript.partialText, "Words")
        let segment = try XCTUnwrap(transcript.apply(.speechComplete(turn: 1, text: "Words.", processedMs: 3_000)))
        XCTAssertEqual(segment.startSeconds, 2)
        XCTAssertEqual(segment.endSeconds, 3)
    }

    // MARK: - Final text and times

    func testTheFinalTextIsTrimmedAndTakenVerbatim() throws {
        var transcript = StreamTranscript()
        _ = transcript.apply(.speechStart(turn: 3, processedMs: 1_500))
        _ = transcript.apply(.transcript(turn: 3, text: "hello there", processedMs: 2_000))

        // The final replaces the partial entirely; it is not reconciled with it.
        let segment = try XCTUnwrap(transcript.apply(.speechComplete(turn: 3, text: "  Goodbye,  now.\n", processedMs: 4_250)))
        XCTAssertEqual(segment.text, "Goodbye,  now.")
        XCTAssertEqual(segment.startSeconds, 1.5)
        XCTAssertEqual(segment.endSeconds, 4.25)
    }

    func testOtherEventsChangeNothing() {
        var transcript = StreamTranscript()
        XCTAssertNil(transcript.apply(.other))
        XCTAssertEqual(transcript.partialText, "")

        _ = transcript.apply(.speechStart(turn: 1, processedMs: 0))
        _ = transcript.apply(.transcript(turn: 1, text: "Words", processedMs: 100))
        XCTAssertNil(transcript.apply(.other))
        XCTAssertEqual(transcript.partialText, "Words")
    }

    func testAFreshValueStartsEmpty() {
        var first = StreamTranscript()
        _ = first.apply(.speechStart(turn: 1, processedMs: 0))
        _ = first.apply(.transcript(turn: 1, text: "Words", processedMs: 100))

        // A new stream gets a new value: nothing carries over.
        var second = StreamTranscript()
        XCTAssertEqual(second.partialText, "")
        XCTAssertNil(second.apply(.transcript(turn: 1, text: "Words", processedMs: 100)))
        XCTAssertEqual(second.partialText, "")
    }
}
