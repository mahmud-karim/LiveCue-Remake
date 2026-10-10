import Foundation
@testable import LiveCueCore
import XCTest

/// One test per row of protocol 13.6 (P2-205), through the client with a fake transport, and the pure
/// classification function on its own.
final class RelayClientClassificationTests: XCTestCase {
    // MARK: - Fixtures

    private static let healthJSON = #"{"ok":true,"protocol":2,"relayVersion":"1.0.0"}"#

    private static func reply(_ status: Int, _ body: String, headers: [String: String] = [:]) -> FakeHTTPTransport.Reply {
        .response(status: status, headers: headers, body: Data(body.utf8))
    }

    /// The error of one health check against a relay that gives this reply.
    private static func healthError(for reply: FakeHTTPTransport.Reply) async -> (any Error)? {
        let client = RelayClient(transport: FakeHTTPTransport(replies: [reply]))
        do {
            _ = try await client.health(CoreFixtures.pairing())
            return nil
        } catch {
            return error
        }
    }

    private static func classified(_ status: Int, _ body: String, route: RelayRoute = .health) -> Result<Data, RelayFailure> {
        RelayOutcome.classify(route: route, result: .success((HTTPResponseHead(status: status), Data(body.utf8))))
    }

    private static func failure(_ result: Result<Data, RelayFailure>) -> RelayFailure? {
        if case .failure(let failure) = result {
            return failure
        }
        return nil
    }

    // MARK: - Row 1: cancellation

    func testRow1ACancelledTaskEndsInCancellationNotInAnError() async throws {
        let transport = FakeHTTPTransport(replies: [.never])
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()
        let task = Task { try await client.health(pairing) }

        await transport.waitForRequests(count: 1)
        task.cancel()

        do {
            _ = try await task.value
            XCTFail("A cancelled call does not return a value.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
            XCTAssertNil(error as? RelayFailure)
        }
        let count = await transport.requests.count
        XCTAssertEqual(count, 1)
    }

    func testRow1CancellationReportedByTheTransportIsCancellation() async throws {
        let error = await Self.healthError(for: .failure(.cancelled))

        XCTAssertTrue(error is CancellationError, "Unexpected error: \(String(describing: error))")
    }

    func testRow1ATaskCancelledBeforeTheCallSendsNothing() async throws {
        let transport = FakeHTTPTransport(replies: [Self.reply(200, Self.healthJSON)])
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()
        let task = Task { () async throws -> RelayHealth in
            withUnsafeCurrentTask { $0?.cancel() }
            return try await client.health(pairing)
        }

        do {
            _ = try await task.value
            XCTFail("A cancelled call does not return a value.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }
        let count = await transport.requests.count
        XCTAssertEqual(count, 0)
    }

    // MARK: - Row 2: the endpoint forms no URL

    func testRow2AnEndpointThatFormsNoURLIsABadEndpoint() throws {
        // A pairing is always valid, so its endpoint always forms a URL. The row is the client's answer
        // to the one input it cannot get from a pairing: no URL.
        XCTAssertThrowsError(try RelayClient.makeRequest(route: .health, url: nil, token: CoreFixtures.token)) { error in
            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.badEndpoint))
        }
        XCTAssertEqual(RelayMessages.text(for: RelayFailure.local(.badEndpoint)), "The relay address is invalid.")

        // And with a URL, the request of protocol 13.2.
        let url = try XCTUnwrap(CoreFixtures.pairing().relayEndpoint.url(for: .assist))
        let request = try RelayClient.makeRequest(route: .assist, url: url, token: CoreFixtures.token, body: Data("{}".utf8))
        XCTAssertEqual(request.url.absoluteString, "https://pc.example.test:11443/v1/assist")
        XCTAssertEqual(request.method, "POST")
        XCTAssertEqual(request.body, Data("{}".utf8))
        XCTAssertEqual(request.timeout, 80)
    }

    func testRow2EveryRouteOfAValidPairingFormsAURL() throws {
        let endpoints = [
            "https://pc.example.test/",
            "https://pc.example.test:11443/",
            "https://pc.example.test:10000/livecue/",
            "https://a-1.b-2.example.test:65535/a.b/c_d/e~f/g-h/"
        ]
        let routes: [RelayRoute] = [
            .health, .pairVerify, .models, .localModel, .localModelStart, .localModelStop,
            .assist, .sessionSummary, .cancel(requestId: UUID().uuidString)
        ]
        for endpoint in endpoints {
            let pairing = CoreFixtures.pairing(endpoint)
            for route in routes {
                let url = pairing.relayEndpoint.url(for: route)
                XCTAssertEqual(url?.absoluteString, endpoint + String(route.path.dropFirst()), endpoint)
                XCTAssertNoThrow(try RelayClient.makeRequest(route: route, url: url, token: pairing.token), endpoint)
            }
        }
    }

    // MARK: - Row 3: the phone timeout

    func testRow3ThePhoneTimeoutOfTheRouteIsATimeout() async throws {
        let time = ManualTime()
        let transport = FakeHTTPTransport(replies: [.never])
        let client = RelayClient(transport: transport, timing: time.timing)
        let pairing = CoreFixtures.pairing()
        let task = Task { try await client.health(pairing) }

        await transport.waitForRequests(count: 1)
        await time.waitForSleepers()
        // One second short of the 8 s of protocol 6.1: the call is still waiting.
        await time.advance(by: 7)
        XCTAssertEqual(time.sleeperCount, 1)
        await time.advance(by: 1)

        do {
            _ = try await task.value
            XCTFail("A relay that never answers does not return a value.")
        } catch {
            // Row 3, although the transport then reported cancellation: it was the timer that stopped it.
            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.timeout))
            XCTAssertEqual(
                (error as? RelayFailure).map { RelayMessages.text(for: $0) },
                "Your PC did not answer in time. Check that LiveCue Desktop is running, then retry."
            )
        }
        XCTAssertEqual(time.sleeperCount, 0)
        let count = await transport.requests.count
        XCTAssertEqual(count, 1)
    }

    func testRow3EveryRouteWaitsItsOwnTime() async throws {
        let pairing = CoreFixtures.pairing()
        let waits: [(name: String, seconds: TimeInterval, call: @Sendable (RelayClient) async throws -> Void)] = [
            ("verify", 15, { try await $0.verifyPairing(pairing) }),
            ("models", 45, { _ = try await $0.models(pairing) }),
            ("status", 8, { _ = try await $0.localModelStatus(pairing) }),
            ("start", 15, { _ = try await $0.startLocalModel(.nemotron, pairing: pairing) }),
            ("stop", 15, { _ = try await $0.stopLocalModel(.qwen3, pairing: pairing) })
        ]
        for wait in waits {
            let time = ManualTime()
            let transport = FakeHTTPTransport(replies: [.never])
            let client = RelayClient(transport: transport, timing: time.timing)
            let call = wait.call
            let task = Task { try await call(client) }

            await transport.waitForRequests(count: 1)
            await time.waitForSleepers()
            await time.advance(by: wait.seconds - 1)
            XCTAssertEqual(time.sleeperCount, 1, wait.name)
            await time.advance(by: 1)

            do {
                try await task.value
                XCTFail("\(wait.name) returned without an answer.")
            } catch {
                XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.timeout), wait.name)
            }
            XCTAssertEqual(time.now, wait.seconds, wait.name)
        }
    }

    func testRow3AnAnswerInsideTheTimeStopsTheTimer() async throws {
        let time = ManualTime()
        let transport = FakeHTTPTransport(replies: [Self.reply(200, Self.healthJSON)])
        let client = RelayClient(transport: transport, timing: time.timing)

        let health = try await client.health(CoreFixtures.pairing())

        XCTAssertEqual(health.relayVersion, "1.0.0")
        XCTAssertEqual(time.sleeperCount, 0)
    }

    // MARK: - Rows 4 to 6: no HTTP response

    func testRow4ATLSFailureIsLTLS() async throws {
        let error = await Self.healthError(for: .failure(.tls))

        XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.tls))
        XCTAssertNil((error as? RelayFailure)?.httpStatus)
    }

    func testRow5AnUnreachablePCAndATransportTimeoutAreLUnreachable() async throws {
        for failure in [TransportFailure.unreachable, TransportFailure.timedOut] {
            let error = await Self.healthError(for: .failure(failure))

            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.unreachable), "\(failure)")
        }
    }

    func testRow6AnyOtherTransportFailureIsLNetwork() async throws {
        let error = await Self.healthError(for: .failure(.other))

        XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.network))
    }

    // MARK: - Rows 7 and 8: a 2xx reply

    func testRow7A2xxReplyOfTheSuccessShapeIsSuccess() async throws {
        let client = RelayClient(transport: FakeHTTPTransport(replies: [Self.reply(200, Self.healthJSON)]))

        let health = try await client.health(CoreFixtures.pairing())

        XCTAssertEqual(health, RelayHealth(relayVersion: "1.0.0"))
    }

    func testRow8A2xxReplyOfAnotherShapeIsUnreadable() async throws {
        for (status, body) in [(200, "<html>ok</html>"), (200, ""), (204, ""), (200, "[1,2]"), (201, #"{"ok":true,"protocol":2}"#)] {
            let error = await Self.healthError(for: Self.reply(status, body))

            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.unreadable), "\(status) \(body)")
        }
        // Also when the body carries a code: a 2xx is never read as an error reply.
        let error = await Self.healthError(for: Self.reply(200, #"{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}"#))
        XCTAssertEqual((error as? RelayFailure)?.wireCode, .protocolMismatch)
    }

    // MARK: - Row 9: a status with a code

    func testRow9AnErrorStatusWithACodeIsThatWireCode() async throws {
        let replies: [(status: Int, code: RelayWireCode, text: String)] = [
            (401, .unauthorized, "Invalid pairing token."),
            (404, .notFound, "Endpoint not found."),
            (400, .protocolMismatch, "Protocol version mismatch."),
            (429, .tooManyRequests, "Too many requests in progress."),
            (503, .relayPaused, "PC relay is paused. Resume it in LiveCue Desktop."),
            (504, .deadlineExceeded, "The PC took too long to answer."),
            (500, .internalError, "The PC relay hit an internal error."),
            // Read like any other code (protocol 8.5).
            (409, .requestCancelled, "The request was cancelled.")
        ]
        for reply in replies {
            let body = "{\"error\":\"\(reply.text)\",\"code\":\"\(reply.code.rawValue)\"}"
            let error = await Self.healthError(for: Self.reply(reply.status, body))

            let failure = try XCTUnwrap(error as? RelayFailure, reply.code.rawValue)
            XCTAssertEqual(failure.kind, RelayFailure.Kind.wire(reply.code.rawValue))
            XCTAssertEqual(failure.wireCode, reply.code)
            XCTAssertTrue(failure.isCode(reply.code))
            XCTAssertEqual(failure.origin, .http)
            XCTAssertEqual(failure.httpStatus, reply.status)
            XCTAssertEqual(failure.relayText, reply.text)
            // The phone's own text, never the relay's.
            XCTAssertEqual(RelayMessages.text(for: failure), RelayMessages.text(for: reply.code))
        }
    }

    func testRow9ACodeThisBuildDoesNotKnowIsKeptWithTheRelaysText() async throws {
        let error = await Self.healthError(
            for: Self.reply(418, #"{"error":"The PC is making tea.","code":"TEAPOT_BUSY","extra":[1,2]}"#)
        )

        let failure = try XCTUnwrap(error as? RelayFailure)
        XCTAssertEqual(failure.kind, RelayFailure.Kind.wire("TEAPOT_BUSY"))
        XCTAssertNil(failure.wireCode)
        XCTAssertEqual(failure.httpStatus, 418)
        XCTAssertEqual(failure.relayText, "The PC is making tea.")
        XCTAssertEqual(RelayMessages.text(for: failure), "The PC is making tea.")

        // Without a relay text, the status is what is shown.
        let bare = await Self.healthError(for: Self.reply(418, #"{"code":"TEAPOT_BUSY"}"#))
        let bareFailure = try XCTUnwrap(bare as? RelayFailure)
        XCTAssertNil(bareFailure.relayText)
        XCTAssertEqual(RelayMessages.text(for: bareFailure), "Relay error (418).")
    }

    func testRow9TheCodeWinsOverTheStatus() async throws {
        // A 503 with a code is the relay speaking, not the edge proxy.
        let paused = await Self.healthError(
            for: Self.reply(503, #"{"error":"PC relay is paused. Resume it in LiveCue Desktop.","code":"RELAY_PAUSED"}"#)
        )
        XCTAssertEqual((paused as? RelayFailure)?.wireCode, .relayPaused)
        XCTAssertEqual((paused as? RelayFailure)?.isLocal(.noRelay), false)

        // A 401 with another code keeps that code.
        let other = await Self.healthError(for: Self.reply(401, #"{"error":"Later.","code":"TOKEN_EXPIRED"}"#))
        XCTAssertEqual((other as? RelayFailure)?.kind, RelayFailure.Kind.wire("TOKEN_EXPIRED"))
    }

    // MARK: - Rows 10 to 12: a status without a code

    func testRow10A401WithoutACodeIsUnauthorized() async throws {
        for body in ["", "<html>401</html>", "{}", #"{"code":7}"#, #"["UNAUTHORIZED"]"#, #"{"error":"no"}"#] {
            let error = await Self.healthError(for: Self.reply(401, body))

            let failure = try XCTUnwrap(error as? RelayFailure, body)
            XCTAssertTrue(failure.isCode(.unauthorized), body)
            XCTAssertEqual(failure.httpStatus, 401, body)
            XCTAssertEqual(
                RelayMessages.text(for: failure),
                "The PC no longer recognizes this pairing. Scan its pairing QR once to reconnect.",
                body
            )
        }
    }

    func testRow11A502A503OrA504WithoutACodeIsNoRelay() async throws {
        for status in [502, 503, 504] {
            for body in ["", "<html>Bad Gateway</html>", #"{"message":"upstream"}"#] {
                let error = await Self.healthError(for: Self.reply(status, body))

                let failure = try XCTUnwrap(error as? RelayFailure, "\(status)")
                XCTAssertTrue(failure.isLocal(.noRelay), "\(status) \(body)")
                XCTAssertEqual(failure.httpStatus, status)
                XCTAssertEqual(
                    RelayMessages.text(for: failure),
                    "Your PC answered, but the LiveCue relay is not running. Open LiveCue Desktop and start the relay."
                )
            }
        }
    }

    func testRow12AnyOtherStatusIsLHTTPWithTheStatus() async throws {
        for status in [100, 302, 400, 403, 404, 408, 413, 429, 500, 501, 505, 599] {
            let error = await Self.healthError(for: Self.reply(status, "<html></html>"))

            let failure = try XCTUnwrap(error as? RelayFailure, "\(status)")
            XCTAssertEqual(failure, RelayFailure.local(.http, httpStatus: status))
            XCTAssertEqual(RelayMessages.text(for: failure), "Relay error (\(status)).")
        }
    }

    // MARK: - The classification function

    func testClassifyMapsEveryTransportFailure() {
        let expected: [(TransportFailure, RelayLocalCondition)] = [
            (.tls, .tls),
            (.unreachable, .unreachable),
            (.timedOut, .unreachable),
            (.other, .network),
            // The client reads this one as row 1 before it classifies; on its own it is "anything else".
            (.cancelled, .network)
        ]
        for (failure, condition) in expected {
            let result = RelayOutcome.classify(route: .models, result: .failure(failure))

            XCTAssertEqual(Self.failure(result), RelayFailure.local(condition), "\(failure)")
        }
    }

    func testClassifyReturnsTheBytesOfEvery2xxReply() throws {
        for status in [200, 201, 202, 204, 299] {
            let result = Self.classified(status, "payload", route: .localModelStart)

            XCTAssertEqual(try result.get(), Data("payload".utf8), "\(status)")
        }
        XCTAssertNotNil(Self.failure(Self.classified(199, "payload")))
        XCTAssertNotNil(Self.failure(Self.classified(300, "payload")))
    }

    func testClassifyReadsTheRelaysTextFromErrorOrMessage() {
        let fromError = Self.failure(Self.classified(400, #"{"error":"A","message":"B","code":"X_CODE"}"#))
        XCTAssertEqual(fromError?.relayText, "A")

        let fromMessage = Self.failure(Self.classified(400, #"{"message":"B","code":"X_CODE"}"#))
        XCTAssertEqual(fromMessage?.relayText, "B")

        let neither = Self.failure(Self.classified(400, #"{"error":7,"code":"X_CODE"}"#))
        XCTAssertEqual(neither?.kind, RelayFailure.Kind.wire("X_CODE"))
        XCTAssertNil(neither?.relayText)
    }

    func testClassifyTakesBothAnswersOfTheCancelRouteAsDone() throws {
        let notFound = #"{"error":"No request with that id is in progress.","code":"REQUEST_NOT_FOUND"}"#
        let cancel = RelayRoute.cancel(requestId: "6F9619FF-8B86-4011-B42D-00C04FC964FF")

        // Protocol 6.8: "nothing more to do", for the cancel route only.
        XCTAssertNoThrow(try Self.classified(200, #"{"ok":true,"cancelled":true}"#, route: cancel).get())
        XCTAssertNoThrow(try Self.classified(404, notFound, route: cancel).get())
        XCTAssertEqual(Self.failure(Self.classified(404, notFound, route: .assist))?.wireCode, .requestNotFound)
        // Another 404 on the cancel route is still an error.
        let unknownRoute = Self.failure(Self.classified(404, #"{"error":"Endpoint not found.","code":"NOT_FOUND"}"#, route: cancel))
        XCTAssertEqual(unknownRoute?.wireCode, .notFound)
    }
}
