import Foundation
import LiveCueCore
import XCTest

/// Assist and summary requests, and the same-text retry.
final class ContextBuilderTests: XCTestCase {
    private let sixSegments = ["segment 0", "segment 1", "segment 2", "segment 3", "segment 4", "segment 5"]

    /// A session with six segments whose memory marker is the segment at `marker`.
    private func sixSegmentSession(markerAt marker: Int?) -> Session {
        var session = CoreFixtures.session(segments: sixSegments)
        session.memory = SessionMemory(
            summary: "Earlier context",
            throughSegmentId: marker.map { session.segments[$0].id }
        )
        return session
    }

    // MARK: - The window (spec 02 section 3.2)

    /// AC-CORE-009 and AC-CORE-010.
    func testContextIncludesOverlapAndNewSegments() {
        let session = sixSegmentSession(markerAt:3)
        let request = ContextBuilder.assistRequest(session: session, partial: "latest words", instruction: "Answer briefly")

        XCTAssertTrue(request.transcript.contains("segment 2"))
        XCTAssertTrue(request.transcript.contains("segment 5"))
        XCTAssertEqual(request.transcript, "segment 2\nsegment 3\nsegment 4\nsegment 5")
        XCTAssertEqual(request.instruction, "Answer briefly")
        XCTAssertEqual(request.partialTranscript, "latest words")
        XCTAssertEqual(request.sessionId, session.id)
        XCTAssertEqual(request.memory, session.memory)
        XCTAssertNil(request.assistant)
    }

    /// AC-CORE-S01.
    func testContextWithoutAMarkerIncludesEverySegment() {
        let all = sixSegments.joined(separator: "\n")
        XCTAssertEqual(ContextBuilder.assistRequest(session: sixSegmentSession(markerAt:nil), partial: nil, instruction: nil).transcript, all)

        var unknown = sixSegmentSession(markerAt:nil)
        unknown.memory.throughSegmentId = UUID()
        let request = ContextBuilder.assistRequest(session: unknown, partial: nil, instruction: nil)
        XCTAssertEqual(request.transcript, all)
        XCTAssertEqual(request.memory.throughSegmentId, unknown.memory.throughSegmentId, "The marker is passed through as it is.")
    }

    /// AC-CORE-S02.
    func testContextWithTheMarkerOnTheLastSegmentIsTheLastTwoSegments() {
        let request = ContextBuilder.assistRequest(session: sixSegmentSession(markerAt:5), partial: nil, instruction: nil)
        XCTAssertEqual(request.transcript, "segment 4\nsegment 5")
    }

    func testTheWindowStartsTwoBeforeTheFirstUncoveredSegment() {
        let expected: [(marker: Int, first: Int)] = [(0, 0), (1, 0), (2, 1), (3, 2), (4, 3), (5, 4)]
        for row in expected {
            let request = ContextBuilder.assistRequest(session: sixSegmentSession(markerAt:row.marker), partial: nil, instruction: nil)
            XCTAssertEqual(request.transcript, sixSegments[row.first...].joined(separator: "\n"), "marker \(row.marker)")
        }
    }

    func testOneSegmentAndNoSegments() {
        var one = CoreFixtures.session(segments: ["only"])
        XCTAssertEqual(ContextBuilder.assistRequest(session: one, partial: nil, instruction: nil).transcript, "only")
        one.memory.throughSegmentId = one.segments[0].id
        XCTAssertEqual(ContextBuilder.assistRequest(session: one, partial: nil, instruction: nil).transcript, "only")

        let none = CoreFixtures.session(segments: [])
        let request = ContextBuilder.assistRequest(session: none, partial: nil, instruction: nil)
        XCTAssertEqual(request.transcript, "")
        XCTAssertNil(request.partialTranscript)
        XCTAssertNil(request.instruction)
    }

    func testSegmentsAreJoinedByOneLineFeedWithNoneAtTheEnd() {
        let session = CoreFixtures.session(segments: ["one", "two", "three"])
        let request = ContextBuilder.assistRequest(session: session, partial: "four", instruction: nil)
        XCTAssertEqual(request.transcript, "one\ntwo\nthree")
        XCTAssertEqual(request.partialTranscript, "four", "The partial is never merged into the transcript.")
    }

    /// AC-CORE-S04.
    func testALongTranscriptKeepsItsEnd() {
        let old = String(repeating: "a", count: 50_000)
        let recent = String(repeating: "b", count: 50_000)
        let session = CoreFixtures.session(segments: [old, recent])
        let request = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil)

        XCTAssertEqual(request.transcript.utf8.count, RelayConstants.assistTranscriptBytes)
        XCTAssertEqual(request.transcript, String(repeating: "a", count: 29_999) + "\n" + recent)
        XCTAssertTrue((old + "\n" + recent).hasSuffix(request.transcript))
    }

    func testEachRequestHasAFreshIdAndNoAssistant() {
        let session = sixSegmentSession(markerAt:3)
        let first = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil)
        let second = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil)
        XCTAssertNotEqual(first.requestId, second.requestId)
        XCTAssertNil(first.assistant)
        XCTAssertNil(second.assistant)
        XCTAssertEqual(first.transcript, second.transcript)
    }

    // MARK: - Instruction and partial

    /// AC-CORE-S05.
    func testInstructionAndPartialAreTrimmedAndOmittedWhenEmpty() {
        let session = sixSegmentSession(markerAt:nil)

        let trimmed = ContextBuilder.assistRequest(
            session: session,
            partial: "\n  latest words \t",
            instruction: "  Answer briefly \n\n"
        )
        XCTAssertEqual(trimmed.partialTranscript, "latest words")
        XCTAssertEqual(trimmed.instruction, "Answer briefly")

        for blank in ["", " ", "\n", " \t \r\n "] {
            let request = ContextBuilder.assistRequest(session: session, partial: blank, instruction: blank)
            XCTAssertNil(request.partialTranscript, blank.debugDescription)
            XCTAssertNil(request.instruction, blank.debugDescription)
        }

        let inner = ContextBuilder.assistRequest(session: session, partial: "two  words", instruction: "line one\nline two")
        XCTAssertEqual(inner.partialTranscript, "two  words", "Only the ends are trimmed.")
        XCTAssertEqual(inner.instruction, "line one\nline two")
    }

    func testThePartialKeepsItsLastSixteenThousandBytes() {
        let session = sixSegmentSession(markerAt:nil)
        let partial = String(repeating: "x", count: 20_000) + "END"
        let request = ContextBuilder.assistRequest(session: session, partial: partial, instruction: nil)
        XCTAssertEqual(request.partialTranscript?.utf8.count, RelayConstants.partialTranscriptBytes)
        XCTAssertEqual(request.partialTranscript, String(repeating: "x", count: 15_997) + "END")

        // Three bytes each: 5,333 whole characters fit in 16,000 bytes.
        let euros = String(repeating: "\u{20AC}", count: 6_000)
        let cut = ContextBuilder.assistRequest(session: session, partial: euros, instruction: nil)
        XCTAssertEqual(cut.partialTranscript, String(repeating: "\u{20AC}", count: 5_333))
    }

    func testTheInstructionKeepsItsFirstFourThousandCharactersThenEightThousandBytes() {
        let session = sixSegmentSession(markerAt:nil)

        let ascii = "START" + String(repeating: "i", count: 5_000)
        let byCharacters = ContextBuilder.assistRequest(session: session, partial: nil, instruction: ascii)
        XCTAssertEqual(byCharacters.instruction?.count, RelayConstants.instructionCharacters)
        XCTAssertEqual(byCharacters.instruction, "START" + String(repeating: "i", count: 3_995))

        // 4,000 characters of three bytes are 12,000 bytes: 2,666 whole characters fit in 8,000 bytes.
        let euros = String(repeating: "\u{20AC}", count: 5_000)
        let byBytes = ContextBuilder.assistRequest(session: session, partial: nil, instruction: euros)
        XCTAssertEqual(byBytes.instruction, String(repeating: "\u{20AC}", count: 2_666))
        XCTAssertEqual(byBytes.instruction?.utf8.count, 7_998)

        // 4,000 characters of two bytes are exactly 8,000 bytes.
        let accents = String(repeating: "\u{E9}", count: 4_500)
        let both = ContextBuilder.assistRequest(session: session, partial: nil, instruction: accents)
        XCTAssertEqual(both.instruction, String(repeating: "\u{E9}", count: 4_000))
        XCTAssertEqual(both.instruction?.utf8.count, RelayConstants.instructionBytes)

        let short = ContextBuilder.assistRequest(session: session, partial: nil, instruction: "Answer briefly")
        XCTAssertEqual(short.instruction, "Answer briefly")
    }

    // MARK: - Memory

    func testMemoryIsCutToItsBudgetsAndTheMarkerIsPassedThrough() {
        var session = sixSegmentSession(markerAt:3)
        let marker = session.memory.throughSegmentId
        let facts = (0..<20).map { "fact \($0) " + String(repeating: "\u{20AC}", count: 200) }
        let questions = (0..<40).map { "question \($0)" }
        session.memory = SessionMemory(
            summary: "OLD" + String(repeating: "s", count: 30_000) + "NEW",
            facts: facts,
            openQuestions: questions,
            throughSegmentId: marker
        )

        let memory = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil).memory
        XCTAssertEqual(memory.summary.utf8.count, RelayConstants.memorySummaryBytes)
        XCTAssertEqual(memory.summary, String(repeating: "s", count: 23_997) + "NEW")
        XCTAssertEqual(memory.throughSegmentId, marker)

        XCTAssertEqual(memory.facts.count, RelayConstants.memoryListItems)
        XCTAssertEqual(memory.openQuestions, Array(questions[24...]), "The last sixteen items are kept, in order.")
        for (index, fact) in memory.facts.enumerated() {
            let original = facts[index + 4]
            XCTAssertTrue(original.hasPrefix(fact), "item \(index) keeps its head")
            XCTAssertTrue(fact.hasPrefix("fact \(index + 4) "))
            XCTAssertLessThanOrEqual(fact.utf8.count, RelayConstants.memoryListItemBytes)
            // The next whole character would not have fitted.
            XCTAssertGreaterThan(fact.utf8.count + 3, RelayConstants.memoryListItemBytes)
        }
        // "fact 4 " is 7 bytes, so 83 three-byte characters fit: 256 bytes exactly.
        XCTAssertEqual(memory.facts[0], "fact 4 " + String(repeating: "\u{20AC}", count: 83))
        // "fact 10 " is 8 bytes, so 82 fit: 254 bytes.
        XCTAssertEqual(memory.facts[6], "fact 10 " + String(repeating: "\u{20AC}", count: 82))
    }

    func testMemoryInsideItsBudgetsIsPassedThroughUnchanged() {
        var session = sixSegmentSession(markerAt:2)
        session.memory.facts = ["A fact", "Another"]
        session.memory.openQuestions = ["A question"]
        let request = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil)
        XCTAssertEqual(request.memory, session.memory)

        let summary = ContextBuilder.summaryRequest(session: session, assistant: AssistantConfiguration())
        XCTAssertEqual(summary?.memory, session.memory)
    }

    // MARK: - Nothing to send

    func testHasContent() {
        let empty = CoreFixtures.session(segments: [])
        XCTAssertFalse(ContextBuilder.hasContent(ContextBuilder.assistRequest(session: empty, partial: nil, instruction: nil)))
        XCTAssertFalse(ContextBuilder.hasContent(ContextBuilder.assistRequest(session: empty, partial: "  \n", instruction: "Answer briefly")))
        XCTAssertTrue(ContextBuilder.hasContent(ContextBuilder.assistRequest(session: empty, partial: "words", instruction: nil)))

        let spoken = CoreFixtures.session(segments: ["Hello."])
        XCTAssertTrue(ContextBuilder.hasContent(ContextBuilder.assistRequest(session: spoken, partial: nil, instruction: nil)))
        XCTAssertTrue(ContextBuilder.hasContent(ContextBuilder.assistRequest(session: spoken, partial: "words", instruction: nil)))

        XCTAssertFalse(ContextBuilder.hasContent(AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: "")))
        XCTAssertFalse(
            ContextBuilder.hasContent(AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: "", partialTranscript: ""))
        )
        XCTAssertTrue(
            ContextBuilder.hasContent(AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: "", partialTranscript: "w"))
        )
        XCTAssertTrue(ContextBuilder.hasContent(AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: "t")))
    }

    // MARK: - Summary (P2-208)

    func testASummaryRequestHasARequestIdAndEverySegment() throws {
        // The marker does not window a summary.
        let session = sixSegmentSession(markerAt:3)
        let assistant = AssistantConfiguration(model: "gpt-5.6-luna", reasoningEffort: "high")

        let request = try XCTUnwrap(ContextBuilder.summaryRequest(session: session, assistant: assistant))
        XCTAssertEqual(request.transcript, sixSegments.joined(separator: "\n"))
        XCTAssertEqual(request.sessionId, session.id)
        XCTAssertEqual(request.assistant, assistant)
        XCTAssertEqual(request.memory, session.memory)
        XCTAssertNil(request.transcriptTruncated)

        let again = try XCTUnwrap(ContextBuilder.summaryRequest(session: session, assistant: assistant))
        XCTAssertNotEqual(again.requestId, request.requestId, "Each request has a fresh id.")

        let body = try XCTUnwrap(JSONValue.parse(WireBudget.encodedBody(request)))
        XCTAssertEqual(body["requestId"], .string(request.requestId.uuidString))
        XCTAssertEqual(body["sessionId"], .string(session.id.uuidString))
        XCTAssertEqual(body["assistant"]?["model"], .string("gpt-5.6-luna"))
        XCTAssertEqual(body["assistant"]?["reasoningEffort"], .string("high"))
        XCTAssertEqual(body["assistant"]?["provider"], .string("codex"))
        XCTAssertNil(body["transcriptTruncated"], "Absent, never false, when nothing was cut.")
        XCTAssertNil(body["instruction"])
        XCTAssertNil(body["partialTranscript"])
    }

    func testTranscriptTruncatedIsTrueExactlyWhenTheTranscriptWasCut() throws {
        let assistant = AssistantConfiguration()

        // 100,000 bytes exactly: 99,999 and a line feed between two segments.
        let exact = CoreFixtures.session(segments: [String(repeating: "a", count: 60_000), String(repeating: "b", count: 39_999)])
        let whole = try XCTUnwrap(ContextBuilder.summaryRequest(session: exact, assistant: assistant))
        XCTAssertEqual(whole.transcript.utf8.count, RelayConstants.summaryTranscriptBytes)
        XCTAssertNil(whole.transcriptTruncated)

        // One byte more.
        let over = CoreFixtures.session(segments: [String(repeating: "a", count: 60_000), String(repeating: "b", count: 40_000)])
        let cut = try XCTUnwrap(ContextBuilder.summaryRequest(session: over, assistant: assistant))
        XCTAssertEqual(cut.transcriptTruncated, true)
        XCTAssertEqual(cut.transcript.utf8.count, RelayConstants.summaryTranscriptBytes)
        XCTAssertEqual(cut.transcript, String(repeating: "a", count: 59_999) + "\n" + String(repeating: "b", count: 40_000))

        let body = try XCTUnwrap(JSONValue.parse(WireBudget.encodedBody(cut)))
        XCTAssertEqual(body["transcriptTruncated"], .bool(true))
    }

    func testNoSummaryRequestForASessionWithoutSegments() {
        XCTAssertNil(ContextBuilder.summaryRequest(session: Session(), assistant: AssistantConfiguration()))

        var remembered = CoreFixtures.session(segments: [])
        remembered.memory = SessionMemory(summary: "Earlier context", facts: ["A fact"])
        XCTAssertNil(ContextBuilder.summaryRequest(session: remembered, assistant: AssistantConfiguration()))

        XCTAssertNotNil(ContextBuilder.summaryRequest(session: CoreFixtures.session(segments: ["one"]), assistant: AssistantConfiguration()))
    }

    // MARK: - Same-text retry

    func testRetryKeepsTheTextAndChangesOnlyTheIdAndTheAssistant() {
        var previous = ContextBuilder.assistRequest(session: sixSegmentSession(markerAt:3), partial: "latest words", instruction: "Answer briefly")
        previous.assistant = AssistantConfiguration()
        let selected = AssistantConfiguration(model: "gpt-5.6-luna", reasoningEffort: "high")

        let retry = ContextBuilder.retry(previous, assistant: selected)
        XCTAssertNotEqual(retry.requestId, previous.requestId)
        XCTAssertEqual(retry.assistant, selected)
        XCTAssertEqual(retry.sessionId, previous.sessionId)
        XCTAssertEqual(retry.transcript, previous.transcript)
        XCTAssertEqual(retry.partialTranscript, previous.partialTranscript)
        XCTAssertEqual(retry.instruction, previous.instruction)
        XCTAssertEqual(retry.memory, previous.memory)

        // Putting the two changed members back gives the previous request exactly.
        var restored = retry
        restored.requestId = previous.requestId
        restored.assistant = previous.assistant
        XCTAssertEqual(restored, previous)

        let secondRetry = ContextBuilder.retry(retry, assistant: selected)
        XCTAssertNotEqual(secondRetry.requestId, retry.requestId)
    }
}
