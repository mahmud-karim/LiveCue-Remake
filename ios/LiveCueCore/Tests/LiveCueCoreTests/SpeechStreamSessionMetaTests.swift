import Foundation
import LiveCueCore
import XCTest

/// A Meta speech stream end to end over a fake socket: the fixed endpoint, the handshake first, the
/// acknowledgement before any audio, 3,840-byte frames, the provider error, the completed rule
/// (IOS-A11) and the 30 s connect limit.
final class SpeechStreamSessionMetaTests: XCTestCase {
    // MARK: - Opening

    func testOpensTheFixedEndpointAndSendsTheHandshakeFirst() async throws {
        let transport = FakeWebSocketTransport()
        let session = makeSession(transport)
        await transport.deliver(.text(acknowledgement))
        try await session.start()

        // Directly to Meta: no header carries anything, and the largest inbound message is 128 KiB.
        let url = await transport.openedURL
        let headers = await transport.openedHeaders
        let maximum = await transport.openedMaximumMessageSize
        XCTAssertEqual(url?.absoluteString, "wss://api.meta.ai/v1/asr/realtime")
        XCTAssertEqual(url, MetaRealtimeAPI.endpoint)
        XCTAssertEqual(headers, [:])
        XCTAssertEqual(maximum, 131_072)

        // The first thing sent is the handshake, and it is the only place the key appears.
        let texts = await transport.sentTexts
        let frames = await transport.sentFrames
        XCTAssertEqual(texts.count, 1)
        XCTAssertEqual(frames, [])
        let handshake = try XCTUnwrap(JSONValue.parse(texts.first ?? ""))
        XCTAssertEqual(handshake["authorization"]?["accessToken"]?.stringValue, "Bearer " + metaKey)
        XCTAssertEqual(handshake["model"]?.stringValue, "muse-voice-transcribe-1.0")
        XCTAssertEqual(handshake["audioEncoding"]?.stringValue, "PCM_24KHZ")
        XCTAssertEqual(handshake.objectValue?.count, 6)

        await session.abort()
    }

    func testAKeyOfTheWrongShapeFailsBeforeAnythingIsOpened() async throws {
        let rows: [(key: String, error: MetaSpeechError)] = [
            ("", .missingKey),
            ("key with spaces", .malformedKey),
            ("key\nsecret", .malformedKey)
        ]
        for row in rows {
            let transport = FakeWebSocketTransport()
            let session = SpeechStreamSession(dialect: .meta(key: row.key), transport: transport, timing: ManualTime().timing)

            let failure = await startFailure(of: session)
            XCTAssertEqual(failure, SpeechStreamFailure(.meta(row.error)), row.key.debugDescription)
            XCTAssertEqual(failure?.message, row.error.errorDescription)

            let url = await transport.openedURL
            XCTAssertNil(url)
            let events = await allEvents(of: session)
            XCTAssertEqual(events, [])
        }
    }

    // MARK: - The acknowledgement

    func testTheAcknowledgementIsRequired() async throws {
        let rejected = [
            #"{"type":"error","sessionId":"id","message":"secret"}"#,
            #"{"sessionId":""}"#,
            "{}",
            "secret is not JSON"
        ]
        for message in rejected {
            let transport = FakeWebSocketTransport()
            let session = makeSession(transport)
            await transport.deliver(.text(message))

            let failure = await startFailure(of: session)
            XCTAssertEqual(failure, SpeechStreamFailure(.meta(.authentication)), message)
            XCTAssertEqual(
                failure?.message,
                "Meta did not accept the connection. Check the API key, account access, and billing in Transcription."
            )
            XCTAssertFalse(failure?.message.contains("secret") ?? true, message)

            // No audio, the socket closed, `events` finished.
            await session.send(audio: Data(count: 7_680))
            let frames = await transport.sentFrames
            let closes = await transport.closeCalls
            XCTAssertEqual(frames, [], message)
            XCTAssertEqual(closes.count, 1, message)
            let events = await allEvents(of: session)
            XCTAssertEqual(describe(events), ["status connecting"], message)
        }
    }

    func testABinaryFirstMessageIsNotTheAcknowledgement() async throws {
        let transport = FakeWebSocketTransport()
        let session = makeSession(transport)
        await transport.deliver(.data(Data(acknowledgement.utf8)))

        let failure = await startFailure(of: session)
        XCTAssertEqual(failure, SpeechStreamFailure(.meta(.authentication)))
    }

    func testNoAudioLeavesBeforeTheAcknowledgement() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = makeSession(transport, time: time)

        let start = Task { try await session.start() }
        await transport.waitForSentTexts(count: 1)
        await transport.waitForReceive()
        await session.send(audio: Data(count: 7_680))
        let framesBefore = await transport.sentFrames
        XCTAssertEqual(framesBefore, [], "The microphone opens only after the acknowledgement.")

        // Meta takes two seconds to answer: that is the setup time.
        await time.waitForSleepers()
        await time.advance(by: 2)
        await transport.deliver(.text(acknowledgement))
        try await start.value

        await session.send(audio: Data(count: 3_840))
        let frames = await transport.sentFrames
        XCTAssertEqual(frames.map(\.count), [3_840])

        await session.abort()
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status connected 2.0",
            "usage begin",
            "usage update sent=80.0 processed=- text=false",
            "usage finish completed=false"
        ])
    }

    // MARK: - Framing

    func testAudioIsSentIn3840ByteFramesThenARemainderThenEndStream() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        let audio = Data((0..<10_001).map { UInt8(truncatingIfNeeded: $0 &* 11) })

        await session.send(audio: audio.subdata(in: 0..<4_096))
        await session.send(audio: audio.subdata(in: 4_096..<8_192))
        await session.send(audio: audio.subdata(in: 8_192..<10_001))
        let beforeFinish = await transport.sentFrames
        XCTAssertEqual(beforeFinish.map(\.count), [3_840, 3_840])

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 2)

        // 2,321 bytes were left: one shorter frame, even, then `endStream`.
        let frames = await transport.sentFrames
        XCTAssertEqual(frames.map(\.count), [3_840, 3_840, 2_320])
        XCTAssertEqual(frames.reduce(into: Data()) { $0.append($1) }, audio.subdata(in: 0..<10_000))

        await transport.deliver(.closed(code: 1000, reason: ""))
        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)

        let texts = await transport.sentTexts
        XCTAssertEqual(texts.count, 2)
        XCTAssertEqual(texts.last, #"{"type":"endStream"}"#)
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events).filter { $0.hasPrefix("usage update sent=") }, [
            "usage update sent=80.0 processed=- text=false",
            "usage update sent=160.0 processed=- text=false",
            // 10,000 bytes at 48 bytes per millisecond.
            "usage update sent=\(10_000.0 / 48.0) processed=- text=false"
        ])
    }

    // MARK: - Events

    func testEventsBecomeCaptionsAndUsage() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = try await startedSession(transport, time: time)

        await session.send(audio: Data(count: 3_840))
        await transport.deliver(.text(#"{"type":"speechStart","turnId":2,"audioProcessedMs":0}"#))
        await transport.waitForReceive()

        await time.advance(by: 0.5)
        await transport.deliver(.text(
            #"{"type":"transcript","turnId":2,"audioProcessedMs":1234,"final":false,"transcript":"Hello synthetic-meta-unit-key","authorization":"synthetic-meta-unit-key","metadata":"synthetic-meta-unit-key"}"#
        ))
        await transport.deliver(.text(#"{"type":"audioProgress","audioProcessedMs":1500}"#))
        await transport.deliver(.text(#"{"type":"speechEnd","turnId":2}"#))
        await transport.deliver(.text(#"{"type":"unknown","transcript":"synthetic-meta-unit-key"}"#))
        await transport.deliver(.data(Data([1, 2, 3])))
        await transport.waitForReceive()

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 2)
        await transport.deliver(.text(#"{"type":"speechComplete","turnId":2,"audioProcessedMs":2000,"transcript":"Hello world."}"#))
        await transport.waitForReceive()
        await time.waitForSleepers()
        await time.advance(by: 0.25)
        await transport.deliver(.closed(code: 1000, reason: ""))
        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)

        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status connected 0.0",
            "usage begin",
            "usage update sent=80.0 processed=- text=false",
            "usage update sent=- processed=0.0 text=false",
            "usage update sent=- processed=1234.0 text=true",
            "partial [Hello [redacted]]",
            "usage firstText 500.0",
            "status firstWords 0.5",
            "usage update sent=- processed=1500.0 text=false",
            // `speechEnd` carries nothing: the update is sent all the same and changes nothing.
            "usage update sent=- processed=- text=false",
            "usage update sent=- processed=2000.0 text=true",
            "segment [Hello world.] 0.0 2.0",
            "partial []",
            // Meta has no `streamComplete`: its normal close after `endStream` is the finalisation.
            "usage finalization 250.0",
            "status finalized 0.25",
            "usage finish completed=true"
        ])

        // Nothing the session reports contains the key.
        for event in events {
            XCTAssertFalse("\(event)".contains(metaKey), "\(event)")
        }
    }

    func testAnErrorEventEndsTheStreamWithTheProviderText() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.deliver(.text(#"{"type":"error","message":"synthetic-meta-unit-key is over its limit"}"#))

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(SpeechStreamFailure(.meta(.provider))))
        XCTAssertEqual(
            failureMessage(events),
            "Meta transcription stopped. Check API access, billing, or rate limits before retrying."
        )
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status connected 0.0",
            "usage begin",
            "usage finish completed=false",
            "failed"
        ])
        for event in events {
            XCTAssertFalse("\(event)".contains(metaKey), "\(event)")
            XCTAssertFalse("\(event)".contains("over its limit"), "\(event)")
        }

        // The socket is closed and the stream is over.
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
        let ending = await session.finish()
        XCTAssertEqual(ending, .interrupted)
    }

    // MARK: - What counts as a clean end (IOS-A11, as changed in 1.1.0)

    private let speechStart = #"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#
    private let speechComplete = #"{"type":"speechComplete","turnId":1,"audioProcessedMs":900,"transcript":"Last words."}"#

    /// A session in which turn 1 has started and has no final text yet.
    private func sessionWithAnOpenTurn(_ transport: FakeWebSocketTransport, time: ManualTime = ManualTime()) async throws -> SpeechStreamSession {
        let session = try await startedSession(transport, time: time)
        await transport.deliver(.text(speechStart))
        await transport.waitForReceive()
        return session
    }

    func testACleanCloseAfterEndStreamIsCompleted() async throws {
        // Normal, going away, and no status code at all: the provider ended the stream it was asked to end.
        for code in [1000, 1001, 1005] {
            let transport = FakeWebSocketTransport()
            let session = try await startedSession(transport)

            let finishing = Task { await session.finish() }
            await transport.waitForSentTexts(count: 2)
            await transport.deliver(.closed(code: code, reason: ""))
            let ending = await finishing.value
            XCTAssertEqual(ending, .completed, "\(code)")

            let events = await allEvents(of: session)
            XCTAssertEqual(describe(events).last, "usage finish completed=true", "\(code)")
            XCTAssertFalse(describe(events).contains("failed"), "\(code)")
            let closes = await transport.closeCalls
            XCTAssertEqual(closes, [1000], "\(code)")
        }
    }

    func testACleanCloseIsCompletedEvenWhileATurnIsOpen() async throws {
        for code in [1000, 1001, 1005] {
            let transport = FakeWebSocketTransport()
            let session = try await sessionWithAnOpenTurn(transport)

            let finishing = Task { await session.finish() }
            await transport.waitForSentTexts(count: 2)
            await transport.deliver(.closed(code: code, reason: ""))
            let ending = await finishing.value
            XCTAssertEqual(ending, .completed, "\(code)")
        }
    }

    func testAClose1000WithoutEndStreamIsInterrupted() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.deliver(.closed(code: 1000, reason: ""))

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(connectionFailure))
        XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"])
        let ending = await session.finish()
        XCTAssertEqual(ending, .interrupted)
    }

    func testAnyOtherCloseAfterEndStreamIsInterruptedWhileATurnHasNoFinalText() async throws {
        for code in [1006, 1008, 1011] {
            let transport = FakeWebSocketTransport()
            let session = try await sessionWithAnOpenTurn(transport)

            let finishing = Task { await session.finish() }
            await transport.waitForSentTexts(count: 2)
            await transport.deliver(.closed(code: code, reason: "secret"))
            let ending = await finishing.value
            XCTAssertEqual(ending, .interrupted, "\(code)")

            let events = await allEvents(of: session)
            XCTAssertEqual(events.last, .failed(connectionFailure), "\(code)")
            XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"], "\(code)")
            XCTAssertFalse(failureMessage(events)?.contains("secret") ?? true)
        }
    }

    func testAnyOtherCloseAfterEndStreamIsCompletedWhenNothingIsMissing() async throws {
        for code in [1006, 1008, 1011] {
            // No turn ever started.
            let transport = FakeWebSocketTransport()
            let session = try await startedSession(transport)

            let finishing = Task { await session.finish() }
            await transport.waitForSentTexts(count: 2)
            await transport.deliver(.closed(code: code, reason: ""))
            let ending = await finishing.value
            XCTAssertEqual(ending, .completed, "\(code)")
            let events = await allEvents(of: session)
            XCTAssertEqual(describe(events).last, "usage finish completed=true", "\(code)")
            XCTAssertFalse(describe(events).contains("failed"), "\(code)")

            // A turn started and its final text arrived before the close.
            let second = FakeWebSocketTransport()
            let finished = try await sessionWithAnOpenTurn(second)
            let finishingSecond = Task { await finished.finish() }
            await second.waitForSentTexts(count: 2)
            await second.deliver(.text(speechComplete))
            await second.deliver(.closed(code: code, reason: ""))
            let secondEnding = await finishingSecond.value
            XCTAssertEqual(secondEnding, .completed, "\(code)")
            let secondEvents = await allEvents(of: finished)
            XCTAssertTrue(describe(secondEvents).contains("segment [Last words.] 0.0 0.9"), "\(code)")
        }
    }

    func testADropAfterEndStreamIsInterruptedWhileATurnHasNoFinalText() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await sessionWithAnOpenTurn(transport)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 2)
        await transport.drop()
        let ending = await finishing.value
        XCTAssertEqual(ending, .interrupted)

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(connectionFailure))
        XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"])
    }

    func testADropAfterEndStreamIsCompletedWhenNothingIsMissing() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 2)
        await transport.drop()
        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)

        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events).suffix(3), [
            "usage finalization 0.0",
            "status finalized 0.0",
            "usage finish completed=true"
        ])
        XCTAssertFalse(describe(events).contains("failed"))
    }

    func testADropBeforeEndStreamLeftIsAFailure() async throws {
        // Mid-stream, with no turn open: the stream did not ask to end, so its end is a failure.
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.drop()

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(connectionFailure))
        XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"])
    }

    func testAfterAQuietPeriodThePhoneClosesAndTheStreamIsCompleted() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = try await startedSession(transport, time: time)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 2)
        // The 15 s limit and the quiet period are both waiting.
        await time.waitForSleepers(count: 2)

        await time.advance(by: 1)
        let closesInsideTheQuietPeriod = await transport.closeCalls
        XCTAssertEqual(closesInsideTheQuietPeriod, [])

        await time.advance(by: 0.5)
        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)
        XCTAssertEqual(time.now, SpeechStreamSession.quietSeconds)
        XCTAssertEqual(SpeechStreamSession.quietSeconds, 1.5)

        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events).suffix(3), [
            // The time up to `endStream`, not the quiet period that followed it.
            "usage finalization 0.0",
            "status finalized 0.0",
            "usage finish completed=true"
        ])
        XCTAssertFalse(describe(events).contains("failed"))
        let closes = await transport.closeCalls
        XCTAssertEqual(closes, [1000], "The phone closes the stream itself.")
        XCTAssertEqual(time.sleeperCount, 0, "The 15 s limit is no longer waiting.")
    }

    func testTheQuietPeriodWaitsForTheFinalTextOfAStartedTurn() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = try await sessionWithAnOpenTurn(transport, time: time)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 2)
        await time.waitForSleepers()

        // Five seconds of silence: the turn has no final text, so only the 15 s limit is waiting.
        await time.advance(by: 5)
        XCTAssertEqual(time.sleeperCount, 1)
        let closesWhileWaiting = await transport.closeCalls
        XCTAssertEqual(closesWhileWaiting, [])

        // The final text arrives: now the quiet period runs.
        await transport.deliver(.text(speechComplete))
        await time.waitForSleepers(count: 2)
        await time.advance(by: 1)
        let closesInsideTheQuietPeriod = await transport.closeCalls
        XCTAssertEqual(closesInsideTheQuietPeriod, [])
        await time.advance(by: 0.5)

        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events).suffix(7), [
            "usage update sent=- processed=900.0 text=true",
            "segment [Last words.] 0.0 0.9",
            "usage firstText 5000.0",
            "status firstWords 5.0",
            // Up to the provider's last word, not the quiet period that followed it.
            "usage finalization 5000.0",
            "status finalized 5.0",
            "usage finish completed=true"
        ])
    }

    func testEverythingTheProviderSaysStartsTheQuietPeriodAgain() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = try await startedSession(transport, time: time)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 2)
        await time.waitForSleepers(count: 2)

        // One second in, the provider reports progress: the quiet period counts from there.
        await time.advance(by: 1)
        await transport.deliver(.text(#"{"type":"audioProgress","audioProcessedMs":1500}"#))
        await transport.waitForReceive()
        await time.waitForSleepers(count: 2)
        await time.advance(by: 1)
        let closes = await transport.closeCalls
        XCTAssertEqual(closes, [], "Two seconds after `endStream`, one second after the last word of the provider.")

        await time.advance(by: 0.5)
        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)
        XCTAssertEqual(time.now, 2.5)
    }

    func testNoFinalTextWithinFifteenSecondsIsInterrupted() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = try await sessionWithAnOpenTurn(transport, time: time)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 2)
        await time.waitForSleepers()

        await time.advance(by: 14)
        let closesInsideTheLimit = await transport.closeCalls
        XCTAssertEqual(closesInsideTheLimit, [])

        await time.advance(by: 1)
        let ending = await finishing.value
        XCTAssertEqual(ending, .interrupted)
        XCTAssertEqual(time.now, MetaRealtimeAPI.finalizeSeconds)

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(connectionFailure))
        XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"])
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1, "The connection is cut when the limit passes.")
    }

    // MARK: - The 30 s connect limit

    func testThirtySecondConnectLimit() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = makeSession(transport, time: time)

        // The handshake is sent and Meta never acknowledges.
        let start = Task { try await session.start() }
        await transport.waitForSentTexts(count: 1)
        await transport.waitForReceive()
        await time.waitForSleepers()

        await time.advance(by: 29)
        let closesInsideTheLimit = await transport.closeCalls
        XCTAssertEqual(closesInsideTheLimit, [])

        await time.advance(by: 1)
        do {
            try await start.value
            XCTFail("The start ends at the connect limit.")
        } catch {
            XCTAssertEqual(error as? SpeechStreamFailure, connectionFailure)
        }
        XCTAssertEqual(time.now, MetaRealtimeAPI.connectSeconds)

        let closes = await transport.closeCalls
        let frames = await transport.sentFrames
        XCTAssertEqual(closes.count, 1, "The socket is cancelled when the limit passes.")
        XCTAssertEqual(frames, [])
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), ["status connecting"])
    }

    func testAConnectionThatNeverOpensAlsoEndsAtTheConnectLimit() async throws {
        let transport = FakeWebSocketTransport(open: .never)
        let time = ManualTime()
        let session = makeSession(transport, time: time)

        let start = Task { try await session.start() }
        await transport.waitForOpen()
        await time.waitForSleepers()
        await time.advance(by: 30)

        do {
            try await start.value
            XCTFail("The start ends at the connect limit.")
        } catch {
            XCTAssertEqual(error as? SpeechStreamFailure, connectionFailure)
        }
        let texts = await transport.sentTexts
        XCTAssertEqual(texts, [], "Not even the handshake left.")
    }

    // MARK: - Connection failures

    func testEveryOtherFailureOfTheDirectPathIsTheConnectionText() async throws {
        let text = "Could not connect directly to Meta. Check this iPhone's internet connection and retry. No PC is required for Meta transcription."
        XCTAssertEqual(connectionFailure.message, text)

        // The connection cannot be made, or the upgrade is refused.
        let refusals: [FakeWebSocketTransport.OpenResult] = [
            .failure(.tls), .failure(.unreachable), .failure(.timedOut), .failure(.other),
            .rejected(status: 401), .rejected(status: 503)
        ]
        for refusal in refusals {
            let session = makeSession(FakeWebSocketTransport(open: refusal))
            let failure = await startFailure(of: session)
            XCTAssertEqual(failure, connectionFailure)
        }

        // The handshake cannot be sent.
        let silent = FakeWebSocketTransport()
        await silent.failSends()
        let unsent = await startFailure(of: makeSession(silent))
        XCTAssertEqual(unsent, connectionFailure)
        let silentCloses = await silent.closeCalls
        XCTAssertEqual(silentCloses.count, 1)

        // The socket closes or drops before the acknowledgement.
        let closing = FakeWebSocketTransport()
        await closing.deliver(.closed(code: 1008, reason: "secret"))
        let closed = await startFailure(of: makeSession(closing))
        XCTAssertEqual(closed, connectionFailure)

        let dropping = FakeWebSocketTransport()
        await dropping.drop()
        let dropped = await startFailure(of: makeSession(dropping))
        XCTAssertEqual(dropped, connectionFailure)
    }

    func testADropMidStreamEndsTheStreamWithTheConnectionText() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await session.send(audio: Data(count: 3_840))
        await transport.drop()

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(connectionFailure))
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status connected 0.0",
            "usage begin",
            "usage update sent=80.0 processed=- text=false",
            "usage finish completed=false",
            "failed"
        ])
    }

    func testASendErrorEndsTheStreamWithSendFailed() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.failSends()
        await session.send(audio: Data(count: 3_840))

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(SpeechStreamFailure(.sendFailed)))
        XCTAssertEqual(failureMessage(events), "Audio connection failed. Resume to reconnect.")
    }

    func testCancellingTheStartThrowsCancellation() async throws {
        let transport = FakeWebSocketTransport()
        let session = makeSession(transport)

        let start = Task { try await session.start() }
        await transport.waitForSentTexts(count: 1)
        await transport.waitForReceive()
        start.cancel()

        do {
            try await start.value
            XCTFail("A cancelled start throws.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
    }

    // MARK: - Helpers

    private let connectionFailure = SpeechStreamFailure(.meta(.connection))

    private func makeSession(_ transport: FakeWebSocketTransport, time: ManualTime = ManualTime()) -> SpeechStreamSession {
        SpeechStreamSession(dialect: .meta(key: metaKey), transport: transport, timing: time.timing)
    }

    /// A session that Meta acknowledged at once: `start()` has returned and the session is waiting for
    /// the next event.
    private func startedSession(_ transport: FakeWebSocketTransport, time: ManualTime = ManualTime()) async throws -> SpeechStreamSession {
        let session = makeSession(transport, time: time)
        await transport.deliver(.text(acknowledgement))
        try await session.start()
        await transport.waitForReceive()
        return session
    }

    /// The failure `start()` throws, or nil (with a test failure) when it does not throw one.
    private func startFailure(of session: SpeechStreamSession) async -> SpeechStreamFailure? {
        do {
            try await session.start()
            XCTFail("The start must fail.")
            return nil
        } catch {
            XCTAssertTrue(error is SpeechStreamFailure, "Unexpected error: \(error)")
            return error as? SpeechStreamFailure
        }
    }
}

// MARK: - Values and event helpers

private let metaKey = "synthetic-meta-unit-key"
private let acknowledgement = #"{"sessionId":"synthetic-session"}"#

/// Every event of a session. Returns when the stream has ended, so a test calls it after the ending
/// it caused.
private func allEvents(of session: SpeechStreamSession) async -> [SpeechStreamEvent] {
    var events: [SpeechStreamEvent] = []
    for await event in session.events {
        events.append(event)
    }
    return events
}

/// The message of the stream's `failed` event, or nil when it has none.
private func failureMessage(_ events: [SpeechStreamEvent]) -> String? {
    for case .failed(let failure) in events {
        return failure.message
    }
    return nil
}

/// One line per event, without the random ids, so a whole stream can be compared at once.
private func describe(_ events: [SpeechStreamEvent]) -> [String] {
    events.map { event in
        switch event {
        case .status(.connecting):
            return "status connecting"
        case .status(.loading(let elapsedSeconds)):
            return "status loading \(elapsedSeconds)"
        case .status(.connected(let setupSeconds)):
            return "status connected \(setupSeconds)"
        case .status(.firstWords(let seconds)):
            return "status firstWords \(seconds)"
        case .status(.finalized(let seconds)):
            return "status finalized \(seconds)"
        case .partial(let text):
            return "partial [\(text)]"
        case .segment(let segment):
            return "segment [\(segment.text)] \(segment.startSeconds) \(segment.endSeconds)"
        case .usage(.begin):
            return "usage begin"
        case .usage(.update(_, let sentMs, let processedMs, let hasTranscript)):
            let sent = sentMs.map { "\($0)" } ?? "-"
            let processed = processedMs.map { "\($0)" } ?? "-"
            return "usage update sent=\(sent) processed=\(processed) text=\(hasTranscript)"
        case .usage(.finish(_, let completed)):
            return "usage finish completed=\(completed)"
        case .usage(.firstText(let milliseconds)):
            return "usage firstText \(milliseconds)"
        case .usage(.finalization(let milliseconds)):
            return "usage finalization \(milliseconds)"
        case .failed:
            return "failed"
        }
    }
}
