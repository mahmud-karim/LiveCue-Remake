import Foundation
@testable import LiveCueCore
import XCTest

/// The SSE parser and the whole-body form.
///
/// AC-CORE-055 to 069 and the supplementary statements S14 and S15 of `docs/spec/02-ios-core.md`
/// section 10. Lines are fed one at a time, without their terminators; "" is a blank line.
final class OpenRouterStreamParserTests: XCTestCase {
    // MARK: - Helpers

    /// Feeds the lines in order and returns what each one reported.
    private static func feed(_ lines: [String], to parser: inout OpenRouterStreamParser) throws -> [Bool] {
        var grew: [Bool] = []
        for line in lines {
            grew.append(try parser.consumeLine(line))
        }
        return grew
    }

    private static func envelope(_ json: String) throws -> OpenRouterStreamResult {
        try OpenRouterStreamParser.completeJSON(Data(json.utf8))
    }

    private static let partialEvent = #"data: {"choices":[{"delta":{"content":"Partial"}}]}"#
    private static let stopEvent = #"data: {"choices":[{"delta":{},"finish_reason":"stop"}]}"#

    // MARK: - testCommentsMultilineUnicodeReasoningAndRepeatedTerminalUsage

    func testCommentsMultilineUnicodeReasoningAndRepeatedTerminalUsage() throws {
        var parser = OpenRouterStreamParser()
        let grew = try Self.feed(
            [
                ": OPENROUTER PROCESSING",
                "event: message",
                #"data: {"id":"gen-1","#,
                #"data: "choices":[{"delta":{"reasoning":"not an answer","content":"Café 世界"}}]}"#,
                "",
                #"data: {"choices":[{"delta":{},"finish_reason":"stop"}]}"#,
                "",
                #"data: {"choices":[{"delta":{"content":""},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":5,"cost":"0.002"}}"#,
                "",
                "data: [DONE]",
                ""
            ],
            to: &parser
        )

        // Only the blank line that dispatched the event with content reports growth.
        XCTAssertEqual(grew, [false, false, false, false, true, false, false, false, false, false, false])
        XCTAssertTrue(parser.done)

        let result = try parser.result()
        // AC-CORE-055: the comment and the `event:` line are ignored, the two `data:` lines are joined,
        // reasoning is not rendered and a repeated "stop" is accepted.
        XCTAssertEqual(result.text, "Café 世界")
        // AC-CORE-056: the cost came as a string.
        XCTAssertEqual(result.usage?.costCredits, 0.002)
        // AC-CORE-057
        XCTAssertEqual(result.usage?.generationId, "gen-1")
        XCTAssertEqual(result.usage?.promptTokens, 10)
        XCTAssertEqual(result.usage?.completionTokens, 5)
        XCTAssertEqual(result.id, "gen-1")
        XCTAssertNil(result.model)
    }

    // MARK: - testTextBlocksAndUnknownAccountingDoNotDiscardAnswer

    func testTextBlocksAndUnknownAccountingDoNotDiscardAnswer() throws {
        let result = try Self.envelope(
            #"{"choices":[{"message":{"content":[{"type":"text","text":"A usable answer"},{"type":"reasoning","text":"Hidden"}]},"finish_reason":"stop"}],"usage":{"cost":{"unexpected":true}}}"#
        )

        // AC-CORE-058
        XCTAssertEqual(result.text, "A usable answer")
        // AC-CORE-059: a cost of an unknown shape is absent, and the answer is kept.
        XCTAssertNil(result.usage?.costCredits)
        XCTAssertNotNil(result.usage)
    }

    func testEveryTextBlockIsJoinedAndOtherBlocksAreNeverRendered() throws {
        let result = try Self.envelope(
            #"{"choices":[{"message":{"content":[{"type":"text","text":"One "},{"type":"image_url","image_url":{"url":"https://example.test/a.png"}},{"type":"text","text":"two"},{"type":"text"},{"type":"text","text":7},"loose",{"text":"untyped"},{"type":"refusal","refusal":"No."}]},"finish_reason":"stop"}]}"#
        )

        XCTAssertEqual(result.text, "One two")
    }

    // MARK: - testPlainAnswerNeverNeedsAUUIDOrMemoryObject

    func testPlainAnswerNeverNeedsAUUIDOrMemoryObject() throws {
        // AC-CORE-060
        let result = try Self.envelope(
            #"{"choices":[{"message":{"content":"Use a queue. throughSegmentId is not a UUID."},"finish_reason":"stop"}]}"#
        )

        XCTAssertTrue(result.text.contains("Use a queue"))
        XCTAssertEqual(result.text, "Use a queue. throughSegmentId is not a UUID.")
        XCTAssertNil(result.id)
        XCTAssertNil(result.model)
        XCTAssertNil(result.usage)
    }

    // MARK: - testEmptyLengthResultIsReportedAsTruncatedNotMalformed

    func testEmptyLengthResultIsReportedAsTruncatedNotMalformed() {
        // AC-CORE-061
        XCTAssertThrowsError(try Self.envelope(#"{"choices":[{"message":{"content":null},"finish_reason":"length"}]}"#)) { error in
            XCTAssertEqual(error as? OpenRouterError, .truncated)
        }
    }

    func testContentThatComesWithLengthIsNotKept() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed([Self.partialEvent, ""], to: &parser)

        XCTAssertThrowsError(
            try Self.feed([#"data: {"choices":[{"delta":{"content":" and more"},"finish_reason":"length"}]}"#, ""], to: &parser)
        ) { error in
            XCTAssertEqual(error as? OpenRouterError, .truncated)
        }
        // The finish reason is read before the content of the same frame.
        XCTAssertEqual(parser.text, "Partial")
    }

    // MARK: - testDisconnectedStreamCannotBecomeCompletedAnswer

    func testDisconnectedStreamCannotBecomeCompletedAnswer() throws {
        var parser = OpenRouterStreamParser()
        let grew = try Self.feed([Self.partialEvent, ""], to: &parser)
        XCTAssertEqual(grew, [false, true])

        // AC-CORE-062: no terminator, no answer; the text is readable all the same.
        XCTAssertThrowsError(try parser.result()) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
        XCTAssertEqual(parser.text, "Partial")
        XCTAssertFalse(parser.done)

        // AC-CORE-063: the terminator without a "stop" is no answer either.
        _ = try Self.feed(["data: [DONE]", ""], to: &parser)
        XCTAssertTrue(parser.done)
        XCTAssertThrowsError(try parser.result()) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
        XCTAssertEqual(parser.text, "Partial")
    }

    func testAStopWithoutTheTerminatorIsNoAnswer() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed([Self.partialEvent, "", Self.stopEvent, ""], to: &parser)

        XCTAssertFalse(parser.done)
        XCTAssertThrowsError(try parser.result()) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
    }

    func testAnAnswerOfNothingButWhitespaceIsNoAnswer() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed(
            [#"data: {"choices":[{"delta":{"content":" \n\t "},"finish_reason":"stop"}]}"#, "", "data: [DONE]", ""],
            to: &parser
        )

        XCTAssertTrue(parser.done)
        XCTAssertEqual(parser.text, " \n\t ")
        XCTAssertThrowsError(try parser.result()) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
        XCTAssertThrowsError(try Self.envelope(#"{"choices":[{"message":{"content":""},"finish_reason":"stop"}]}"#)) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
    }

    func testTheAnswerIsReturnedUntrimmed() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed(
            [#"data: {"choices":[{"delta":{"content":"  Yes.\n"},"finish_reason":"stop"}]}"#, "", "data: [DONE]", ""],
            to: &parser
        )

        XCTAssertEqual(try parser.result().text, "  Yes.\n")
    }

    // MARK: - testMidstreamErrorRetainsPreviewWithoutLeakingProviderMessage

    func testMidstreamErrorRetainsPreviewWithoutLeakingProviderMessage() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed([Self.partialEvent, ""], to: &parser)

        // AC-CORE-064
        XCTAssertNoThrow(try parser.consumeLine(#"data: {"error":{"message":"synthetic-key private transcript"}}"#))
        XCTAssertThrowsError(try parser.consumeLine("")) { error in
            XCTAssertEqual(error as? OpenRouterError, .unavailable)
            let description = (error as? OpenRouterError)?.errorDescription ?? ""
            XCTAssertFalse(description.isEmpty)
            XCTAssertFalse(description.contains("synthetic-key"))
            XCTAssertFalse(description.contains("private transcript"))
            XCTAssertFalse(error.localizedDescription.contains("synthetic-key"))
            XCTAssertFalse(error.localizedDescription.contains("private transcript"))
        }
        // AC-CORE-065
        XCTAssertEqual(parser.text, "Partial")
        // AC-CORE-066
        XCTAssertThrowsError(try parser.result())
    }

    func testAnErrorMemberOfAnyValueIsAnError() {
        // CORE-A13, kept: `null` counts too, and the error is read before anything else in the event.
        let events = [
            #"{"error":null}"#,
            #"{"error":false}"#,
            #"{"error":"x"}"#,
            #"{"error":{},"choices":[{"message":{"content":"Text"},"finish_reason":"stop"}]}"#
        ]
        for event in events {
            XCTAssertThrowsError(try Self.envelope(event), event) { error in
                XCTAssertEqual(error as? OpenRouterError, .unavailable, event)
            }
            var parser = OpenRouterStreamParser()
            XCTAssertNoThrow(try parser.consumeLine("data: " + event), event)
            XCTAssertThrowsError(try parser.consumeLine(""), event) { error in
                XCTAssertEqual(error as? OpenRouterError, .unavailable, event)
            }
            XCTAssertEqual(parser.text, "", event)
        }
    }

    // MARK: - testMalformedOrOversizedEventsFailSafely

    func testMalformedOrOversizedEventsFailSafely() throws {
        // AC-CORE-067: the line is accepted, the blank line that dispatches it fails.
        var malformed = OpenRouterStreamParser()
        XCTAssertFalse(try malformed.consumeLine("data: not-json"))
        XCTAssertThrowsError(try malformed.consumeLine("")) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }

        // AC-CORE-068: 128 KiB + 1 of data fails on that line.
        var oversized = OpenRouterStreamParser()
        XCTAssertThrowsError(try oversized.consumeLine("data: " + String(repeating: "x", count: 131_073))) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }

        // Exactly 128 KiB is still accepted as a line.
        var atLimit = OpenRouterStreamParser()
        XCTAssertFalse(try atLimit.consumeLine("data: " + String(repeating: "x", count: 131_072)))
    }

    func testTheEventLimitCountsEveryDataLineOfTheEvent() throws {
        var parser = OpenRouterStreamParser()
        XCTAssertFalse(try parser.consumeLine("data: " + String(repeating: "x", count: 100_000)))
        XCTAssertThrowsError(try parser.consumeLine("data: " + String(repeating: "x", count: 31_073))) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }

        // The count starts again with the next event.
        var fresh = OpenRouterStreamParser()
        let filler = String(repeating: " ", count: 100_000)
        _ = try Self.feed(["data: {\"choices\":[]" + filler + "}", ""], to: &fresh)
        _ = try Self.feed(["data: {\"choices\":[]" + filler + "}", ""], to: &fresh)
        XCTAssertEqual(fresh.text, "")
    }

    func testAnEventThatIsNotAJSONObjectFails() {
        for event in ["[]", "7", "\"text\"", "null", "true", "{", "[DONE] ", " [DONE]", "[done]"] {
            var parser = OpenRouterStreamParser()
            XCTAssertNoThrow(try parser.consumeLine("data: " + event), event)
            XCTAssertThrowsError(try parser.consumeLine(""), event) { error in
                XCTAssertEqual(error as? OpenRouterError, .invalidResponse, event)
            }
            XCTAssertFalse(parser.done, event)
        }
        XCTAssertThrowsError(try Self.envelope("[]")) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
        XCTAssertThrowsError(try OpenRouterStreamParser.completeJSON(Data())) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
        XCTAssertThrowsError(try OpenRouterStreamParser.completeJSON(Data([0xFF, 0xFE, 0x00]))) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
    }

    func testAnAnswerLongerThanOneMebibyteFails() throws {
        var parser = OpenRouterStreamParser()
        let block = String(repeating: "x", count: 104_857)
        let event = "data: {\"choices\":[{\"delta\":{\"content\":\"" + block + "\"}}]}"
        // Ten events of a tenth of the limit fit: 1,048,570 bytes.
        for _ in 0..<10 {
            _ = try Self.feed([event, ""], to: &parser)
        }
        XCTAssertEqual(parser.text.utf8.count, 1_048_570)

        // Six more bytes reach the limit exactly; one more passes it.
        _ = try Self.feed([#"data: {"choices":[{"delta":{"content":"123456"}}]}"#, ""], to: &parser)
        XCTAssertEqual(parser.text.utf8.count, 1_048_576)
        XCTAssertThrowsError(try Self.feed([#"data: {"choices":[{"delta":{"content":"7"}}]}"#, ""], to: &parser)) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
        XCTAssertEqual(parser.text.utf8.count, 1_048_576)
    }

    // MARK: - testRefusalAndToolCallAreNotAcceptedAsAnswers

    func testRefusalAndToolCallAreNotAcceptedAsAnswers() {
        // AC-CORE-069
        for reason in ["content_filter", "tool_calls", "error"] {
            let envelope = "{\"choices\":[{\"message\":{\"content\":\"Not a completed answer\"},\"finish_reason\":\"\(reason)\"}]}"
            XCTAssertThrowsError(try Self.envelope(envelope), reason) { error in
                XCTAssertEqual(error as? OpenRouterError, .invalidResponse, reason)
            }

            var parser = OpenRouterStreamParser()
            let event = "data: {\"choices\":[{\"delta\":{\"content\":\"Not a completed answer\"},\"finish_reason\":\"\(reason)\"}]}"
            XCTAssertNoThrow(try parser.consumeLine(event), reason)
            XCTAssertThrowsError(try parser.consumeLine(""), reason) { error in
                XCTAssertEqual(error as? OpenRouterError, .invalidResponse, reason)
            }
            // Content in the frame of a refusal is not kept.
            XCTAssertEqual(parser.text, "", reason)
        }
    }

    func testAFinishReasonThatIsNotAStringIsNotAFinishReason() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed(
            [
                #"data: {"choices":[{"delta":{"content":"Yes"},"finish_reason":null}]}"#,
                "",
                Self.stopEvent,
                "",
                "data: [DONE]",
                ""
            ],
            to: &parser
        )

        XCTAssertEqual(try parser.result().text, "Yes")
    }

    // MARK: - AC-CORE-S14

    func testDataWithoutAFollowingSpaceIsAccepted() throws {
        var parser = OpenRouterStreamParser()
        let grew = try Self.feed(
            [
                #"data:{"choices":[{"delta":{"content":"No space"},"finish_reason":"stop"}]}"#,
                "",
                "data:[DONE]",
                ""
            ],
            to: &parser
        )

        XCTAssertEqual(grew, [false, true, false, false])
        XCTAssertEqual(try parser.result().text, "No space")
    }

    func testOnlyOneLeadingSpaceOfTheDataIsDropped() throws {
        var parser = OpenRouterStreamParser()
        // Two spaces: one is the separator, the other is data. JSON does not mind it; "[DONE]" does.
        _ = try Self.feed([#"data:  {"choices":[{"delta":{"content":"Kept"},"finish_reason":"stop"}]}"#, ""], to: &parser)
        XCTAssertEqual(parser.text, "Kept")

        XCTAssertNoThrow(try parser.consumeLine("data:  [DONE]"))
        XCTAssertThrowsError(try parser.consumeLine("")) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
        XCTAssertFalse(parser.done)
    }

    func testAChoiceWhoseIndexIsNotZeroIsIgnored() throws {
        var parser = OpenRouterStreamParser()
        let grew = try Self.feed(
            [
                #"data: {"choices":[{"index":1,"delta":{"content":"Other"},"finish_reason":"length"}]}"#,
                "",
                #"data: {"choices":[{"index":2,"delta":{"content":"Third"}},{"index":0,"delta":{"content":"First"}},{"index":0,"delta":{"content":"Again"}}]}"#,
                "",
                // A choice without an integer index counts as index 0.
                #"data: {"choices":[{"index":"1","delta":{"content":" and"}}]}"#,
                "",
                #"data: {"choices":[{"delta":{"content":" last"},"finish_reason":"stop"}]}"#,
                "",
                "data: [DONE]",
                ""
            ],
            to: &parser
        )

        XCTAssertEqual(grew, [false, false, false, true, false, true, false, true, false, false])
        XCTAssertEqual(try parser.result().text, "First and last")
    }

    func testChoicesOfAnotherShapeCountAsEmpty() throws {
        var parser = OpenRouterStreamParser()
        let grew = try Self.feed(
            [
                #"data: {"choices":"none"}"#, "",
                #"data: {"choices":[]}"#, "",
                #"data: {"choices":["text",7,null]}"#, "",
                #"data: {"id":"gen-2"}"#, "",
                #"data: {"choices":[{"delta":"text"}]}"#, "",
                #"data: {"choices":[{"delta":{"content":7}}]}"#, "",
                #"data: {"choices":[{"message":{"content":"Whole-body member"}}]}"#, ""
            ],
            to: &parser
        )

        XCTAssertEqual(grew, Array(repeating: false, count: 14))
        XCTAssertEqual(parser.text, "")
    }

    func testACostThatIsNegativeNotANumberOrInfiniteIsAbsent() throws {
        let absent = ["-1", "-0.001", "\"-1\"", "\"NaN\"", "\"nan\"", "\"Infinity\"", "\"inf\"", "\"-inf\"", "\"abc\"", "\"\"", "null", "true", "[0.1]", "{\"total\":1}"]
        for cost in absent {
            let result = try Self.envelope(
                "{\"choices\":[{\"message\":{\"content\":\"Answer\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":3,\"cost\":\(cost)}}"
            )
            XCTAssertNil(result.usage?.costCredits, cost)
            // The rest of the usage, and the answer, are kept.
            XCTAssertEqual(result.usage?.promptTokens, 3, cost)
            XCTAssertEqual(result.text, "Answer", cost)
        }

        let present: [(String, Double)] = [("0", 0), ("0.002", 0.002), ("\"0.002\"", 0.002), ("\"0\"", 0), ("12", 12), ("\"1e-3\"", 0.001)]
        for (cost, expected) in present {
            let result = try Self.envelope(
                "{\"choices\":[{\"message\":{\"content\":\"Answer\"},\"finish_reason\":\"stop\"}],\"usage\":{\"cost\":\(cost)}}"
            )
            XCTAssertEqual(result.usage?.costCredits, expected, cost)
        }
    }

    func testUsageKeepsOnlyNumericCountsAndTheLatestFrameWins() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed(
            [
                #"data: {"id":"gen-a","model":"vendor/first","choices":[{"delta":{"content":"Hi"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"cost":0.5}}"#,
                "",
                #"data: {"id":"gen-b","model":"vendor/second","choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":"12","completion_tokens":true}}"#,
                "",
                "data: [DONE]",
                ""
            ],
            to: &parser
        )

        let result = try parser.result()
        // Later values replace earlier ones, and a later usage replaces the whole record.
        XCTAssertEqual(result.id, "gen-b")
        XCTAssertEqual(result.model, "vendor/second")
        XCTAssertEqual(result.usage, AssistantUsage(generationId: "gen-b"))

        // A `usage` that is not an object leaves the stored one alone.
        let kept = try Self.envelope(
            #"{"id":"gen-c","choices":[{"message":{"content":"Answer"},"finish_reason":"stop"}],"usage":"none"}"#
        )
        XCTAssertNil(kept.usage)
        XCTAssertEqual(kept.id, "gen-c")
    }

    // MARK: - AC-CORE-S15

    func testAFinalEventWithoutItsBlankLineCompletesWhenFlushedWithAnEmptyLine() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed(
            [#"data: {"choices":[{"delta":{"content":"Flushed"},"finish_reason":"stop"}]}"#, "", "data: [DONE]"],
            to: &parser
        )

        // The terminator is still pending: nothing dispatched it.
        XCTAssertFalse(parser.done)
        XCTAssertThrowsError(try parser.result())

        // What the client does at end of stream.
        XCTAssertFalse(try parser.consumeLine(""))
        XCTAssertTrue(parser.done)
        XCTAssertEqual(try parser.result().text, "Flushed")
    }

    func testAFinalContentEventWithoutItsBlankLineIsDispatchedByTheFlush() throws {
        var parser = OpenRouterStreamParser()
        XCTAssertFalse(try parser.consumeLine(#"data: {"choices":[{"delta":{"content":"Late"},"finish_reason":"stop"}]}"#))
        XCTAssertEqual(parser.text, "")

        XCTAssertTrue(try parser.consumeLine(""))
        XCTAssertEqual(parser.text, "Late")
        // Dispatched, but the terminator never came.
        XCTAssertThrowsError(try parser.result())
    }

    // MARK: - Lines

    func testLinesAfterTheTerminatorAreIgnored() throws {
        var parser = OpenRouterStreamParser()
        _ = try Self.feed(
            [#"data: {"choices":[{"delta":{"content":"Done"},"finish_reason":"stop"}]}"#, "", "data: [DONE]", ""],
            to: &parser
        )
        let after = try Self.feed(
            [
                #"data: {"choices":[{"delta":{"content":" and more"}}]}"#,
                "",
                "data: not-json",
                "",
                #"data: {"error":{"message":"late"}}"#,
                "",
                "data: " + String(repeating: "x", count: 200_000),
                ""
            ],
            to: &parser
        )

        XCTAssertEqual(after, Array(repeating: false, count: 8))
        XCTAssertEqual(try parser.result().text, "Done")
    }

    func testFieldsOtherThanDataAndBlankLinesWithoutDataDoNothing() throws {
        var parser = OpenRouterStreamParser()
        let grew = try Self.feed(
            ["", "", ":", ": keep-alive", "event: done", "id: 7", "retry: 3000", "unknown", " data: {}", "DATA: {}", "data", "datax: {}", ""],
            to: &parser
        )

        XCTAssertEqual(grew, Array(repeating: false, count: 13))
        XCTAssertEqual(parser.text, "")
        XCTAssertFalse(parser.done)
    }

    func testADataLineIsMatchedByItsBytes() throws {
        // A combining mark right after the colon joins it into one character; the field is still `data`.
        var parser = OpenRouterStreamParser()
        XCTAssertNoThrow(try parser.consumeLine("data:\u{301}x"))
        XCTAssertThrowsError(try parser.consumeLine("")) { error in
            XCTAssertEqual(error as? OpenRouterError, .invalidResponse)
        }
    }
}
