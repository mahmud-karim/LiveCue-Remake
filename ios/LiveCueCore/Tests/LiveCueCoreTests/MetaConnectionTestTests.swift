import Foundation
import LiveCueCore
import XCTest

/// The key test without audio: connect, handshake, acknowledgement, `endStream`, close (AC-IOS-110).
final class MetaConnectionTestTests: XCTestCase {
    private let key = "synthetic-meta-unit-key"
    private let acknowledgement = #"{"sessionId":"synthetic-session"}"#

    // MARK: - The exchange

    func testSendsTheHandshakeRequiresTheAcknowledgementSendsEndStreamAndCloses() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        await transport.deliver(.text(acknowledgement))

        try await MetaConnectionTest.run(key: key, transport: transport, timing: time.timing)

        let url = await transport.openedURL
        let headers = await transport.openedHeaders
        let maximum = await transport.openedMaximumMessageSize
        XCTAssertEqual(url, MetaRealtimeAPI.endpoint)
        XCTAssertEqual(url?.absoluteString, "wss://api.meta.ai/v1/asr/realtime")
        XCTAssertEqual(headers, [:], "The key is never in a header.")
        XCTAssertEqual(maximum, 131_072)

        // The handshake first, then `endStream`, and nothing else.
        let texts = await transport.sentTexts
        XCTAssertEqual(texts.count, 2)
        let handshake = try XCTUnwrap(JSONValue.parse(texts.first ?? ""))
        XCTAssertEqual(handshake["authorization"]?["accessToken"]?.stringValue, "Bearer " + key)
        XCTAssertEqual(handshake.objectValue?.count, 6)
        XCTAssertEqual(texts.last, #"{"type":"endStream"}"#)

        // No audio: not one binary message (AC-IOS-110).
        let frames = await transport.sentFrames
        XCTAssertEqual(frames, [])

        let closes = await transport.closeCalls
        XCTAssertEqual(closes, [1000])

        // The limit is not left waiting once the test is over.
        XCTAssertEqual(time.now, 0)
    }

    func testARejectedAcknowledgementIsAuthentication() async throws {
        let rejected = [
            #"{"type":"error","sessionId":"id","message":"secret"}"#,
            #"{"sessionId":""}"#,
            "{}",
            "secret is not JSON"
        ]
        for message in rejected {
            let transport = FakeWebSocketTransport()
            await transport.deliver(.text(message))

            do {
                try await MetaConnectionTest.run(key: key, transport: transport, timing: ManualTime().timing)
                XCTFail("The acknowledgement \(message) is not acceptable.")
            } catch {
                XCTAssertEqual(error as? MetaSpeechError, .authentication, message)
                XCTAssertFalse(error.localizedDescription.contains("secret"), message)
            }

            // Nothing follows the handshake, and the socket is closed.
            let texts = await transport.sentTexts
            let frames = await transport.sentFrames
            let closes = await transport.closeCalls
            XCTAssertEqual(texts.count, 1, message)
            XCTAssertEqual(frames, [], message)
            XCTAssertEqual(closes.count, 1, message)
        }
    }

    func testABinaryFirstMessageIsNotAnAcknowledgement() async throws {
        let transport = FakeWebSocketTransport()
        await transport.deliver(.data(Data(acknowledgement.utf8)))

        do {
            try await MetaConnectionTest.run(key: key, transport: transport, timing: ManualTime().timing)
            XCTFail("Only a text message can acknowledge.")
        } catch {
            XCTAssertEqual(error as? MetaSpeechError, .authentication)
        }
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
    }

    // MARK: - The 15 s limit

    func testExpiryOfFifteenSecondsIsConnection() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let timing = time.timing
        let key = self.key

        // The handshake is sent and Meta never answers.
        let test = Task { try await MetaConnectionTest.run(key: key, transport: transport, timing: timing) }
        await transport.waitForSentTexts(count: 1)
        await transport.waitForReceive()
        await time.waitForSleepers()

        await time.advance(by: 14)
        let closesBeforeTheLimit = await transport.closeCalls
        XCTAssertEqual(closesBeforeTheLimit, [], "The test keeps waiting inside its limit.")

        await time.advance(by: 1)
        do {
            try await test.value
            XCTFail("The test ends at its limit.")
        } catch {
            XCTAssertEqual(error as? MetaSpeechError, .connection)
        }

        XCTAssertEqual(time.now, 15)
        let texts = await transport.sentTexts
        let frames = await transport.sentFrames
        let closes = await transport.closeCalls
        XCTAssertEqual(texts.count, 1, "No endStream without an acknowledgement.")
        XCTAssertEqual(frames, [])
        XCTAssertEqual(closes.count, 1, "The socket is closed when the limit passes.")
    }

    func testAnOpenThatNeverAnswersEndsAtTheLimit() async throws {
        let transport = FakeWebSocketTransport(open: .never)
        let time = ManualTime()
        let timing = time.timing
        let key = self.key

        let test = Task { try await MetaConnectionTest.run(key: key, transport: transport, timing: timing) }
        await transport.waitForOpen()
        await time.waitForSleepers()
        await time.advance(by: 15)

        do {
            try await test.value
            XCTFail("The test ends at its limit.")
        } catch {
            XCTAssertEqual(error as? MetaSpeechError, .connection)
        }
        let texts = await transport.sentTexts
        XCTAssertEqual(texts, [])
    }

    // MARK: - Failures

    func testAKeyOfTheWrongShapeOpensNothing() async throws {
        let rows: [(key: String, error: MetaSpeechError)] = [
            ("", .missingKey),
            ("key with spaces", .malformedKey),
            ("key\nsecret", .malformedKey)
        ]
        for row in rows {
            let transport = FakeWebSocketTransport()
            do {
                try await MetaConnectionTest.run(key: row.key, transport: transport, timing: ManualTime().timing)
                XCTFail("The key \(row.key.debugDescription) cannot be used.")
            } catch {
                XCTAssertEqual(error as? MetaSpeechError, row.error, row.key.debugDescription)
            }
            let url = await transport.openedURL
            XCTAssertNil(url, "Nothing is opened for a key of the wrong shape.")
        }
    }

    func testEveryOtherFailureIsConnection() async throws {
        // The upgrade is refused, or the connection cannot be made.
        let refusals: [FakeWebSocketTransport.OpenResult] = [
            .rejected(status: 401), .rejected(status: 503), .failure(.tls), .failure(.unreachable), .failure(.other)
        ]
        for refusal in refusals {
            let transport = FakeWebSocketTransport(open: refusal)
            do {
                try await MetaConnectionTest.run(key: key, transport: transport, timing: ManualTime().timing)
                XCTFail("The connection cannot be made.")
            } catch {
                XCTAssertEqual(error as? MetaSpeechError, .connection)
            }
        }

        // The handshake cannot be sent.
        let silent = FakeWebSocketTransport()
        await silent.failSends()
        do {
            try await MetaConnectionTest.run(key: key, transport: silent, timing: ManualTime().timing)
            XCTFail("The handshake cannot be sent.")
        } catch {
            XCTAssertEqual(error as? MetaSpeechError, .connection)
        }
        let silentCloses = await silent.closeCalls
        XCTAssertEqual(silentCloses.count, 1)

        // The socket drops before the acknowledgement.
        let dropped = FakeWebSocketTransport()
        await dropped.drop()
        do {
            try await MetaConnectionTest.run(key: key, transport: dropped, timing: ManualTime().timing)
            XCTFail("The socket dropped.")
        } catch {
            XCTAssertEqual(error as? MetaSpeechError, .connection)
        }

        // Meta closes before the acknowledgement.
        let closed = FakeWebSocketTransport()
        await closed.deliver(.closed(code: 1008, reason: "secret"))
        do {
            try await MetaConnectionTest.run(key: key, transport: closed, timing: ManualTime().timing)
            XCTFail("The socket was closed.")
        } catch {
            XCTAssertEqual(error as? MetaSpeechError, .connection)
            XCTAssertFalse(error.localizedDescription.contains("secret"))
        }
        let closedFrames = await closed.sentFrames
        XCTAssertEqual(closedFrames, [])
    }

    func testCancellationSurfacesAsCancellation() async throws {
        let transport = FakeWebSocketTransport()
        let timing = ManualTime().timing
        let key = self.key

        let test = Task { try await MetaConnectionTest.run(key: key, transport: transport, timing: timing) }
        await transport.waitForSentTexts(count: 1)
        await transport.waitForReceive()
        test.cancel()

        do {
            try await test.value
            XCTFail("A cancelled test throws.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }
        // Cancellation closes the socket from its own task, so wait for the close to arrive.
        await transport.waitForCloseCalls()
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1, "The socket does not outlive a cancelled test.")
    }
}
