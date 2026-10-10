import Foundation
import LiveCueCore
import XCTest

/// Dates with exactly three fractional digits out and zero to nine in; UUID case; absent optionals.
final class LiveCueJSONTests: XCTestCase {
    // MARK: - Writing dates (CORE-A12, IOS-B18)

    func testDatesAreWrittenWithExactlyThreeFractionalDigits() {
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_863_402.123)), "2026-10-01T14:03:22.123Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_863_402)), "2026-10-01T14:03:22.000Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_863_402.5)), "2026-10-01T14:03:22.500Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_863_402.007)), "2026-10-01T14:03:22.007Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_000_000)), "2026-09-21T14:13:20.000Z")
    }

    func testDatesAreRoundedToTheNearestMillisecond() {
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_863_402.1234)), "2026-10-01T14:03:22.123Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_863_402.1236)), "2026-10-01T14:03:22.124Z")
        XCTAssertEqual(
            LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_863_402.9996)),
            "2026-10-01T14:03:23.000Z",
            "Rounding up carries into the seconds."
        )
        XCTAssertEqual(
            LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_790_899_199.9996)),
            "2026-10-02T00:00:00.000Z",
            "Rounding up carries into the date."
        )
    }

    func testCalendarEdges() {
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 0)), "1970-01-01T00:00:00.000Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: -0.001)), "1969-12-31T23:59:59.999Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: -86_400)), "1969-12-31T00:00:00.000Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 951_782_400)), "2000-02-29T00:00:00.000Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 68_169_599.999)), "1972-02-28T23:59:59.999Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1_709_164_800)), "2024-02-29T00:00:00.000Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 978_307_200)), "2001-01-01T00:00:00.000Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSinceReferenceDate: 0)), "2001-01-01T00:00:00.000Z")
    }

    func testADateOutsideTheFourDigitYearsIsWrittenAsTheNearestEnd() {
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 1e15)), "9999-12-31T23:59:59.999Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: -1e15)), "0000-01-01T00:00:00.000Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: 253_402_300_799.999)), "9999-12-31T23:59:59.999Z")
        XCTAssertEqual(LiveCueJSON.string(from: Date(timeIntervalSince1970: -62_167_219_200)), "0000-01-01T00:00:00.000Z")
    }

    // MARK: - Reading dates

    func testZeroToNineFractionalDigitsAreAccepted() throws {
        // The text as read, and the same instant as it is written back.
        let cases: [(text: String, written: String)] = [
            ("2026-10-01T14:03:22Z", "2026-10-01T14:03:22.000Z"),
            ("2026-10-01T14:03:22.1Z", "2026-10-01T14:03:22.100Z"),
            ("2026-10-01T14:03:22.12Z", "2026-10-01T14:03:22.120Z"),
            ("2026-10-01T14:03:22.123Z", "2026-10-01T14:03:22.123Z"),
            ("2026-10-01T14:03:22.1234Z", "2026-10-01T14:03:22.123Z"),
            ("2026-10-01T14:03:22.12345Z", "2026-10-01T14:03:22.123Z"),
            ("2026-10-01T14:03:22.123456Z", "2026-10-01T14:03:22.123Z"),
            ("2026-10-01T14:03:22.1234567Z", "2026-10-01T14:03:22.123Z"),
            ("2026-10-01T14:03:22.12345678Z", "2026-10-01T14:03:22.123Z"),
            ("2026-10-01T14:03:22.123456789Z", "2026-10-01T14:03:22.123Z"),
            ("2026-10-01T14:03:22.000000000Z", "2026-10-01T14:03:22.000Z"),
            ("2026-10-01T14:03:22.0Z", "2026-10-01T14:03:22.000Z")
        ]
        for entry in cases {
            let date = try XCTUnwrap(LiveCueJSON.date(from: entry.text), entry.text)
            XCTAssertEqual(LiveCueJSON.string(from: date), entry.written, entry.text)
        }

        let whole = try XCTUnwrap(LiveCueJSON.date(from: "2026-10-01T14:03:22Z"))
        XCTAssertEqual(whole, Date(timeIntervalSince1970: 1_790_863_402))
        let milliseconds = try XCTUnwrap(LiveCueJSON.date(from: "2026-10-01T14:03:22.123Z"))
        XCTAssertEqual(milliseconds, Date(timeIntervalSince1970: 1_790_863_402.123))
        let epoch = try XCTUnwrap(LiveCueJSON.date(from: "1970-01-01T00:00:00.000Z"))
        XCTAssertEqual(epoch.timeIntervalSince1970, 0)
        let before = try XCTUnwrap(LiveCueJSON.date(from: "1969-12-31T23:59:59.999Z"))
        XCTAssertEqual(before.timeIntervalSince1970, -0.001, accuracy: 0.000_000_1)
    }

    func testDigitsBeyondTheThirdAreRoundedToTheMillisecond() throws {
        let cases: [(text: String, written: String)] = [
            ("2026-10-01T14:03:22.1235Z", "2026-10-01T14:03:22.124Z"),
            ("2026-10-01T14:03:22.123499999Z", "2026-10-01T14:03:22.123Z"),
            ("2026-10-01T14:03:22.9996Z", "2026-10-01T14:03:23.000Z"),
            ("2026-12-31T23:59:59.999500000Z", "2027-01-01T00:00:00.000Z"),
            ("9999-12-31T23:59:59.999999999Z", "9999-12-31T23:59:59.999Z")
        ]
        for entry in cases {
            let date = try XCTUnwrap(LiveCueJSON.date(from: entry.text), entry.text)
            XCTAssertEqual(LiveCueJSON.string(from: date), entry.written, entry.text)
        }
    }

    func testEveryCalendarDateThatExistsIsAccepted() {
        XCTAssertNotNil(LiveCueJSON.date(from: "2024-02-29T00:00:00Z"))
        XCTAssertNotNil(LiveCueJSON.date(from: "2000-02-29T23:59:59.999Z"))
        XCTAssertNotNil(LiveCueJSON.date(from: "2026-01-31T00:00:00Z"))
        XCTAssertNotNil(LiveCueJSON.date(from: "2026-04-30T00:00:00Z"))
        XCTAssertNotNil(LiveCueJSON.date(from: "2026-12-31T23:59:59Z"))
        XCTAssertNotNil(LiveCueJSON.date(from: "0000-01-01T00:00:00Z"))
        XCTAssertNotNil(LiveCueJSON.date(from: "9999-12-31T23:59:59.999Z"))
    }

    func testANonUTCDateFails() {
        let texts = [
            "2026-10-01T14:03:22+00:00",
            "2026-10-01T14:03:22.123+00:00",
            "2026-10-01T14:03:22+01:00",
            "2026-10-01T14:03:22.123-05:00",
            "2026-10-01T14:03:22+0000",
            "2026-10-01T14:03:22",
            "2026-10-01T14:03:22.123",
            "2026-10-01T14:03:22z",
            "2026-10-01T14:03:22 Z",
            "2026-10-01T14:03:22ZZ",
            "2026-10-01T14:03:22Z+01:00"
        ]
        for text in texts {
            XCTAssertNil(LiveCueJSON.date(from: text), text)
        }
    }

    func testAMalformedDateFails() {
        let texts = [
            "",
            "Z",
            "not a date",
            "2026-10-01",
            "2026-10-01T14:03Z",
            "2026-10-01 14:03:22Z",
            "2026-10-01t14:03:22Z",
            "2026-10-01T14:03:22.Z",
            "2026-10-01T14:03:22,123Z",
            "2026-10-01T14:03:22.1234567890Z",
            "2026-10-01T14:03:22.12a4Z",
            "2026-10-01T14:03:22.-12Z",
            "2026-10-01T14:03:2Z",
            "2026-10-1T14:03:22Z",
            "26-10-01T14:03:22Z",
            "02026-10-01T14:03:22Z",
            "+2026-10-01T14:03:22Z",
            "2026/10/01T14:03:22Z",
            "2026-10-01T14.03.22Z",
            "20261001T140322Z",
            "2O26-10-01T14:03:22Z",
            " 2026-10-01T14:03:22Z",
            "2026-10-01T14:03:22Z ",
            "2026-10-01T14:03:22Z\n",
            "\u{FF12}026-10-01T14:03:22Z",
            "2026-10-01T14:03:22\u{0301}Z",
            "2026-00-01T14:03:22Z",
            "2026-13-01T14:03:22Z",
            "2026-10-00T14:03:22Z",
            "2026-10-32T14:03:22Z",
            "2026-04-31T14:03:22Z",
            "2026-02-29T14:03:22Z",
            "2026-02-30T14:03:22Z",
            "1900-02-29T14:03:22Z",
            "2026-10-01T24:00:00Z",
            "2026-10-01T14:60:22Z",
            "2026-10-01T14:03:60Z",
            "1790863402",
            "1790863402.123"
        ]
        for text in texts {
            XCTAssertNil(LiveCueJSON.date(from: text), text.debugDescription)
        }
    }

    func testTheDecoderFailsOnADateItDoesNotAccept() throws {
        func turn(createdAt: String) -> Data {
            Data("{\"id\":\"6F9619FF-8B86-4011-B42D-00C04FC964FF\",\"createdAt\":\(createdAt),\"detectedQuestion\":\"Q\",\"answer\":\"A\",\"details\":\"\"}".utf8)
        }

        let wholeSeconds = try LiveCueJSON.decode(AssistantTurn.self, from: turn(createdAt: "\"2026-10-01T14:03:22Z\""))
        XCTAssertEqual(wholeSeconds.createdAt, Date(timeIntervalSince1970: 1_790_863_402))
        let nineDigits = try LiveCueJSON.decode(AssistantTurn.self, from: turn(createdAt: "\"2026-10-01T14:03:22.123456789Z\""))
        XCTAssertEqual(nineDigits.createdAt, Date(timeIntervalSince1970: 1_790_863_402.123))

        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantTurn.self, from: turn(createdAt: "\"2026-10-01T14:03:22+00:00\"")))
        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantTurn.self, from: turn(createdAt: "\"2026-10-01T15:03:22+01:00\"")))
        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantTurn.self, from: turn(createdAt: "\"2026-10-01\"")))
        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantTurn.self, from: turn(createdAt: "\"\"")))
        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantTurn.self, from: turn(createdAt: "812556202.123")), "a number")
        XCTAssertThrowsError(try LiveCueJSON.decode(AssistantTurn.self, from: turn(createdAt: "null")))
    }

    // MARK: - Round trips

    func testRoundedToMillisecondsIsWhatIsWrittenAndReadBack() throws {
        let seconds: [TimeInterval] = [
            0, 0.001, -0.001, 1.9996, 1_790_000_000, 1_790_000_000.123, 1_790_000_000.123_456_7, 1_790_863_402.999_9,
            951_782_400.5, -12_345.678_9, 4_102_444_800.001, 253_402_300_799.999, -62_167_219_200
        ]
        for value in seconds {
            let date = Date(timeIntervalSince1970: value)
            let rounded = LiveCueJSON.roundedToMilliseconds(date)
            XCTAssertEqual(LiveCueJSON.roundedToMilliseconds(rounded), rounded, "\(value): rounding twice changes nothing")
            XCTAssertEqual(rounded.timeIntervalSince1970, value, accuracy: 0.000_500_1, "\(value)")

            let text = LiveCueJSON.string(from: date)
            XCTAssertEqual(text, LiveCueJSON.string(from: rounded), "\(value)")
            let read = try XCTUnwrap(LiveCueJSON.date(from: text), text)
            XCTAssertEqual(read, rounded, "\(value): the date read back is the rounded date, exactly")
            XCTAssertEqual(LiveCueJSON.string(from: read), text, "\(value)")
        }
    }

    func testInitialisersRoundTheirDatesToWholeMilliseconds() {
        let session = Session(
            startedAt: Date(timeIntervalSince1970: 1_790_000_000.123_456_7),
            endedAt: Date(timeIntervalSince1970: 1_790_000_600.999_6)
        )
        XCTAssertEqual(session.startedAt, Date(timeIntervalSince1970: 1_790_000_000.123))
        XCTAssertEqual(session.endedAt, Date(timeIntervalSince1970: 1_790_000_601))

        let turn = AssistantTurn(
            createdAt: Date(timeIntervalSince1970: 1_790_000_038.000_4),
            detectedQuestion: "Q",
            answer: "A",
            details: ""
        )
        XCTAssertEqual(turn.createdAt, Date(timeIntervalSince1970: 1_790_000_038))
    }

    /// CORE-A12: a reloaded session compares equal to the one in memory.
    func testASessionWithSubMillisecondDatesRoundTripsExactly() throws {
        var session = Session(
            startedAt: Date(timeIntervalSince1970: 1_790_000_000.123_456_7),
            endedAt: Date(timeIntervalSince1970: 1_790_000_600.987_654_3),
            segments: [TranscriptSegment(text: "Hello.", startSeconds: 0.25, endSeconds: 1.5)],
            assistantTurns: [
                AssistantTurn(createdAt: Date(timeIntervalSince1970: 1_790_000_010.000_49), detectedQuestion: "Q1", answer: "A1", details: ""),
                AssistantTurn(createdAt: Date(timeIntervalSince1970: 1_790_000_010.250_9), detectedQuestion: "Q2", answer: "A2", details: "")
            ]
        )
        session.transcriptionUsage = TranscriptionUsage()

        let data = try LiveCueJSON.encode(session)
        let reloaded = try LiveCueJSON.decode(Session.self, from: data)
        XCTAssertEqual(reloaded, session)
        XCTAssertLessThan(reloaded.assistantTurns[0].createdAt, reloaded.assistantTurns[1].createdAt, "IOS-B18")
        XCTAssertEqual(try LiveCueJSON.encode(reloaded), data, "Encoding is stable.")

        let json = try XCTUnwrap(JSONValue.parse(data))
        XCTAssertEqual(json["startedAt"], .string("2026-09-21T14:13:20.123Z"))
        XCTAssertEqual(json["endedAt"], .string("2026-09-21T14:23:20.988Z"))
        XCTAssertEqual(json["assistantTurns"]?.arrayValue?.first?["createdAt"], .string("2026-09-21T14:13:30.000Z"))
        XCTAssertEqual(json["assistantTurns"]?.arrayValue?.last?["createdAt"], .string("2026-09-21T14:13:30.251Z"))
    }

    // MARK: - UUIDs

    func testUUIDsAreWrittenUpperCaseAndReadInEitherCase() throws {
        let upper = "6F9619FF-8B86-4011-B42D-00C04FC964FF"
        let id = try XCTUnwrap(UUID(uuidString: upper.lowercased()))
        let segment = TranscriptSegment(id: id, text: "Hello", startSeconds: 0, endSeconds: 1)

        let written = try XCTUnwrap(JSONValue.parse(LiveCueJSON.encode(segment)))
        XCTAssertEqual(written["id"], .string(upper))

        for text in [upper, upper.lowercased(), "6f9619ff-8B86-4011-b42d-00C04FC964ff"] {
            let json = Data("{\"id\":\"\(text)\",\"text\":\"Hello\",\"startSeconds\":0,\"endSeconds\":1}".utf8)
            XCTAssertEqual(try LiveCueJSON.decode(TranscriptSegment.self, from: json), segment, text)
        }

        let notAUUID = Data("{\"id\":\"6F9619FF8B864011B42D00C04FC964FF\",\"text\":\"Hello\",\"startSeconds\":0,\"endSeconds\":1}".utf8)
        XCTAssertThrowsError(try LiveCueJSON.decode(TranscriptSegment.self, from: notAUUID))
    }

    func testRequestIdsAreWrittenAsUUIDString() throws {
        let request = AssistRequest(sessionId: UUID(), memory: SessionMemory(throughSegmentId: UUID()), transcript: "t")
        let json = try XCTUnwrap(JSONValue.parse(LiveCueJSON.encode(request)))
        XCTAssertEqual(json["requestId"], .string(request.requestId.uuidString))
        XCTAssertEqual(json["sessionId"], .string(request.sessionId.uuidString))
        XCTAssertEqual(json["memory"]?["throughSegmentId"]?.stringValue, request.memory.throughSegmentId?.uuidString)
        XCTAssertEqual(request.requestId.uuidString, request.requestId.uuidString.uppercased())
    }

    // MARK: - Optionals

    func testAbsentOptionalsAreOmittedNeverNull() throws {
        let turn = try LiveCueJSON.encode(AssistantTurn(detectedQuestion: "Q", answer: "A", details: ""))
        XCTAssertEqual(try keys(turn), ["answer", "createdAt", "details", "detectedQuestion", "id"])

        let session = try LiveCueJSON.encode(Session())
        XCTAssertEqual(try keys(session), ["assistantTurns", "id", "memory", "notes", "segments", "startedAt", "title"])

        let memory = try LiveCueJSON.encode(SessionMemory())
        XCTAssertEqual(try keys(memory), ["facts", "openQuestions", "summary"])

        let assist = try LiveCueJSON.encode(AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: ""))
        XCTAssertEqual(try keys(assist), ["memory", "requestId", "sessionId", "transcript"])

        let summary = try LiveCueJSON.encode(
            SummaryRequest(sessionId: UUID(), assistant: AssistantConfiguration(), memory: SessionMemory(), transcript: "t")
        )
        XCTAssertEqual(try keys(summary), ["assistant", "memory", "requestId", "sessionId", "transcript"])

        let stream = try LiveCueJSON.encode(SpeechStreamUsage(id: UUID()))
        XCTAssertEqual(try keys(stream), ["hasTranscript", "id", "sentMs", "state"])

        let status = try LiveCueJSON.encode(PCModelStatus(state: "stopped", message: "Stopped"))
        XCTAssertEqual(try keys(status), ["busy", "elapsedSeconds", "message", "state"])

        for data in [turn, session, memory, assist, summary, stream, status] {
            let text = String(decoding: data, as: UTF8.self)
            XCTAssertFalse(text.contains("null"), text)
        }
    }

    func testAnOptionalAcceptsAbsentOrNull() throws {
        let absent = Data("{\"summary\":\"S\",\"facts\":[],\"openQuestions\":[]}".utf8)
        let null = Data("{\"summary\":\"S\",\"facts\":[],\"openQuestions\":[],\"throughSegmentId\":null}".utf8)
        XCTAssertEqual(try LiveCueJSON.decode(SessionMemory.self, from: absent), SessionMemory(summary: "S"))
        XCTAssertEqual(try LiveCueJSON.decode(SessionMemory.self, from: null), SessionMemory(summary: "S"))

        let nullTurn = Data(
            "{\"id\":\"6F9619FF-8B86-4011-B42D-00C04FC964FF\",\"createdAt\":\"2026-10-01T14:03:22.123Z\",\"request\":null,\"detectedQuestion\":\"Q\",\"answer\":\"A\",\"details\":\"\",\"performance\":null}".utf8
        )
        let turn = try LiveCueJSON.decode(AssistantTurn.self, from: nullTurn)
        XCTAssertNil(turn.request)
        XCTAssertNil(turn.performance)
        XCTAssertEqual(turn.createdAt, Date(timeIntervalSince1970: 1_790_863_402.123))
    }

    // MARK: - The retired `isFinal` member (CORE-C2)

    func testAStoredIsFinalKeyIsIgnored() throws {
        let id = try XCTUnwrap(UUID(uuidString: "6F9619FF-8B86-4011-B42D-00C04FC964FF"))
        let expected = TranscriptSegment(id: id, text: "Hello there.", startSeconds: 0.2, endSeconds: 3.1)
        for stored in ["true", "false", "null", "\"yes\""] {
            let json = Data(
                "{\"id\":\"6F9619FF-8B86-4011-B42D-00C04FC964FF\",\"text\":\"Hello there.\",\"startSeconds\":0.2,\"endSeconds\":3.1,\"isFinal\":\(stored)}".utf8
            )
            XCTAssertEqual(try LiveCueJSON.decode(TranscriptSegment.self, from: json), expected, stored)
            XCTAssertEqual(try JSONDecoder().decode(TranscriptSegment.self, from: json), expected, stored)
        }
        XCTAssertEqual(try keys(LiveCueJSON.encode(expected)), ["endSeconds", "id", "startSeconds", "text"])
    }

    // MARK: - Output formatting

    func testOutputIsCompactWithSortedKeysAndPlainSlashes() throws {
        let configuration = AssistantConfiguration(model: "vendor/model", reasoningEffort: "default", provider: .openrouter)
        let text = try String(decoding: LiveCueJSON.encode(configuration), as: UTF8.self)
        XCTAssertEqual(text, "{\"model\":\"vendor/model\",\"provider\":\"openrouter\",\"reasoningEffort\":\"default\"}")

        let notes = SessionNotes(summary: "See https://example.test/a/b", keyPoints: ["One", "Two"], actionItems: [])
        let notesText = try String(decoding: LiveCueJSON.encode(notes), as: UTF8.self)
        XCTAssertEqual(notesText, "{\"actionItems\":[],\"keyPoints\":[\"One\",\"Two\"],\"summary\":\"See https://example.test/a/b\"}")
    }

    func testEachCallMakesItsOwnEncoderAndDecoder() {
        XCTAssertFalse(LiveCueJSON.makeEncoder() === LiveCueJSON.makeEncoder())
        XCTAssertFalse(LiveCueJSON.makeDecoder() === LiveCueJSON.makeDecoder())
        XCTAssertTrue(LiveCueJSON.makeEncoder().outputFormatting.contains(.sortedKeys))
        XCTAssertTrue(LiveCueJSON.makeEncoder().outputFormatting.contains(.withoutEscapingSlashes))
        XCTAssertFalse(LiveCueJSON.makeEncoder().outputFormatting.contains(.prettyPrinted))
    }

    func testTextThatNeedsEscapingRoundTrips() throws {
        let text = "quote \" backslash \\ slash / newline \n tab \t control \u{01} emoji \u{1F600} accent e\u{301}"
        let segment = TranscriptSegment(text: text, startSeconds: 0, endSeconds: 1)
        let decoded = try LiveCueJSON.decode(TranscriptSegment.self, from: LiveCueJSON.encode(segment))
        XCTAssertEqual(decoded, segment)
        XCTAssertEqual(Array(decoded.text.unicodeScalars), Array(text.unicodeScalars), "No normalisation on the way.")
    }

    // MARK: - Support

    /// The member names of a JSON object, sorted.
    private func keys(_ data: Data) throws -> [String] {
        try XCTUnwrap(JSONValue.parse(data)?.objectValue).keys.sorted()
    }
}
