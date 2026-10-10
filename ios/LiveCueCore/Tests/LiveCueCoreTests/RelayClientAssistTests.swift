import Foundation
import LiveCueCore
import XCTest

/// The echo check, the marker, the fitted body and the one cancel.
///
/// P2-210, the core half of P2-209 and P2-212 of protocol 15.9.
final class RelayClientAssistTests: XCTestCase {
    // MARK: - Fixtures

    private static let notFoundJSON = #"{"error":"No request with that id is in progress.","code":"REQUEST_NOT_FOUND"}"#
    private static let cancelledJSON = #"{"ok":true,"cancelled":true}"#
    private static let healthJSON = #"{"ok":true,"protocol":2,"relayVersion":"1.0.0"}"#

    private static func reply(_ status: Int, _ json: String) -> FakeHTTPTransport.Reply {
        .response(status: status, headers: ["content-type": "application/json; charset=utf-8"], body: Data(json.utf8))
    }

    private static func assistRequest(
        model: String = "gpt-test",
        effort: String = "low",
        transcript: String = "segment 2\nsegment 3\nsegment 4\nsegment 5"
    ) -> AssistRequest {
        var request = AssistRequest(
            sessionId: UUID(),
            instruction: "Answer briefly",
            memory: SessionMemory(
                summary: "Earlier context",
                facts: ["A fact"],
                openQuestions: [],
                throughSegmentId: UUID()
            ),
            transcript: transcript,
            partialTranscript: "latest words"
        )
        request.assistant = AssistantConfiguration(model: model, reasoningEffort: effort, provider: .codex)
        return request
    }

    private static func summaryRequest() -> SummaryRequest {
        SummaryRequest(
            sessionId: UUID(),
            assistant: AssistantConfiguration(model: "gpt-test", reasoningEffort: "low", provider: .codex),
            memory: SessionMemory(summary: "Earlier context"),
            transcript: "every segment, one per line"
        )
    }

    /// The assist reply of protocol 8.3 with this text in place of the marker's `null`.
    private static func assistReply(marker: String) -> String {
        let fixture = CoreFixtures.assistReplyJSON
        let replaced = fixture.replacingOccurrences(of: "\"throughSegmentId\":null", with: "\"throughSegmentId\":" + marker)
        precondition(replaced != fixture || marker == "null", "The fixture reply has no marker to replace.")
        return replaced
    }

    /// The assist reply of protocol 8.3 with this `execution` selection.
    private static func assistReply(model: String, effort: String) -> String {
        "{\"detectedQuestion\":\"Q\",\"answer\":\"A\",\"details\":\"D\","
            + "\"memory\":{\"summary\":\"S\",\"facts\":[],\"openQuestions\":[],\"throughSegmentId\":null},"
            + "\"execution\":{\"model\":\"\(model)\",\"reasoningEffort\":\"\(effort)\","
            + "\"codexMs\":1,\"relayTotalMs\":2,\"requestReadMs\":0.5,\"relayOverheadMs\":1}}"
    }

    private static func caught(_ call: () async throws -> Void) async -> (any Error)? {
        do {
            try await call()
            return nil
        } catch {
            return error
        }
    }

    // MARK: - Success

    func testAssistReturnsTheReplyOfProtocol8_3() async throws {
        let transport = FakeHTTPTransport(replies: [Self.reply(200, CoreFixtures.assistReplyJSON)])
        let client = RelayClient(transport: transport)
        let request = Self.assistRequest()

        let response = try await client.assist(request, pairing: CoreFixtures.pairing())

        XCTAssertEqual(response.detectedQuestion, "What is the main advantage of local transcription?")
        XCTAssertEqual(response.answer, "Your audio stays on the phone.")
        XCTAssertEqual(response.details, "")
        XCTAssertEqual(response.memory, SessionMemory(summary: "Earlier context", facts: ["A fact"], openQuestions: []))
        XCTAssertEqual(
            response.execution,
            RelayExecution(
                model: "gpt-test",
                reasoningEffort: "low",
                codexMs: 600.1,
                relayTotalMs: 620.4,
                requestReadMs: 2.0,
                relayOverheadMs: 20.3
            )
        )
        XCTAssertNil(response.execution?.usage)

        let requests = await transport.requests
        XCTAssertEqual(requests.count, 1)
        XCTAssertEqual(requests.first?.method, "POST")
        XCTAssertEqual(requests.first?.url.absoluteString, "https://pc.example.test:11443/v1/assist")
    }

    // MARK: - P2-209, core half: the marker of the reply is never read

    func testTheReplyDecodesWhateverItsMarkerIsAndTheReturnedMarkerIsNil() async throws {
        let markers = [
            "null",
            "\"0B5C1C7A-51E2-4D0B-9D43-2F1E7A9C4D11\"",
            "\"0b5c1c7a-51e2-4d0b-9d43-2f1e7a9c4d11\"",
            "\"not-a-uuid\"",
            "\"\"",
            "17",
            "{\"id\":\"x\"}"
        ]
        for marker in markers {
            let transport = FakeHTTPTransport(replies: [Self.reply(200, Self.assistReply(marker: marker))])
            let client = RelayClient(transport: transport)

            let response = try await client.assist(Self.assistRequest(), pairing: CoreFixtures.pairing())

            XCTAssertNil(response.memory.throughSegmentId, marker)
            XCTAssertEqual(response.memory.summary, "Earlier context", marker)
            XCTAssertEqual(response.memory.facts, ["A fact"], marker)
            XCTAssertEqual(response.answer, "Your audio stays on the phone.", marker)
        }
    }

    func testAReplyWithoutAMarkerMemberDecodes() async throws {
        let reply = CoreFixtures.assistReplyJSON.replacingOccurrences(of: ",\"throughSegmentId\":null", with: "")
        XCTAssertNotEqual(reply, CoreFixtures.assistReplyJSON)
        let client = RelayClient(transport: FakeHTTPTransport(replies: [Self.reply(200, reply)]))

        let response = try await client.assist(Self.assistRequest(), pairing: CoreFixtures.pairing())

        XCTAssertNil(response.memory.throughSegmentId)
        XCTAssertEqual(response.memory.facts, ["A fact"])
    }

    func testAReplyThatIsNotTheSuccessShapeIsUnreadable() async throws {
        let bodies = [
            "",
            "not json",
            "[]",
            // No `answer`.
            #"{"detectedQuestion":"Q","details":"","memory":{"summary":"","facts":[],"openQuestions":[]},"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":1,"relayTotalMs":2,"requestReadMs":1,"relayOverheadMs":1}}"#,
            // `memory` is not an object.
            #"{"detectedQuestion":"Q","answer":"A","details":"","memory":"none","execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":1,"relayTotalMs":2,"requestReadMs":1,"relayOverheadMs":1}}"#,
            // `memory.facts` is missing.
            #"{"detectedQuestion":"Q","answer":"A","details":"","memory":{"summary":"","openQuestions":[],"throughSegmentId":null},"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":1,"relayTotalMs":2,"requestReadMs":1,"relayOverheadMs":1}}"#
        ]
        for body in bodies {
            let transport = FakeHTTPTransport(replies: [Self.reply(200, body)])
            let client = RelayClient(transport: transport)

            let error = await Self.caught { _ = try await client.assist(Self.assistRequest(), pairing: CoreFixtures.pairing()) }

            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.unreadable), body)
            let count = await transport.requests.count
            XCTAssertEqual(count, 1, body)
        }
    }

    // MARK: - P2-210: the echo

    func testAReplyThatEchoesTheSelectionIsAccepted() async throws {
        let client = RelayClient(transport: FakeHTTPTransport(replies: [
            Self.reply(200, Self.assistReply(model: "gpt-5.6-sol", effort: "none"))
        ]))

        let response = try await client.assist(
            Self.assistRequest(model: "gpt-5.6-sol", effort: "none"),
            pairing: CoreFixtures.pairing()
        )

        XCTAssertEqual(response.answer, "A")
        XCTAssertEqual(response.execution?.model, "gpt-5.6-sol")
        XCTAssertEqual(response.execution?.reasoningEffort, "none")
    }

    func testAReplyThatNamesAnotherModelOrEffortIsNotConfirmed() async throws {
        let replies = [
            Self.assistReply(model: "gpt-other", effort: "low"),
            Self.assistReply(model: "gpt-test", effort: "high"),
            Self.assistReply(model: "GPT-TEST", effort: "low"),
            Self.assistReply(model: "", effort: ""),
            // No `execution` at all.
            #"{"detectedQuestion":"Q","answer":"A","details":"D","memory":{"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":null}}"#
        ]
        for reply in replies {
            let transport = FakeHTTPTransport(replies: [Self.reply(200, reply)])
            let client = RelayClient(transport: transport)

            let error = await Self.caught { _ = try await client.assist(Self.assistRequest(), pairing: CoreFixtures.pairing()) }

            let failure = try XCTUnwrap(error as? RelayFailure, reply)
            XCTAssertTrue(failure.isLocal(.notConfirmed), reply)
            XCTAssertEqual(
                RelayMessages.text(for: failure),
                "The PC did not confirm the selected model. Restart the updated LiveCue Desktop app, then retry.",
                reply
            )
            // The answer was read, so there is nothing to cancel and nothing to retry.
            let count = await transport.requests.count
            XCTAssertEqual(count, 1, reply)
        }
    }

    func testAssistWithoutASelectionSendsNothing() async throws {
        let transport = FakeHTTPTransport(replies: [Self.reply(200, CoreFixtures.assistReplyJSON)])
        let client = RelayClient(transport: transport)
        let request = AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: "Hello")
        XCTAssertNil(request.assistant)

        let error = await Self.caught { _ = try await client.assist(request, pairing: CoreFixtures.pairing()) }

        XCTAssertEqual((error as? RelayFailure)?.wireCode, .invalidAssistant)
        let count = await transport.requests.count
        XCTAssertEqual(count, 0)
    }

    // MARK: - The body

    func testTheBodySentIsTheEncodedRequest() async throws {
        let transport = FakeHTTPTransport(replies: [Self.reply(200, CoreFixtures.assistReplyJSON)])
        let client = RelayClient(transport: transport)
        let request = Self.assistRequest()

        _ = try await client.assist(request, pairing: CoreFixtures.pairing())

        let requests = await transport.requests
        let sent = try XCTUnwrap(requests.first?.body)
        // A request inside the limit is sent as it is.
        XCTAssertEqual(sent, try WireBudget.encodedBody(request))
        XCTAssertEqual(try LiveCueJSON.decode(AssistRequest.self, from: sent), request)

        let body = try XCTUnwrap(JSONValue.parse(sent))
        XCTAssertEqual(body["requestId"]?.stringValue, request.requestId.uuidString)
        XCTAssertEqual(body["sessionId"]?.stringValue, request.sessionId.uuidString)
        XCTAssertEqual(
            body["assistant"],
            JSONValue.object([
                "model": .string("gpt-test"),
                "reasoningEffort": .string("low"),
                "provider": .string("codex")
            ])
        )
        XCTAssertEqual(body["instruction"]?.stringValue, "Answer briefly")
        XCTAssertEqual(body["transcript"]?.stringValue, "segment 2\nsegment 3\nsegment 4\nsegment 5")
        XCTAssertEqual(body["partialTranscript"]?.stringValue, "latest words")
        XCTAssertEqual(body["memory"]?["summary"]?.stringValue, "Earlier context")
        XCTAssertEqual(body["memory"]?["throughSegmentId"]?.stringValue, request.memory.throughSegmentId?.uuidString)
    }

    func testTheBodySentIsTheFittedBody() async throws {
        // Far over C-11: only a fitted body can be sent. The transcript is a character that JSON escapes
        // to six bytes, and the other members that step 3 of protocol 13.4 shrinks are large too.
        var request = Self.assistRequest(transcript: String(repeating: "\u{1}", count: 200_000))
        request.memory.summary = String(repeating: "\"", count: 30_000)
        request.partialTranscript = String(repeating: "\u{1F600}", count: 10_000)
        XCTAssertGreaterThan(try LiveCueJSON.encode(request).count, RelayConstants.maximumEncodedBodyBytes)
        let fitted = try WireBudget.fitted(request)
        let expected = try WireBudget.encodedBody(fitted)

        let transport = FakeHTTPTransport(replies: [Self.reply(200, CoreFixtures.assistReplyJSON)])
        let client = RelayClient(transport: transport)

        _ = try await client.assist(request, pairing: CoreFixtures.pairing())

        let requests = await transport.requests
        let sent = try XCTUnwrap(requests.first?.body)
        XCTAssertEqual(sent, expected)
        XCTAssertLessThanOrEqual(sent.count, RelayConstants.maximumEncodedBodyBytes)
        // The request id survives the fitting: it is the handle of the cancel.
        XCTAssertEqual(JSONValue.parse(sent)?["requestId"]?.stringValue, request.requestId.uuidString)
    }

    func testTheSummaryBodySentIsTheFittedBody() async throws {
        var request = Self.summaryRequest()
        request.transcript = String(repeating: "\"", count: 300_000)
        XCTAssertGreaterThan(try LiveCueJSON.encode(request).count, RelayConstants.maximumEncodedBodyBytes)
        let fitted = try WireBudget.fitted(request)
        let expected = try WireBudget.encodedBody(fitted)

        let transport = FakeHTTPTransport(replies: [Self.reply(200, CoreFixtures.summaryReplyJSON)])
        let client = RelayClient(transport: transport)

        _ = try await client.summarize(request, pairing: CoreFixtures.pairing())

        let requests = await transport.requests
        let sent = try XCTUnwrap(requests.first?.body)
        XCTAssertEqual(sent, expected)
        XCTAssertLessThanOrEqual(sent.count, RelayConstants.maximumEncodedBodyBytes)
        XCTAssertEqual(JSONValue.parse(sent)?["transcriptTruncated"]?.boolValue, true)
    }

    // MARK: - P2-212: the one cancel

    func testOneDeleteIsSentWhenThePhoneTimeoutOfAssistPasses() async throws {
        let time = ManualTime()
        let transport = FakeHTTPTransport(replies: [.never, Self.reply(200, Self.cancelledJSON)])
        await transport.setDefault(Self.reply(200, Self.healthJSON))
        let client = RelayClient(transport: transport, timing: time.timing)
        let pairing = CoreFixtures.pairing("https://pc.example.test:10000/livecue/")
        let request = Self.assistRequest()
        let task = Task { try await client.assist(request, pairing: pairing) }

        await transport.waitForRequests(count: 1)
        await time.waitForSleepers()
        await time.advance(by: 69)
        XCTAssertEqual(time.sleeperCount, 1)
        await time.advance(by: 1)

        do {
            _ = try await task.value
            XCTFail("A relay that never answers gives no answer.")
        } catch {
            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.timeout))
        }

        await transport.waitForRequests(count: 2)
        // One more request through the same transport: a second cancel would have to come before it.
        _ = try await client.health(pairing)

        let requests = await transport.requests
        XCTAssertEqual(requests.map(\.method), ["POST", "DELETE", "GET"])
        XCTAssertEqual(
            requests.map(\.url.absoluteString),
            [
                "https://pc.example.test:10000/livecue/v1/assist",
                "https://pc.example.test:10000/livecue/v1/requests/" + request.requestId.uuidString,
                "https://pc.example.test:10000/livecue/v1/health"
            ]
        )
        let cancel = requests[1]
        XCTAssertEqual(cancel.headers["Authorization"], "Bearer " + CoreFixtures.token)
        XCTAssertEqual(cancel.headers["X-LiveCue-Protocol"], "2")
        XCTAssertNil(cancel.body)
        XCTAssertEqual(cancel.timeout, 18)
    }

    func testOneDeleteIsSentWhenThePhoneTimeoutOfSummaryPasses() async throws {
        let time = ManualTime()
        // The cancel finds nothing to cancel: that is not an error either.
        let transport = FakeHTTPTransport(replies: [.never, Self.reply(404, Self.notFoundJSON)])
        await transport.setDefault(Self.reply(200, Self.healthJSON))
        let client = RelayClient(transport: transport, timing: time.timing)
        let pairing = CoreFixtures.pairing()
        let request = Self.summaryRequest()
        let task = Task { try await client.summarize(request, pairing: pairing) }

        await transport.waitForRequests(count: 1)
        await time.waitForSleepers()
        await time.advance(by: 70)

        do {
            _ = try await task.value
            XCTFail("A relay that never answers gives no summary.")
        } catch {
            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.timeout))
        }

        await transport.waitForRequests(count: 2)
        _ = try await client.health(pairing)

        let requests = await transport.requests
        XCTAssertEqual(requests.map(\.method), ["POST", "DELETE", "GET"])
        XCTAssertEqual(
            requests[1].url.absoluteString,
            "https://pc.example.test:11443/v1/requests/" + request.requestId.uuidString
        )
    }

    func testOneDeleteIsSentWhenTheTaskOfAnAssistIsCancelled() async throws {
        let transport = FakeHTTPTransport(replies: [.never, Self.reply(404, Self.notFoundJSON)])
        await transport.setDefault(Self.reply(200, Self.healthJSON))
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()
        let request = Self.assistRequest()
        let task = Task { try await client.assist(request, pairing: pairing) }

        await transport.waitForRequests(count: 1)
        task.cancel()

        do {
            _ = try await task.value
            XCTFail("A cancelled assist gives no answer.")
        } catch {
            // Row 1 of protocol 13.6: cancellation, never an error to show.
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }

        await transport.waitForRequests(count: 2)
        _ = try await client.health(pairing)

        let requests = await transport.requests
        XCTAssertEqual(requests.map(\.method), ["POST", "DELETE", "GET"])
        XCTAssertEqual(
            requests[1].url.absoluteString,
            "https://pc.example.test:11443/v1/requests/" + request.requestId.uuidString
        )
    }

    func testOneDeleteIsSentWhenTheTaskOfASummaryIsCancelled() async throws {
        // What deleting a session does while its summary is in flight: the summary's task is cancelled.
        let transport = FakeHTTPTransport(replies: [.never, Self.reply(200, Self.cancelledJSON)])
        await transport.setDefault(Self.reply(200, Self.healthJSON))
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()
        let request = Self.summaryRequest()
        let task = Task { try await client.summarize(request, pairing: pairing) }

        await transport.waitForRequests(count: 1)
        task.cancel()

        do {
            _ = try await task.value
            XCTFail("A cancelled summary gives no summary.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }

        await transport.waitForRequests(count: 2)
        _ = try await client.health(pairing)

        let requests = await transport.requests
        XCTAssertEqual(requests.map(\.method), ["POST", "DELETE", "GET"])
        XCTAssertEqual(
            requests[1].url.absoluteString,
            "https://pc.example.test:11443/v1/requests/" + request.requestId.uuidString
        )
    }

    func testACancelThatFailsChangesNothingForTheCaller() async throws {
        // The cancel is best effort: the PC may be gone, which is often why the timeout passed.
        let time = ManualTime()
        let transport = FakeHTTPTransport(replies: [.never, .failure(.unreachable)])
        await transport.setDefault(Self.reply(200, Self.healthJSON))
        let client = RelayClient(transport: transport, timing: time.timing)
        let pairing = CoreFixtures.pairing()
        let request = Self.assistRequest()
        let task = Task { try await client.assist(request, pairing: pairing) }

        await transport.waitForRequests(count: 1)
        await time.waitForSleepers()
        await time.advance(by: 70)

        do {
            _ = try await task.value
            XCTFail("A relay that never answers gives no answer.")
        } catch {
            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.timeout))
        }
        await transport.waitForRequests(count: 2)
        _ = try await client.health(pairing)

        let requests = await transport.requests
        XCTAssertEqual(requests.map(\.method), ["POST", "DELETE", "GET"])
    }

    func testNoDeleteIsSentAfterAnAnswerOrAFailureThatWasRead() async throws {
        let outcomes: [FakeHTTPTransport.Reply] = [
            Self.reply(200, CoreFixtures.assistReplyJSON),
            // Read like any other code (protocol 8.5): the phone did not cancel this one itself.
            Self.reply(409, #"{"error":"The request was cancelled.","code":"REQUEST_CANCELLED"}"#),
            Self.reply(504, #"{"error":"The local assistant request timed out.","code":"ASSISTANT_TIMEOUT"}"#),
            Self.reply(429, #"{"error":"The PC is processing another request. Try again shortly.","code":"ASSISTANT_BUSY"}"#),
            Self.reply(500, #"{"error":"The local assistant request failed.","code":"ASSISTANT_FAILED"}"#),
            .failure(.unreachable),
            .failure(.timedOut)
        ]
        let expected: [RelayFailure.Kind?] = [
            nil,
            .wire("REQUEST_CANCELLED"),
            .wire("ASSISTANT_TIMEOUT"),
            .wire("ASSISTANT_BUSY"),
            .wire("ASSISTANT_FAILED"),
            .local(.unreachable),
            .local(.unreachable)
        ]
        let transport = FakeHTTPTransport(replies: outcomes)
        await transport.setDefault(Self.reply(200, Self.healthJSON))
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()

        for (index, kind) in expected.enumerated() {
            let error = await Self.caught { _ = try await client.assist(Self.assistRequest(), pairing: pairing) }
            XCTAssertEqual((error as? RelayFailure)?.kind, kind, "Outcome \(index)")
        }
        _ = try await client.health(pairing)

        let requests = await transport.requests
        XCTAssertEqual(requests.map(\.method), Array(repeating: "POST", count: outcomes.count) + ["GET"])
        XCTAssertEqual(
            RelayMessages.text(for: RelayFailure(kind: .wire("REQUEST_CANCELLED"), origin: .http, httpStatus: 409)),
            "This request was cancelled."
        )
    }
}
