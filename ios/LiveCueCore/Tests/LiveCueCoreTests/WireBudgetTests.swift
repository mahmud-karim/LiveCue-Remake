import Foundation
import LiveCueCore
import XCTest

/// Byte budgets and the fitting step of protocol 13.4.
final class WireBudgetTests: XCTestCase {
    /// C-11.
    private let limit = RelayConstants.maximumEncodedBodyBytes
    private let megabyte = 1_048_576

    // One character each, of more than one scalar.
    private let accented = "e\u{301}"                                                           // 3 bytes
    private let family = "\u{1F468}\u{200D}\u{1F469}\u{200D}\u{1F467}\u{200D}\u{1F466}"         // 25 bytes
    private let flagOne = "\u{1F1E7}\u{1F1E9}"                                                  // 8 bytes
    private let flagTwo = "\u{1F1EF}\u{1F1F5}"                                                  // 8 bytes

    // MARK: - head and tail

    func testHeadByCharactersKeepsWholeCharacters() {
        XCTAssertEqual(WireBudget.head("hello", maxCharacters: 2), "he")
        XCTAssertEqual(WireBudget.head("hello", maxCharacters: 5), "hello")
        XCTAssertEqual(WireBudget.head("hello", maxCharacters: 50), "hello")
        XCTAssertEqual(WireBudget.head("hello", maxCharacters: 0), "")
        XCTAssertEqual(WireBudget.head("hello", maxCharacters: -1), "")
        XCTAssertEqual(WireBudget.head("", maxCharacters: 3), "")

        let text = accented + family + flagOne + "x"
        XCTAssertEqual(text.count, 4)
        assertSameBytes(WireBudget.head(text, maxCharacters: 1), accented)
        assertSameBytes(WireBudget.head(text, maxCharacters: 2), accented + family)
        assertSameBytes(WireBudget.head(text, maxCharacters: 3), accented + family + flagOne)
        assertSameBytes(WireBudget.head(text, maxCharacters: 4), text)
    }

    func testHeadByBytesIsTheLongestPrefixOfWholeCharacters() {
        let mixed = "a\u{20AC}b"   // 1, 3 and 1 bytes
        let expected = ["", "a", "a", "a", "a\u{20AC}", "a\u{20AC}b", "a\u{20AC}b"]
        for (bytes, prefix) in expected.enumerated() {
            assertSameBytes(WireBudget.head(mixed, maxBytes: bytes), prefix, "\(bytes) bytes")
        }
        XCTAssertEqual(WireBudget.head(mixed, maxBytes: -5), "")
        XCTAssertEqual(WireBudget.head("", maxBytes: 10), "")

        let emoji = "\u{1F600}"
        let three = emoji + emoji + emoji
        assertSameBytes(WireBudget.head(three, maxBytes: 3), "")
        assertSameBytes(WireBudget.head(three, maxBytes: 4), emoji)
        assertSameBytes(WireBudget.head(three, maxBytes: 7), emoji)
        assertSameBytes(WireBudget.head(three, maxBytes: 8), emoji + emoji)
        assertSameBytes(WireBudget.head(three, maxBytes: 11), emoji + emoji)
        assertSameBytes(WireBudget.head(three, maxBytes: 12), three)
    }

    func testHeadByBytesNeverSplitsACharacterOfSeveralScalars() {
        XCTAssertEqual(accented.count, 1)
        XCTAssertEqual(family.count, 1)
        XCTAssertEqual(family.utf8.count, 25)
        XCTAssertEqual((flagOne + flagTwo).count, 2)

        assertSameBytes(WireBudget.head(accented + accented, maxBytes: 2), "", "Not the letter without its accent.")
        assertSameBytes(WireBudget.head(accented + accented, maxBytes: 3), accented)
        assertSameBytes(WireBudget.head(accented + accented, maxBytes: 5), accented)
        assertSameBytes(WireBudget.head(accented + accented, maxBytes: 6), accented + accented)

        assertSameBytes(WireBudget.head("x" + family + "y", maxBytes: 25), "x")
        assertSameBytes(WireBudget.head("x" + family + "y", maxBytes: 26), "x" + family)
        assertSameBytes(WireBudget.head("x" + family + "y", maxBytes: 27), "x" + family + "y")

        assertSameBytes(WireBudget.head(flagOne + flagTwo, maxBytes: 7), "")
        assertSameBytes(WireBudget.head(flagOne + flagTwo, maxBytes: 8), flagOne)
        assertSameBytes(WireBudget.head(flagOne + flagTwo, maxBytes: 12), flagOne, "Not a flag and a half.")
        assertSameBytes(WireBudget.head(flagOne + flagTwo, maxBytes: 16), flagOne + flagTwo)

        assertSameBytes(WireBudget.head("\r\nb", maxBytes: 1), "", "A carriage return and line feed are one character.")
        assertSameBytes(WireBudget.head("\r\nb", maxBytes: 2), "\r\n")
    }

    func testTailByBytesIsTheLongestSuffixOfWholeCharacters() {
        let mixed = "a\u{20AC}b"
        let expected = ["", "b", "b", "b", "\u{20AC}b", "a\u{20AC}b", "a\u{20AC}b"]
        for (bytes, suffix) in expected.enumerated() {
            assertSameBytes(WireBudget.tail(mixed, maxBytes: bytes), suffix, "\(bytes) bytes")
        }
        XCTAssertEqual(WireBudget.tail(mixed, maxBytes: -5), "")
        XCTAssertEqual(WireBudget.tail("", maxBytes: 10), "")

        let emoji = "\u{1F600}"
        let three = emoji + emoji + emoji
        assertSameBytes(WireBudget.tail(three, maxBytes: 3), "")
        assertSameBytes(WireBudget.tail(three, maxBytes: 4), emoji)
        assertSameBytes(WireBudget.tail(three, maxBytes: 7), emoji)
        assertSameBytes(WireBudget.tail(three, maxBytes: 8), emoji + emoji)
        assertSameBytes(WireBudget.tail(three, maxBytes: 12), three)
    }

    func testTailByBytesNeverSplitsACharacterOfSeveralScalars() {
        assertSameBytes(WireBudget.tail("x" + accented, maxBytes: 2), "", "Not the accent without its letter.")
        assertSameBytes(WireBudget.tail("x" + accented, maxBytes: 3), accented)
        assertSameBytes(WireBudget.tail("x" + accented, maxBytes: 4), "x" + accented)

        assertSameBytes(WireBudget.tail("x" + family + "y", maxBytes: 25), "y")
        assertSameBytes(WireBudget.tail("x" + family + "y", maxBytes: 26), family + "y")
        assertSameBytes(WireBudget.tail("x" + family + "y", maxBytes: 27), "x" + family + "y")

        assertSameBytes(WireBudget.tail(flagOne + flagTwo, maxBytes: 7), "")
        assertSameBytes(WireBudget.tail(flagOne + flagTwo, maxBytes: 8), flagTwo)
        assertSameBytes(WireBudget.tail(flagOne + flagTwo, maxBytes: 12), flagTwo, "Not a flag and a half.")
        assertSameBytes(WireBudget.tail(flagOne + flagTwo, maxBytes: 16), flagOne + flagTwo)

        assertSameBytes(WireBudget.tail("a\r\n", maxBytes: 1), "")
        assertSameBytes(WireBudget.tail("a\r\n", maxBytes: 2), "\r\n")
    }

    func testHeadAndTailOfALongText() {
        let long = "START" + String(repeating: "m", count: megabyte) + "END"
        XCTAssertEqual(WireBudget.tail(long, maxBytes: 1_000), String(repeating: "m", count: 997) + "END")
        XCTAssertEqual(WireBudget.head(long, maxBytes: 1_000), "START" + String(repeating: "m", count: 995))
        XCTAssertEqual(WireBudget.head(long, maxCharacters: 1_000), "START" + String(repeating: "m", count: 995))
        XCTAssertEqual(WireBudget.tail(long, maxBytes: megabyte + 8), long)
        XCTAssertEqual(WireBudget.head(long, maxBytes: megabyte + 8), long)

        // Three bytes each: 26,666 whole characters fit in 80,000 bytes.
        let accents = String(repeating: accented, count: 40_000)
        let kept = WireBudget.tail(accents, maxBytes: RelayConstants.assistTranscriptBytes)
        XCTAssertEqual(kept.utf8.count, 79_998)
        assertSameBytes(kept, String(repeating: accented, count: 26_666))
    }

    // MARK: - AC-CORE-011

    func testContextIsCapped() throws {
        let session = CoreFixtures.session(segments: [String(repeating: "x", count: 100_000)])
        var request = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil)
        XCTAssertEqual(request.transcript.count, 80_000)

        // The old cap still holds on the wire: 80,000 plain characters need no fitting.
        request.assistant = AssistantConfiguration()
        let fitted = try WireBudget.fitted(request)
        XCTAssertEqual(fitted, request)
        XCTAssertEqual(fitted.transcript.count, 80_000)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fitted).count, limit)
    }

    // MARK: - P2-207: the four inputs

    func testOneMegabyteOfQuotationMarksFits() throws {
        let fitted = try assertTranscriptFits(unit: "\"")
        // Each mark is two bytes on the wire, so step 3 had to cut what step 1 left.
        XCTAssertLessThan(fitted.assist.transcript.utf8.count, RelayConstants.assistTranscriptBytes)
        XCTAssertFalse(fitted.assist.transcript.isEmpty)
        XCTAssertLessThan(fitted.summary.transcript.utf8.count, RelayConstants.summaryTranscriptBytes)
        XCTAssertFalse(fitted.summary.transcript.isEmpty)
    }

    func testOneMegabyteOfAFourByteEmojiFits() throws {
        let fitted = try assertTranscriptFits(unit: "\u{1F600}")
        // Not escaped: the budgets of step 1 are enough.
        XCTAssertEqual(fitted.assist.transcript.utf8.count, RelayConstants.assistTranscriptBytes)
        XCTAssertEqual(fitted.assist.transcript.count, 20_000)
        XCTAssertEqual(fitted.summary.transcript.utf8.count, RelayConstants.summaryTranscriptBytes)
        XCTAssertEqual(fitted.summary.transcript.count, 25_000)
    }

    func testOneMegabyteOfControlCharactersFits() throws {
        let fitted = try assertTranscriptFits(unit: "\u{1}")
        XCTAssertLessThan(fitted.assist.transcript.utf8.count, RelayConstants.assistTranscriptBytes)
        XCTAssertFalse(fitted.assist.transcript.isEmpty)
        XCTAssertLessThan(fitted.summary.transcript.utf8.count, RelayConstants.summaryTranscriptBytes)
        XCTAssertFalse(fitted.summary.transcript.isEmpty)
    }

    func testAMemoryOfAThousandLongFactsFits() throws {
        let filler = String(repeating: "f", count: 10_000)
        let facts: [String] = (0..<1_000).map { index in
            let label = "fact \(index) "
            return label + String(filler.dropFirst(label.count))
        }
        XCTAssertEqual(facts[999].count, 10_000)

        var session = CoreFixtures.session(segments: ["What do we know?"])
        session.memory = SessionMemory(
            summary: "Earlier context",
            facts: facts,
            openQuestions: facts,
            throughSegmentId: session.segments[0].id
        )

        var assist = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil)
        assist.assistant = AssistantConfiguration()
        let fittedAssist = try WireBudget.fitted(assist)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fittedAssist).count, limit)
        XCTAssertEqual(fittedAssist, assist, "Step 1 was enough.")

        let summary = try XCTUnwrap(ContextBuilder.summaryRequest(session: session, assistant: AssistantConfiguration()))
        let fittedSummary = try WireBudget.fitted(summary)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fittedSummary).count, limit)
        XCTAssertEqual(fittedSummary, summary)
        XCTAssertNil(fittedSummary.transcriptTruncated)

        for memory in [fittedAssist.memory, fittedSummary.memory] {
            XCTAssertEqual(memory.summary, "Earlier context")
            XCTAssertEqual(memory.throughSegmentId, session.segments[0].id)
            XCTAssertEqual(memory.facts.count, RelayConstants.memoryListItems)
            XCTAssertEqual(memory.openQuestions, memory.facts)
            for (offset, fact) in memory.facts.enumerated() {
                // The newest sixteen, each cut to its first 256 bytes.
                XCTAssertEqual(fact, String(facts[984 + offset].prefix(256)))
                XCTAssertTrue(fact.hasPrefix("fact \(984 + offset) "))
            }
        }
    }

    /// P2-207 with the same megabyte in every text member at once, so that several members meet step 3.
    func testOneMegabyteOfQuotationMarksInEveryMemberFits() throws {
        let fitted = try assertEverythingFits(unit: "\"")
        // Only the transcript had to give way.
        XCTAssertFalse(fitted.assist.transcript.isEmpty)
        XCTAssertEqual(fitted.assist.memory, fitted.stepOne.memory)
        XCTAssertEqual(fitted.assist.partialTranscript, fitted.stepOne.partialTranscript)
        XCTAssertEqual(fitted.assist.instruction, fitted.stepOne.instruction)
        XCTAssertFalse(fitted.summary.transcript.isEmpty)
    }

    func testOneMegabyteOfAFourByteEmojiInEveryMemberFits() throws {
        let fitted = try assertEverythingFits(unit: "\u{1F600}")
        // Nothing is escaped, but the members at their budgets are together larger than the body may be.
        XCTAssertLessThan(fitted.assist.transcript.utf8.count, RelayConstants.assistTranscriptBytes)
        XCTAssertFalse(fitted.assist.transcript.isEmpty)
        XCTAssertEqual(fitted.assist.memory, fitted.stepOne.memory)
        XCTAssertEqual(fitted.assist.partialTranscript, fitted.stepOne.partialTranscript)
        XCTAssertEqual(fitted.assist.instruction, fitted.stepOne.instruction)
        XCTAssertEqual(fitted.assist.instruction?.utf8.count, RelayConstants.instructionBytes)
    }

    func testOneMegabyteOfControlCharactersInEveryMemberFits() throws {
        let fitted = try assertEverythingFits(unit: "\u{1}")
        // Six bytes each on the wire: the transcript and the memory summary go, and the partial is cut.
        XCTAssertEqual(fitted.assist.transcript, "")
        XCTAssertEqual(fitted.assist.memory.summary, "")
        let partial = try XCTUnwrap(fitted.assist.partialTranscript)
        XCTAssertFalse(partial.isEmpty)
        XCTAssertLessThan(partial.utf8.count, RelayConstants.partialTranscriptBytes)
        XCTAssertEqual(fitted.assist.memory.facts, fitted.stepOne.memory.facts)
        XCTAssertEqual(fitted.assist.memory.openQuestions, fitted.stepOne.memory.openQuestions)
        XCTAssertEqual(fitted.assist.instruction, fitted.stepOne.instruction)

        XCTAssertEqual(fitted.summary.transcript, "")
        XCTAssertEqual(fitted.summary.transcriptTruncated, true)
        XCTAssertFalse(fitted.summary.memory.summary.isEmpty)
    }

    /// The end of a text that step 3 cuts is recognisably its end.
    func testTheEndOfAnEscapedTextIsKept() throws {
        let text = "FIRST WORDS " + String(repeating: "\"", count: megabyte) + " LAST WORDS"
        let session = CoreFixtures.session(segments: [text])

        var assist = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil)
        assist.assistant = AssistantConfiguration()
        let fittedAssist = try WireBudget.fitted(assist)
        XCTAssertTrue(fittedAssist.transcript.hasSuffix("\"\"\" LAST WORDS"))
        XCTAssertTrue(fittedAssist.transcript.hasPrefix("\"\"\""))
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fittedAssist).count, limit)

        let summary = try XCTUnwrap(ContextBuilder.summaryRequest(session: session, assistant: AssistantConfiguration()))
        let fittedSummary = try WireBudget.fitted(summary)
        XCTAssertTrue(fittedSummary.transcript.hasSuffix("\"\"\" LAST WORDS"))
        XCTAssertEqual(fittedSummary.transcriptTruncated, true)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fittedSummary).count, limit)
    }

    // MARK: - Step 2: a body that fits

    func testARequestThatFitsIsReturnedUnchanged() throws {
        let original = request(
            transcript: "segment 2\nsegment 3",
            summary: "Earlier context",
            partial: "latest words",
            facts: ["A fact"],
            openQuestions: ["A question"],
            instruction: "Answer briefly"
        )
        XCTAssertEqual(try WireBudget.fitted(original), original)

        var withoutAssistant = original
        withoutAssistant.assistant = nil
        XCTAssertEqual(try WireBudget.fitted(withoutAssistant), withoutAssistant)

        let empty = AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: "")
        XCTAssertEqual(try WireBudget.fitted(empty), empty)
    }

    func testABodyOfExactlyTheLimitIsNotCutAndOneByteMoreIsCutByOneCharacter() throws {
        let frame = request(transcript: "", summary: "Earlier context", facts: ["A fact"])
        let frameBytes = try WireBudget.encodedBody(frame).count
        let room = limit - frameBytes

        var exact = frame
        exact.transcript = "A" + String(repeating: "t", count: room - 1)
        XCTAssertEqual(try WireBudget.encodedBody(exact).count, limit)
        XCTAssertEqual(try WireBudget.fitted(exact), exact)

        var over = frame
        over.transcript = "AB" + String(repeating: "t", count: room - 1)
        XCTAssertEqual(try WireBudget.encodedBody(over).count, limit + 1)
        let fitted = try WireBudget.fitted(over)
        XCTAssertEqual(fitted.transcript, "B" + String(repeating: "t", count: room - 1))
        XCTAssertEqual(try WireBudget.encodedBody(fitted).count, limit)
    }

    func testEncodedBodyIsTheBodyAsLiveCueJSONWritesIt() throws {
        let assist = request(transcript: "one\ntwo", summary: "S", partial: "p", facts: ["f"], openQuestions: ["q"], instruction: "i")
        let body = try WireBudget.encodedBody(assist)
        XCTAssertEqual(body, try LiveCueJSON.encode(assist))
        XCTAssertEqual(try LiveCueJSON.decode(AssistRequest.self, from: body), assist)
        let text = String(decoding: body, as: UTF8.self)
        XCTAssertTrue(text.hasPrefix("{\"assistant\":{"), "Sorted keys, no white space.")
        XCTAssertTrue(text.contains("\"transcript\":\"one\\ntwo\""))
        XCTAssertFalse(text.contains(": "))

        let summary = SummaryRequest(sessionId: UUID(), assistant: AssistantConfiguration(), memory: SessionMemory(), transcript: "one\ntwo")
        let summaryBody = try WireBudget.encodedBody(summary)
        XCTAssertEqual(summaryBody, try LiveCueJSON.encode(summary))
        XCTAssertEqual(try LiveCueJSON.decode(SummaryRequest.self, from: summaryBody), summary)
    }

    // MARK: - Step 3: the order in which members shrink

    /// X-13: plain ASCII, nothing escaped, every member exactly at its step 1 budget.
    func testPlainASCIIWithEveryMemberAtItsBudgetCutsOnlyTheTranscript() throws {
        let original = request(
            transcript: "HEAD" + String(repeating: "t", count: 79_992) + "TAIL",
            summary: String(repeating: "s", count: RelayConstants.memorySummaryBytes),
            partial: String(repeating: "p", count: RelayConstants.partialTranscriptBytes),
            facts: Array(repeating: String(repeating: "f", count: 256), count: 16),
            openQuestions: Array(repeating: String(repeating: "q", count: 256), count: 16),
            instruction: String(repeating: "i", count: RelayConstants.instructionBytes)
        )
        XCTAssertEqual(original.transcript.utf8.count, RelayConstants.assistTranscriptBytes)
        XCTAssertEqual(textBytes(original), 136_192)

        let unfitted = try WireBudget.encodedBody(original).count
        XCTAssertGreaterThan(unfitted, limit, "Step 2 does not fit although no member is over its budget.")

        let fitted = try WireBudget.fitted(original)
        // One byte per character, so the longest tail that fits fills the body exactly.
        XCTAssertEqual(try WireBudget.encodedBody(fitted).count, limit)
        XCTAssertEqual(fitted.transcript.utf8.count, RelayConstants.assistTranscriptBytes - (unfitted - limit))
        XCTAssertTrue(fitted.transcript.hasSuffix("tttTAIL"))
        XCTAssertTrue(fitted.transcript.hasPrefix("ttt"))
        XCTAssertTrue(original.transcript.hasSuffix(fitted.transcript))

        var expected = original
        expected.transcript = fitted.transcript
        XCTAssertEqual(fitted, expected, "No other member is touched.")
        assertShrinkOrder(from: original, to: fitted)
    }

    /// The same through the context builder: a long plain conversation with every member at its budget.
    func testAPlainConversationAtEveryBudgetIsFittedByStepThree() throws {
        var session = CoreFixtures.session(segments: [String(repeating: "t", count: 90_000)])
        session.memory = SessionMemory(
            summary: String(repeating: "s", count: 30_000),
            facts: Array(repeating: String(repeating: "f", count: 300), count: 20),
            openQuestions: Array(repeating: String(repeating: "q", count: 300), count: 20),
            throughSegmentId: UUID()
        )
        var stepOne = ContextBuilder.assistRequest(
            session: session,
            partial: String(repeating: "p", count: 20_000),
            instruction: String(repeating: "i", count: 5_000)
        )
        stepOne.assistant = AssistantConfiguration()
        // Transcript, memory summary, partial, two lists of sixteen items, and 4,000 characters of instruction.
        let atBudget: Int = 80_000 + 24_000 + 16_000 + 8_192 + 4_000
        XCTAssertEqual(textBytes(stepOne), atBudget)
        XCTAssertGreaterThan(try WireBudget.encodedBody(stepOne).count, limit)

        let fitted = try WireBudget.fitted(stepOne)
        XCTAssertEqual(try WireBudget.encodedBody(fitted).count, limit)
        var expected = stepOne
        expected.transcript = fitted.transcript
        XCTAssertEqual(fitted, expected)
        XCTAssertFalse(fitted.transcript.isEmpty)
        XCTAssertTrue(stepOne.transcript.hasSuffix(fitted.transcript))
    }

    func testTheTranscriptIsEmptiedBeforeTheMemorySummaryIsCut() throws {
        let original = request(
            transcript: String(repeating: "t", count: 50_000),
            summary: "OLD" + String(repeating: "s", count: 129_994) + "NEW",
            partial: "partial words",
            facts: ["a fact"],
            openQuestions: ["a question"],
            instruction: "Answer briefly"
        )
        let fitted = try WireBudget.fitted(original)
        XCTAssertEqual(try WireBudget.encodedBody(fitted).count, limit)
        XCTAssertEqual(fitted.transcript, "")
        XCTAssertTrue(fitted.memory.summary.hasSuffix("sssNEW"))
        XCTAssertTrue(fitted.memory.summary.hasPrefix("sss"))
        XCTAssertTrue(original.memory.summary.hasSuffix(fitted.memory.summary))

        var expected = original
        expected.transcript = ""
        expected.memory.summary = fitted.memory.summary
        XCTAssertEqual(fitted, expected)
        assertShrinkOrder(from: original, to: fitted)
    }

    func testThePartialIsCutAfterTheTranscriptAndTheMemorySummaryAreEmpty() throws {
        let original = request(
            transcript: String(repeating: "t", count: 1_000),
            summary: String(repeating: "s", count: 1_000),
            partial: "OLD" + String(repeating: "p", count: 129_994) + "NEW",
            facts: ["a fact"],
            openQuestions: ["a question"],
            instruction: "Answer briefly"
        )
        let fitted = try WireBudget.fitted(original)
        XCTAssertEqual(try WireBudget.encodedBody(fitted).count, limit)
        let partial = try XCTUnwrap(fitted.partialTranscript)
        XCTAssertTrue(partial.hasSuffix("pppNEW"))
        XCTAssertTrue(partial.hasPrefix("ppp"))

        var expected = original
        expected.transcript = ""
        expected.memory.summary = ""
        expected.partialTranscript = partial
        XCTAssertEqual(fitted, expected)
        assertShrinkOrder(from: original, to: fitted)
    }

    func testTheOldestFactsAreDroppedBeforeAnyOpenQuestion() throws {
        let items = longItems()
        let original = request(
            transcript: String(repeating: "t", count: 1_000),
            summary: String(repeating: "s", count: 1_000),
            partial: String(repeating: "p", count: 1_000),
            facts: items,
            openQuestions: ["q1", "q2", "q3"],
            instruction: "Answer briefly"
        )
        let fitted = try WireBudget.fitted(original)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fitted).count, limit)

        let kept = fitted.memory.facts.count
        XCTAssertGreaterThan(kept, 100)
        XCTAssertLessThan(kept, items.count)
        XCTAssertEqual(fitted.memory.facts, Array(items.suffix(kept)), "The newest items are kept, whole and in order.")

        var expected = original
        expected.transcript = ""
        expected.memory.summary = ""
        expected.partialTranscript = nil
        expected.memory.facts = fitted.memory.facts
        XCTAssertEqual(fitted, expected, "The open questions and the instruction are not touched.")
        assertShrinkOrder(from: original, to: fitted)

        // One more item would not have fitted.
        var larger = fitted
        larger.memory.facts = Array(items.suffix(kept + 1))
        XCTAssertGreaterThan(try WireBudget.encodedBody(larger).count, limit)
    }

    func testTheOpenQuestionsAreDroppedAfterTheFactsAreGone() throws {
        let items = longItems()
        let original = request(
            transcript: String(repeating: "t", count: 1_000),
            summary: String(repeating: "s", count: 1_000),
            partial: String(repeating: "p", count: 1_000),
            facts: ["f1", "f2", "f3"],
            openQuestions: items,
            instruction: "Answer briefly"
        )
        let fitted = try WireBudget.fitted(original)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fitted).count, limit)

        let kept = fitted.memory.openQuestions.count
        XCTAssertGreaterThan(kept, 100)
        XCTAssertLessThan(kept, items.count)
        XCTAssertEqual(fitted.memory.openQuestions, Array(items.suffix(kept)))

        var expected = original
        expected.transcript = ""
        expected.memory.summary = ""
        expected.partialTranscript = nil
        expected.memory.facts = []
        expected.memory.openQuestions = fitted.memory.openQuestions
        XCTAssertEqual(fitted, expected, "The instruction is not touched.")
        assertShrinkOrder(from: original, to: fitted)

        var larger = fitted
        larger.memory.openQuestions = Array(items.suffix(kept + 1))
        XCTAssertGreaterThan(try WireBudget.encodedBody(larger).count, limit)
    }

    func testTheInstructionIsCutLastAndKeepsItsHead() throws {
        let original = request(
            transcript: String(repeating: "t", count: 1_000),
            summary: String(repeating: "s", count: 1_000),
            partial: String(repeating: "p", count: 1_000),
            facts: ["f1", "f2", "f3"],
            openQuestions: ["q1", "q2", "q3"],
            instruction: "FIRST" + String(repeating: "i", count: 199_991) + "LAST"
        )
        let fitted = try WireBudget.fitted(original)
        XCTAssertEqual(try WireBudget.encodedBody(fitted).count, limit)
        let instruction = try XCTUnwrap(fitted.instruction)
        XCTAssertTrue(instruction.hasPrefix("FIRSTiii"))
        XCTAssertTrue(instruction.hasSuffix("iii"))
        XCTAssertTrue(try XCTUnwrap(original.instruction).hasPrefix(instruction))

        var expected = original
        expected.transcript = ""
        expected.memory.summary = ""
        expected.partialTranscript = nil
        expected.memory.facts = []
        expected.memory.openQuestions = []
        expected.instruction = instruction
        XCTAssertEqual(fitted, expected)
        assertShrinkOrder(from: original, to: fitted)
    }

    func testStepThreeCutsOnCharacterBoundaries() throws {
        // A quotation mark with a combining accent: one character, three bytes, four on the wire.
        let cluster = "\"\u{301}"
        XCTAssertEqual(cluster.count, 1)
        XCTAssertEqual(cluster.utf8.count, 3)
        let original = request(transcript: String(repeating: cluster, count: 50_000))
        XCTAssertGreaterThan(try WireBudget.encodedBody(original).count, limit)

        let fitted = try WireBudget.fitted(original)
        let kept = fitted.transcript
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fitted).count, limit)
        XCTAssertFalse(kept.isEmpty)
        XCTAssertEqual(kept.utf8.count % 3, 0)
        assertSameBytes(kept, String(repeating: cluster, count: kept.utf8.count / 3), "Whole characters only.")

        var larger = fitted
        larger.transcript = cluster + kept
        XCTAssertGreaterThan(try WireBudget.encodedBody(larger).count, limit, "One more character would not have fitted.")

        // The same for the head that an instruction keeps.
        let headed = request(instruction: String(repeating: cluster, count: 50_000))
        let fittedHead = try WireBudget.fitted(headed)
        let head = try XCTUnwrap(fittedHead.instruction)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fittedHead).count, limit)
        assertSameBytes(head, String(repeating: cluster, count: head.utf8.count / 3))
        XCTAssertEqual(head.utf8.count % 3, 0)
        XCTAssertFalse(head.isEmpty)
    }

    func testARequestThatCannotFitThrows() {
        var hopeless = request(transcript: "words")
        hopeless.assistant = AssistantConfiguration(model: String(repeating: "m", count: 200_000), reasoningEffort: "low")
        XCTAssertThrowsError(try WireBudget.fitted(hopeless)) { error in
            XCTAssertTrue(error is EncodingError, "The failure is an encoding error.")
        }

        let summary = SummaryRequest(
            sessionId: UUID(),
            assistant: AssistantConfiguration(model: String(repeating: "m", count: 200_000), reasoningEffort: "low"),
            memory: SessionMemory(),
            transcript: "words"
        )
        XCTAssertThrowsError(try WireBudget.fitted(summary))
    }

    // MARK: - Step 3 for a summary

    func testFittingASummaryCutsItsTranscriptAndSaysSo() throws {
        let original = summaryRequest(transcript: "HEAD" + String(repeating: "\"", count: 99_992) + "TAIL")
        XCTAssertNil(original.transcriptTruncated)
        XCTAssertEqual(original.transcript.utf8.count, RelayConstants.summaryTranscriptBytes)

        let fitted = try WireBudget.fitted(original)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fitted).count, limit)
        XCTAssertEqual(fitted.transcriptTruncated, true)
        XCTAssertTrue(fitted.transcript.hasSuffix("\"\"\"TAIL"))
        XCTAssertTrue(fitted.transcript.hasPrefix("\"\"\""))
        XCTAssertTrue(original.transcript.hasSuffix(fitted.transcript))

        var expected = original
        expected.transcript = fitted.transcript
        expected.transcriptTruncated = true
        XCTAssertEqual(fitted, expected)

        var larger = fitted
        larger.transcript = "\"" + fitted.transcript
        XCTAssertGreaterThan(try WireBudget.encodedBody(larger).count, limit)

        let body = try XCTUnwrap(JSONValue.parse(WireBudget.encodedBody(fitted)))
        XCTAssertEqual(body["transcriptTruncated"], .bool(true))
        XCTAssertEqual(body["requestId"], .string(original.requestId.uuidString))
    }

    func testASummaryThatFitsIsReturnedUnchanged() throws {
        let original = summaryRequest(transcript: String(repeating: "t", count: RelayConstants.summaryTranscriptBytes))
        let fitted = try WireBudget.fitted(original)
        XCTAssertEqual(fitted, original)
        XCTAssertNil(fitted.transcriptTruncated, "Not cut here, so not flagged here.")

        var alreadyCut = original
        alreadyCut.transcriptTruncated = true
        XCTAssertEqual(try WireBudget.fitted(alreadyCut), alreadyCut, "A flag set by step 1 stays.")
    }

    func testASummaryWhoseTranscriptIsNotCutIsNotFlagged() throws {
        var original = summaryRequest(transcript: "")
        original.memory.summary = "OLD" + String(repeating: "s", count: 129_994) + "NEW"

        let fitted = try WireBudget.fitted(original)
        XCTAssertEqual(try WireBudget.encodedBody(fitted).count, limit)
        XCTAssertNil(fitted.transcriptTruncated)
        XCTAssertTrue(fitted.memory.summary.hasSuffix("sssNEW"))
        XCTAssertTrue(fitted.memory.summary.hasPrefix("sss"))
    }

    func testSummaryMembersShrinkInOrder() throws {
        // The transcript goes first, and is flagged, before the memory summary is cut.
        var first = summaryRequest(transcript: String(repeating: "t", count: 50_000))
        first.memory.summary = "OLD" + String(repeating: "s", count: 129_994) + "NEW"
        let fittedFirst = try WireBudget.fitted(first)
        XCTAssertEqual(try WireBudget.encodedBody(fittedFirst).count, limit)
        XCTAssertEqual(fittedFirst.transcript, "")
        XCTAssertEqual(fittedFirst.transcriptTruncated, true)
        XCTAssertTrue(fittedFirst.memory.summary.hasSuffix("sssNEW"))
        XCTAssertEqual(fittedFirst.memory.facts, first.memory.facts)
        XCTAssertEqual(fittedFirst.memory.openQuestions, first.memory.openQuestions)

        // Then the facts, oldest first.
        let items = longItems()
        var second = summaryRequest(transcript: String(repeating: "t", count: 1_000))
        second.memory.summary = String(repeating: "s", count: 1_000)
        second.memory.facts = items
        second.memory.openQuestions = ["q1", "q2"]
        let fittedSecond = try WireBudget.fitted(second)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fittedSecond).count, limit)
        XCTAssertEqual(fittedSecond.transcript, "")
        XCTAssertEqual(fittedSecond.transcriptTruncated, true)
        XCTAssertEqual(fittedSecond.memory.summary, "")
        XCTAssertEqual(fittedSecond.memory.facts, Array(items.suffix(fittedSecond.memory.facts.count)))
        XCTAssertGreaterThan(fittedSecond.memory.facts.count, 100)
        XCTAssertEqual(fittedSecond.memory.openQuestions, ["q1", "q2"])

        // Then the open questions.
        var third = second
        third.memory.facts = ["f1"]
        third.memory.openQuestions = items
        let fittedThird = try WireBudget.fitted(third)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(fittedThird).count, limit)
        XCTAssertEqual(fittedThird.memory.facts, [])
        XCTAssertEqual(fittedThird.memory.openQuestions, Array(items.suffix(fittedThird.memory.openQuestions.count)))
        XCTAssertGreaterThan(fittedThird.memory.openQuestions.count, 100)
        var larger = fittedThird
        larger.memory.openQuestions = Array(items.suffix(fittedThird.memory.openQuestions.count + 1))
        XCTAssertGreaterThan(try WireBudget.encodedBody(larger).count, limit)
    }

    // MARK: - Support

    private struct Fitted {
        let stepOne: AssistRequest
        let assist: AssistRequest
        let summary: SummaryRequest
    }

    /// An assist request with the default assistant, as the session controller sends it to `fitted`.
    private func request(
        transcript: String = "",
        summary: String = "",
        partial: String? = nil,
        facts: [String] = [],
        openQuestions: [String] = [],
        instruction: String? = nil
    ) -> AssistRequest {
        var request = AssistRequest(
            sessionId: UUID(),
            instruction: instruction,
            memory: SessionMemory(summary: summary, facts: facts, openQuestions: openQuestions, throughSegmentId: UUID()),
            transcript: transcript,
            partialTranscript: partial
        )
        request.assistant = AssistantConfiguration()
        return request
    }

    private func summaryRequest(transcript: String) -> SummaryRequest {
        SummaryRequest(
            sessionId: UUID(),
            assistant: AssistantConfiguration(),
            memory: SessionMemory(
                summary: "Earlier context",
                facts: ["A fact"],
                openQuestions: ["A question"],
                throughSegmentId: UUID()
            ),
            transcript: transcript
        )
    }

    /// Two hundred items of 1,000 bytes, each starting with its number.
    private func longItems() -> [String] {
        (0..<200).map { index in
            let label = "item \(index) "
            return label + String(repeating: "x", count: 1_000 - label.count)
        }
    }

    /// The UTF-8 bytes of every text member together.
    private func textBytes(_ request: AssistRequest) -> Int {
        var total = request.transcript.utf8.count + request.memory.summary.utf8.count
        total += request.partialTranscript?.utf8.count ?? 0
        total += request.instruction?.utf8.count ?? 0
        for item in request.memory.facts + request.memory.openQuestions {
            total += item.utf8.count
        }
        return total
    }

    /// Equal byte for byte. `==` on strings would also accept another spelling of the same characters.
    private func assertSameBytes(
        _ actual: String,
        _ expected: String,
        _ message: String = "",
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        XCTAssertEqual(Array(actual.utf8), Array(expected.utf8), message, file: file, line: line)
    }

    /// `kept` is made of whole copies of `unit` and nothing else.
    private func assertWholeUnits(
        _ kept: String,
        of unit: String,
        _ message: String,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let unitBytes = unit.utf8.count
        XCTAssertEqual(kept.utf8.count % unitBytes, 0, message, file: file, line: line)
        assertSameBytes(kept, String(repeating: unit, count: kept.utf8.count / unitBytes), message, file: file, line: line)
    }

    /// Step 3 touches a member only when every member before it in the order is already empty.
    private func assertShrinkOrder(
        from original: AssistRequest,
        to fitted: AssistRequest,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let changed = [
            fitted.transcript != original.transcript,
            fitted.memory.summary != original.memory.summary,
            fitted.partialTranscript != original.partialTranscript,
            fitted.memory.facts != original.memory.facts,
            fitted.memory.openQuestions != original.memory.openQuestions,
            fitted.instruction != original.instruction
        ]
        let empty = [
            fitted.transcript.isEmpty,
            fitted.memory.summary.isEmpty,
            fitted.partialTranscript == nil,
            fitted.memory.facts.isEmpty,
            fitted.memory.openQuestions.isEmpty,
            fitted.instruction == nil
        ]
        let names = ["transcript", "memory.summary", "partialTranscript", "memory.facts", "memory.openQuestions", "instruction"]
        for later in 1..<names.count where changed[later] {
            for earlier in 0..<later {
                XCTAssertTrue(empty[earlier], "\(names[later]) was cut while \(names[earlier]) was not empty", file: file, line: line)
            }
        }
        // Nothing that is not text ever changes.
        XCTAssertEqual(fitted.requestId, original.requestId, file: file, line: line)
        XCTAssertEqual(fitted.sessionId, original.sessionId, file: file, line: line)
        XCTAssertEqual(fitted.assistant, original.assistant, file: file, line: line)
        XCTAssertEqual(fitted.memory.throughSegmentId, original.memory.throughSegmentId, file: file, line: line)
    }

    /// P2-207 for one megabyte of `unit` as the transcript: the assist body and the summary body are at
    /// most C-11 bytes, what is kept is whole characters, and it is the end of the text.
    private func assertTranscriptFits(
        unit: String,
        file: StaticString = #filePath,
        line: UInt = #line
    ) throws -> Fitted {
        let text = String(repeating: unit, count: megabyte / unit.utf8.count)
        XCTAssertEqual(text.utf8.count, megabyte, file: file, line: line)
        let session = CoreFixtures.session(segments: [text])

        var stepOne = ContextBuilder.assistRequest(session: session, partial: nil, instruction: nil)
        stepOne.assistant = AssistantConfiguration()
        let assist = try WireBudget.fitted(stepOne)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(assist).count, limit, "assist body", file: file, line: line)
        assertWholeUnits(assist.transcript, of: unit, "assist transcript", file: file, line: line)
        XCTAssertTrue(text.hasSuffix(assist.transcript), "assist transcript is the end", file: file, line: line)
        assertShrinkOrder(from: stepOne, to: assist, file: file, line: line)
        if assist.transcript != stepOne.transcript {
            var larger = assist
            larger.transcript = unit + assist.transcript
            XCTAssertGreaterThan(try WireBudget.encodedBody(larger).count, limit, "assist transcript is the longest", file: file, line: line)
        }

        let summaryStepOne = try XCTUnwrap(
            ContextBuilder.summaryRequest(session: session, assistant: AssistantConfiguration()),
            file: file,
            line: line
        )
        let summary = try WireBudget.fitted(summaryStepOne)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(summary).count, limit, "summary body", file: file, line: line)
        assertWholeUnits(summary.transcript, of: unit, "summary transcript", file: file, line: line)
        XCTAssertTrue(text.hasSuffix(summary.transcript), "summary transcript is the end", file: file, line: line)
        XCTAssertEqual(summary.transcriptTruncated, true, "a megabyte is always cut", file: file, line: line)
        XCTAssertEqual(summary.requestId, summaryStepOne.requestId, file: file, line: line)
        if summary.transcript != summaryStepOne.transcript {
            var larger = summary
            larger.transcript = unit + summary.transcript
            XCTAssertGreaterThan(try WireBudget.encodedBody(larger).count, limit, "summary transcript is the longest", file: file, line: line)
        }

        return Fitted(stepOne: stepOne, assist: assist, summary: summary)
    }

    /// The same with one megabyte of `unit` in the transcript, the memory summary, three facts, three
    /// open questions, the partial and the instruction, all at once.
    private func assertEverythingFits(
        unit: String,
        file: StaticString = #filePath,
        line: UInt = #line
    ) throws -> Fitted {
        let text = String(repeating: unit, count: megabyte / unit.utf8.count)
        var session = CoreFixtures.session(segments: [text])
        session.memory = SessionMemory(
            summary: text,
            facts: [text, text, text],
            openQuestions: [text, text, text],
            throughSegmentId: UUID()
        )

        var stepOne = ContextBuilder.assistRequest(session: session, partial: text, instruction: text)
        stepOne.assistant = AssistantConfiguration()
        XCTAssertLessThanOrEqual(stepOne.transcript.utf8.count, RelayConstants.assistTranscriptBytes, file: file, line: line)
        XCTAssertLessThanOrEqual(stepOne.memory.summary.utf8.count, RelayConstants.memorySummaryBytes, file: file, line: line)
        XCTAssertLessThanOrEqual(stepOne.partialTranscript?.utf8.count ?? 0, RelayConstants.partialTranscriptBytes, file: file, line: line)
        XCTAssertLessThanOrEqual(stepOne.instruction?.utf8.count ?? 0, RelayConstants.instructionBytes, file: file, line: line)
        XCTAssertLessThanOrEqual(stepOne.instruction?.count ?? 0, RelayConstants.instructionCharacters, file: file, line: line)

        let assist = try WireBudget.fitted(stepOne)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(assist).count, limit, "assist body", file: file, line: line)
        assertShrinkOrder(from: stepOne, to: assist, file: file, line: line)

        assertWholeUnits(assist.transcript, of: unit, "transcript", file: file, line: line)
        assertWholeUnits(assist.memory.summary, of: unit, "memory summary", file: file, line: line)
        assertWholeUnits(assist.partialTranscript ?? "", of: unit, "partial", file: file, line: line)
        assertWholeUnits(assist.instruction ?? "", of: unit, "instruction", file: file, line: line)
        XCTAssertEqual(assist.memory.facts.count, 3, file: file, line: line)
        XCTAssertEqual(assist.memory.openQuestions.count, 3, file: file, line: line)
        for item in assist.memory.facts + assist.memory.openQuestions {
            assertWholeUnits(item, of: unit, "list item", file: file, line: line)
            XCTAssertLessThanOrEqual(item.utf8.count, RelayConstants.memoryListItemBytes, file: file, line: line)
            XCTAssertGreaterThan(item.utf8.count + unit.utf8.count, RelayConstants.memoryListItemBytes, file: file, line: line)
        }

        let summaryStepOne = try XCTUnwrap(
            ContextBuilder.summaryRequest(session: session, assistant: AssistantConfiguration()),
            file: file,
            line: line
        )
        let summary = try WireBudget.fitted(summaryStepOne)
        XCTAssertLessThanOrEqual(try WireBudget.encodedBody(summary).count, limit, "summary body", file: file, line: line)
        XCTAssertEqual(summary.transcriptTruncated, true, file: file, line: line)
        assertWholeUnits(summary.transcript, of: unit, "summary transcript", file: file, line: line)
        assertWholeUnits(summary.memory.summary, of: unit, "summary memory", file: file, line: line)
        if summary.memory.summary != summaryStepOne.memory.summary {
            XCTAssertEqual(summary.transcript, "", "the memory summary is cut only after the transcript is empty", file: file, line: line)
        }
        XCTAssertEqual(summary.memory.facts, summaryStepOne.memory.facts, file: file, line: line)
        XCTAssertEqual(summary.memory.openQuestions, summaryStepOne.memory.openQuestions, file: file, line: line)

        return Fitted(stepOne: stepOne, assist: assist, summary: summary)
    }
}
