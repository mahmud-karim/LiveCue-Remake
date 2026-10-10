import Foundation
import LiveCueCore
import XCTest

/// A relay speech stream end to end over a fake socket: protocol 13.7 and P2-213 to P2-218, the speech
/// URL and headers (P2-200, P2-201), the microphone rule (AC-IOS-020), the completed rule (AC-IOS-047)
/// and the usage events of one stream (AC-IOS-041).
final class SpeechStreamSessionRelayTests: XCTestCase {
    // MARK: - Opening (P2-200, P2-201)

    func testOpensTheSpeechURLWithTheThreeHeaders() async throws {
        let rows: [(endpoint: String, url: String, model: SpeechProvider)] = [
            ("https://pc.example.test:11443/", "wss://pc.example.test:11443/v1/speech", .nemotron),
            ("https://pc.example.test:10000/livecue/", "wss://pc.example.test:10000/livecue/v1/speech", .qwen3)
        ]
        for row in rows {
            let transport = FakeWebSocketTransport()
            let session = makeSession(transport, model: row.model, endpoint: row.endpoint)
            await transport.deliver(.text(readyFrame(row.model.rawValue)))
            try await session.start()

            let url = try await unwrapped(transport.openedURL)
            XCTAssertEqual(url.absoluteString, row.url)
            XCTAssertNil(url.query)
            XCTAssertNil(url.fragment)

            // Exactly the three headers of protocol 10.1: no `Origin`, no `Cookie`, nothing else.
            let headers = await transport.openedHeaders
            XCTAssertEqual(headers, [
                "Authorization": "Bearer " + CoreFixtures.token,
                "X-LiveCue-Protocol": "2",
                "X-LiveCue-Speech-Model": row.model.rawValue
            ])
            for name in headers.keys.map({ $0.lowercased() }) {
                XCTAssertFalse(["origin", "cookie"].contains(name), name)
                XCTAssertFalse(name.hasPrefix("sec-"), name)
            }

            let maximum = await transport.openedMaximumMessageSize
            XCTAssertEqual(maximum, 131_072, "C-21")

            await session.abort()
        }
    }

    // MARK: - No audio before `ready` (P2-213, AC-IOS-020)

    func testNoAudioBeforeReadyAndLoadingKeepsWaiting() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = makeSession(transport, time: time)

        let start = Task { try await session.start() }
        await transport.waitForOpen()

        await transport.deliver(.text(loadingFrame(2)))
        await transport.waitForReceive()
        await session.send(audio: Data(count: 5_120))

        // Almost all of C-59 passes while the model loads. The phone keeps waiting.
        await time.waitForSleepers()
        await time.advance(by: 170)
        await transport.deliver(.text(loadingFrame(172)))
        await transport.waitForReceive()
        await session.send(audio: Data(count: 5_120))

        let framesBeforeReady = await transport.sentFrames
        let closesBeforeReady = await transport.closeCalls
        XCTAssertEqual(framesBeforeReady, [], "No audio may leave before `ready`.")
        XCTAssertEqual(closesBeforeReady, [])

        await transport.deliver(.text(readyFrame("nemotron")))
        try await start.value

        // Only now does audio leave, and nothing of what was offered earlier was kept.
        await session.send(audio: Data(count: 2_560))
        let frames = await transport.sentFrames
        XCTAssertEqual(frames.map(\.count), [2_560])

        await session.abort()
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status loading 2",
            "status loading 172",
            "status connected 0.25",
            "usage begin",
            "usage update sent=80.0 processed=- text=false",
            "usage finish completed=false"
        ])
    }

    func testWhenC59PassesTheSocketIsClosedAndTheStartFailsWithTimeout() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = makeSession(transport, time: time)

        let start = Task { try await session.start() }
        await transport.waitForOpen()
        await transport.deliver(.text(loadingFrame(2)))
        await transport.waitForReceive()
        await time.waitForSleepers()

        await time.advance(by: 179)
        let closesInsideTheLimit = await transport.closeCalls
        XCTAssertEqual(closesInsideTheLimit, [], "Up to C-59 the phone keeps waiting.")

        await time.advance(by: 1)
        do {
            try await start.value
            XCTFail("The start ends when C-59 passes.")
        } catch {
            XCTAssertEqual(error as? SpeechStreamFailure, SpeechStreamFailure(.relay(RelayFailure.local(.timeout, origin: .stream))))
        }
        XCTAssertEqual(time.now, RelayConstants.speechConnectSeconds)

        // The socket is closed, `events` has finished, and no audio ever left.
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), ["status connecting", "status loading 2"])

        await session.send(audio: Data(count: 2_560))
        let frames = await transport.sentFrames
        let texts = await transport.sentTexts
        XCTAssertEqual(frames, [])
        XCTAssertEqual(texts, [])
    }

    func testAnUpgradeThatNeverAnswersAlsoEndsAtC59() async throws {
        let transport = FakeWebSocketTransport(open: .never)
        let time = ManualTime()
        let session = makeSession(transport, time: time)

        let start = Task { try await session.start() }
        await transport.waitForOpen()
        await time.waitForSleepers()
        await time.advance(by: 180)

        do {
            try await start.value
            XCTFail("The start ends when C-59 passes.")
        } catch {
            XCTAssertEqual(error as? SpeechStreamFailure, SpeechStreamFailure(.relay(RelayFailure.local(.timeout, origin: .stream))))
        }
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), ["status connecting"])
    }

    func testAReadyForAnotherProviderOrSampleRateFailsAndNoAudioIsSent() async throws {
        let mismatches = [
            readyFrame("qwen3"),
            #"{"type":"ready","provider":"nemotron","sampleRate":24000,"handshakeMs":250}"#
        ]
        for frame in mismatches {
            let transport = FakeWebSocketTransport()
            let session = makeSession(transport)
            await transport.deliver(.text(loadingFrame(0)))
            await transport.deliver(.text(frame))

            let failure = await startFailure(of: session)
            XCTAssertEqual(failure, SpeechStreamFailure(.relay(RelayFailure.local(.speechNotConfirmed, origin: .stream))), frame)

            await session.send(audio: Data(count: 5_120))
            let frames = await transport.sentFrames
            let closes = await transport.closeCalls
            XCTAssertEqual(frames, [], frame)
            XCTAssertEqual(closes.count, 1, frame)

            let events = await allEvents(of: session)
            XCTAssertEqual(describe(events), ["status connecting", "status loading 0"], frame)
        }
    }

    func testALabFrameOrOtherTextBeforeReadyIsUnexpected() async throws {
        let unexpected = [
            #"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#,
            #"{"type":"transcript","transcript":"Local words"}"#,
            #"{"type":"speechComplete","turnId":1,"transcript":"Local words"}"#,
            #"{"type":"streamComplete","audioProcessedMs":80}"#,
            "not a frame"
        ]
        for frame in unexpected {
            let transport = FakeWebSocketTransport()
            let session = makeSession(transport)
            await transport.deliver(.text(frame))

            let failure = await startFailure(of: session)
            XCTAssertEqual(failure, SpeechStreamFailure(.relay(RelayFailure.local(.unexpectedFrame, origin: .stream))), frame)
            let frames = await transport.sentFrames
            let closes = await transport.closeCalls
            XCTAssertEqual(frames, [], frame)
            XCTAssertEqual(closes.count, 1, frame)
        }
    }

    // MARK: - Errors and closes (P2-214)

    func testAnErrorFrameBeforeReadyFailsTheStartWithThatCode() async throws {
        let transport = FakeWebSocketTransport()
        let session = makeSession(transport)
        await transport.deliver(.text(loadingFrame(0)))
        await transport.deliver(.text(errorFrame("SPEECH_BUSY")))

        let failure = await startFailure(of: session)
        XCTAssertEqual(failure, SpeechStreamFailure(.relay(RelayFailure.wire(.speechBusy, origin: .stream))))

        // After the throw the socket is closed and `events` has finished, without a `failed` event.
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), ["status connecting", "status loading 0"])
    }

    func testAnErrorFrameAfterReadyEndsTheStreamWithThatCode() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.deliver(.text(errorFrame("LAB_DISCONNECTED")))

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(SpeechStreamFailure(.relay(RelayFailure.wire(.labDisconnected, origin: .stream)))))
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status connected 0.25",
            "usage begin",
            "usage finish completed=false",
            "failed"
        ])

        // `failed` arrives with the socket already closed. Nothing reconnects.
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
        let ending = await session.finish()
        XCTAssertEqual(ending, .interrupted)
        let texts = await transport.sentTexts
        XCTAssertEqual(texts, [], "No endStream goes to a stream that has ended.")
    }

    func testAnErrorFrameWithACodeThisBuildDoesNotKnowKeepsTheRelaysText() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.deliver(.text(#"{"type":"error","code":"SOMETHING_NEW","message":"A newer relay said this."}"#))

        let events = await allEvents(of: session)
        let expected = RelayFailure(kind: .wire("SOMETHING_NEW"), origin: .stream, relayText: "A newer relay said this.")
        XCTAssertEqual(events.last, .failed(SpeechStreamFailure(.relay(expected))))
    }

    func testACloseWithAKnownCodeAsReasonAndNoFrameUsesThatCode() async throws {
        // After `ready`.
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.deliver(.closed(code: 1011, reason: "AUDIO_IDLE_TIMEOUT"))

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(SpeechStreamFailure(.relay(RelayFailure.wire(.audioIdleTimeout, origin: .stream)))))

        // Before `ready`: the start fails with it.
        let refusing = FakeWebSocketTransport()
        let refused = makeSession(refusing)
        await refusing.deliver(.closed(code: 1008, reason: "SPEECH_BUSY"))
        let failure = await startFailure(of: refused)
        XCTAssertEqual(failure, SpeechStreamFailure(.relay(RelayFailure.wire(.speechBusy, origin: .stream))))
    }

    func testADropWithNeitherGivesStreamDropped() async throws {
        // After `ready`: a receive failure mid-stream.
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.drop()

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(streamDropped))
        XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"])
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)

        // A close whose reason is not a code is the same condition.
        let closing = FakeWebSocketTransport()
        let closed = try await startedSession(closing)
        await closing.deliver(.closed(code: 1011, reason: "going away"))
        let closedEvents = await allEvents(of: closed)
        XCTAssertEqual(closedEvents.last, .failed(streamDropped))

        // Before `ready`: the start fails with it.
        let early = FakeWebSocketTransport()
        let waiting = makeSession(early)
        await early.deliver(.text(loadingFrame(0)))
        await early.drop()
        let failure = await startFailure(of: waiting)
        XCTAssertEqual(failure, streamDropped)
    }

    // MARK: - The completed rule (P2-215, AC-IOS-047)

    func testACloseWith1000InsideC60AfterEndStreamIsCompleted() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = try await startedSession(transport, time: time)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 1)
        await time.waitForSleepers()
        await time.advance(by: 14)
        await transport.deliver(.closed(code: 1000, reason: ""))

        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)

        // `finish()` returned after `events` finished: the whole stream can be read now.
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status connected 0.25",
            "usage begin",
            "usage finish completed=true"
        ])
        let texts = await transport.sentTexts
        let closes = await transport.closeCalls
        XCTAssertEqual(texts, [endStream])
        XCTAssertEqual(closes, [1000])
    }

    func testACloseWith1011AfterEndStreamIsInterrupted() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 1)
        await transport.deliver(.closed(code: 1011, reason: "FINALIZE_TIMEOUT"))

        let ending = await finishing.value
        XCTAssertEqual(ending, .interrupted)
        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(SpeechStreamFailure(.relay(RelayFailure.wire(.finalizeTimeout, origin: .stream)))))
        XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"])
    }

    func testADropAfterEndStreamIsInterrupted() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 1)
        await transport.drop()

        let ending = await finishing.value
        XCTAssertEqual(ending, .interrupted)
        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(streamDropped))
        XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"])
    }

    func testNoCloseWithinC60IsInterruptedWithStreamDropped() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = try await startedSession(transport, time: time)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 1)
        await time.waitForSleepers()

        await time.advance(by: 14)
        let closesInsideTheLimit = await transport.closeCalls
        XCTAssertEqual(closesInsideTheLimit, [], "Up to C-60 the phone waits for the close.")

        await time.advance(by: 1)
        let ending = await finishing.value
        XCTAssertEqual(ending, .interrupted)
        XCTAssertEqual(time.now, RelayConstants.speechFinalizeSeconds)

        // The connection is cut and the stream is recorded as interrupted (AC-IOS-047).
        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(streamDropped))
        XCTAssertEqual(describe(events).suffix(2), ["usage finish completed=false", "failed"])
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
    }

    func testACloseWith1000WithoutEndStreamIsInterrupted() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.deliver(.closed(code: 1000, reason: ""))

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(streamDropped))
        let ending = await session.finish()
        XCTAssertEqual(ending, .interrupted)
    }

    // MARK: - Framing (P2-216)

    func testAudioIsSentIn2560ByteFramesThenARemainderThenExactlyOneEndStream() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        let audio = Data((0..<7_001).map { UInt8(truncatingIfNeeded: $0 &* 13) })

        await session.send(audio: audio.subdata(in: 0..<3_000))
        let afterFirst = await transport.sentFrames
        XCTAssertEqual(afterFirst.map(\.count), [2_560])

        await session.send(audio: audio.subdata(in: 3_000..<6_000))
        await session.send(audio: audio.subdata(in: 6_000..<7_001))
        let beforeFinish = await transport.sentFrames
        let textsBeforeFinish = await transport.sentTexts
        XCTAssertEqual(beforeFinish.map(\.count), [2_560, 2_560], "1,881 bytes wait for more audio or for the end.")
        XCTAssertEqual(textsBeforeFinish, [])

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 1)

        // The remainder left as one shorter frame, not empty and even: the odd last byte is dropped.
        let frames = await transport.sentFrames
        XCTAssertEqual(frames.map(\.count), [2_560, 2_560, 1_880])
        XCTAssertEqual(frames.reduce(into: Data()) { $0.append($1) }, audio.subdata(in: 0..<7_000))

        // After `finish` began, audio is ignored and a second `finish` sends nothing.
        await session.send(audio: Data(count: 5_120))
        let again = Task { await session.finish() }
        await transport.deliver(.closed(code: 1000, reason: ""))
        let ending = await finishing.value
        let secondEnding = await again.value
        XCTAssertEqual(ending, .completed)
        XCTAssertEqual(secondEnding, .completed)

        let allFrames = await transport.sentFrames
        let texts = await transport.sentTexts
        XCTAssertEqual(allFrames.count, 3)
        XCTAssertEqual(texts, [endStream], "Exactly one endStream follows the audio.")
    }

    func testWithoutARemainderOnlyEndStreamFollows() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await session.send(audio: Data(count: 5_120))

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 1)
        await transport.deliver(.closed(code: 1000, reason: ""))
        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)

        let frames = await transport.sentFrames
        let texts = await transport.sentTexts
        XCTAssertEqual(frames.map(\.count), [2_560, 2_560], "No empty frame is ever sent.")
        XCTAssertEqual(texts, [endStream])
    }

    // MARK: - Unknown frames and members (P2-218)

    func testUnknownFramesAndUnknownMembersAreIgnored() async throws {
        let transport = FakeWebSocketTransport()
        let session = makeSession(transport)

        // Before `ready` a frame of unknown type does not end the wait.
        await transport.deliver(.text(#"{"type":"hello","version":9,"note":"hidden"}"#))
        await transport.deliver(.text(loadingFrame(2)))
        await transport.deliver(.text(#"{"type":"somethingNew"}"#))
        await transport.deliver(.data(Data([1, 2, 3])))
        await transport.deliver(.text(#"{"type":"ready","provider":"nemotron","sampleRate":16000,"handshakeMs":250,"labBuild":"hidden"}"#))
        try await session.start()

        // While streaming: unknown types are dropped, known frames are read without their unknown members.
        await transport.deliver(.text(#"{"type":"somethingNew","value":"hidden"}"#))
        await transport.deliver(.text(#"{"type":"audioProgress","audioProcessedMs":999}"#))
        await transport.deliver(.text(#"{"type":"speechStart","turnId":1,"audioProcessedMs":0,"private":"hidden"}"#))
        await transport.deliver(.text(#"{"type":"transcript","transcript":"Local words","private":"hidden"}"#))
        await transport.deliver(.data(Data([4, 5, 6])))
        await transport.waitForReceive()
        await session.abort()

        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status loading 2",
            "status connected 0.25",
            "usage begin",
            "usage update sent=- processed=0.0 text=false",
            "usage update sent=- processed=- text=true",
            "partial [Local words]",
            "usage firstText 0.0",
            "status firstWords 0.0",
            "usage finish completed=false"
        ])
        for event in events {
            XCTAssertFalse("\(event)".contains("hidden"), "\(event)")
        }
    }

    // MARK: - Usage events in order (AC-IOS-041)

    func testUsageEventsInOrder() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let (events, ending) = try await runWholeStream(transport, time: time)

        XCTAssertEqual(ending, .completed)
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status loading 0",
            "status connected 0.25",
            "usage begin",
            "usage update sent=80.0 processed=- text=false",
            "usage update sent=- processed=0.0 text=false",
            "usage update sent=- processed=80.0 text=true",
            "partial [Local words]",
            "usage firstText 1500.0",
            "status firstWords 1.5",
            "usage update sent=160.0 processed=- text=false",
            "usage update sent=163.125 processed=- text=false",
            "usage update sent=- processed=160.0 text=true",
            "segment [Local words.] 0.0 0.16",
            "partial []",
            "usage update sent=- processed=163.125 text=false",
            "usage finalization 35.0",
            "status finalized 0.035",
            "usage finish completed=true"
        ])

        // One usage stream: every event of the session names the same id, begun once and finished once.
        let ids = usageIDs(events)
        XCTAssertEqual(Set(ids).count, 1)
        XCTAssertEqual(describe(events).filter { $0 == "usage begin" }.count, 1)
        XCTAssertEqual(describe(events).filter { $0.hasPrefix("usage finish") }.count, 1)
    }

    func testAStreamCompleteWithoutAFinalizationTimeUsesThePhonesClock() async throws {
        let transport = FakeWebSocketTransport()
        let time = ManualTime()
        let session = try await startedSession(transport, time: time)

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 1)
        await time.waitForSleepers()
        await time.advance(by: 0.5)
        await transport.deliver(.text(#"{"type":"streamComplete"}"#))
        await transport.deliver(.closed(code: 1000, reason: ""))

        let ending = await finishing.value
        XCTAssertEqual(ending, .completed)
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events).suffix(4), [
            "usage update sent=- processed=- text=false",
            "usage finalization 500.0",
            "status finalized 0.5",
            "usage finish completed=true"
        ])
    }

    // MARK: - Refused upgrades and transport failures (P2-217)

    func testARefusedUpgradeFailsTheStartWithItsClassification() async throws {
        let rows: [(status: Int, failure: RelayFailure)] = [
            (401, RelayFailure.wire(.unauthorized, origin: .stream)),
            (404, RelayFailure.wire(.notFound, origin: .stream)),
            (429, RelayFailure.wire(.tooManyRequests, origin: .stream)),
            (502, RelayFailure.local(.noRelay, origin: .stream)),
            (503, RelayFailure.local(.noRelay, origin: .stream)),
            (504, RelayFailure.local(.noRelay, origin: .stream)),
            (500, RelayFailure.local(.http, origin: .stream, httpStatus: 500)),
            (302, RelayFailure.local(.http, origin: .stream, httpStatus: 302))
        ]
        for row in rows {
            let transport = FakeWebSocketTransport(open: .rejected(status: row.status))
            let session = makeSession(transport)
            let failure = await startFailure(of: session)
            XCTAssertEqual(failure, SpeechStreamFailure(.relay(row.failure)), "\(row.status)")

            let events = await allEvents(of: session)
            XCTAssertEqual(describe(events), ["status connecting"], "\(row.status)")
        }
    }

    func testATransportFailureFailsTheStartAsRows4To6() async throws {
        let rows: [(transport: TransportFailure, condition: RelayLocalCondition)] = [
            (.tls, .tls), (.unreachable, .unreachable), (.timedOut, .unreachable), (.other, .network)
        ]
        for row in rows {
            let transport = FakeWebSocketTransport(open: .failure(row.transport))
            let session = makeSession(transport)
            let failure = await startFailure(of: session)
            XCTAssertEqual(failure, SpeechStreamFailure(.relay(RelayFailure.local(row.condition, origin: .stream))), "\(row.transport)")
        }
    }

    // MARK: - Send failures, abort, cancellation

    func testASendErrorEndsTheStreamWithSendFailed() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.failSends()
        await session.send(audio: Data(count: 5_120))

        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(SpeechStreamFailure(.sendFailed)))
        XCTAssertEqual(SpeechStreamFailure(.sendFailed).message, "Audio connection failed. Resume to reconnect.")
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status connected 0.25",
            "usage begin",
            "usage finish completed=false",
            "failed"
        ])
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
    }

    func testAnEndStreamThatCannotBeSentIsInterrupted() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await transport.failSends()

        let ending = await session.finish()
        XCTAssertEqual(ending, .interrupted)
        let events = await allEvents(of: session)
        XCTAssertEqual(events.last, .failed(SpeechStreamFailure(.sendFailed)))
    }

    func testAbortClosesNowAndMarksTheUsageStreamInterrupted() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)
        await session.send(audio: Data(count: 2_560))

        await session.abort()
        await session.abort()

        // No `failed` event: the caller asked for it.
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), [
            "status connecting",
            "status connected 0.25",
            "usage begin",
            "usage update sent=80.0 processed=- text=false",
            "usage finish completed=false"
        ])
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1, "Abort is idempotent.")

        await session.send(audio: Data(count: 2_560))
        let ending = await session.finish()
        XCTAssertEqual(ending, .interrupted)
        let frames = await transport.sentFrames
        let texts = await transport.sentTexts
        XCTAssertEqual(frames.count, 1)
        XCTAssertEqual(texts, [])
    }

    func testAbortWhileStartingEndsTheStartAsCancelled() async throws {
        let transport = FakeWebSocketTransport()
        let session = makeSession(transport)

        let start = Task { try await session.start() }
        await transport.waitForOpen()
        await transport.waitForReceive()
        await session.abort()

        do {
            try await start.value
            XCTFail("An aborted start throws.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), ["status connecting"])
    }

    func testCancellingTheStartThrowsCancellationAndClosesTheSocket() async throws {
        let transport = FakeWebSocketTransport()
        let session = makeSession(transport)

        let start = Task { try await session.start() }
        await transport.waitForOpen()
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
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events), ["status connecting"])
    }

    func testFinishOrAbortBeforeStartEndsTheSessionWithoutOpeningAnything() async throws {
        let finished = FakeWebSocketTransport()
        let neverStarted = makeSession(finished)
        let ending = await neverStarted.finish()
        XCTAssertEqual(ending, .interrupted)
        let finishedEvents = await allEvents(of: neverStarted)
        XCTAssertEqual(finishedEvents, [])

        let aborted = FakeWebSocketTransport()
        let session = makeSession(aborted)
        await session.abort()
        let abortedEvents = await allEvents(of: session)
        XCTAssertEqual(abortedEvents, [])

        let finishedURL = await finished.openedURL
        let abortedURL = await aborted.openedURL
        XCTAssertNil(finishedURL)
        XCTAssertNil(abortedURL)
    }

    func testASecondStartEndsTheStream() async throws {
        let transport = FakeWebSocketTransport()
        let session = try await startedSession(transport)

        let failure = await startFailure(of: session)
        XCTAssertEqual(failure, streamDropped)

        // The rule "after a throw the socket is closed and `events` has finished" holds here too.
        let closes = await transport.closeCalls
        XCTAssertEqual(closes.count, 1)
        let events = await allEvents(of: session)
        XCTAssertEqual(describe(events).last, "usage finish completed=false")
    }

    func testNoFailureTextContainsTheToken() async throws {
        let failures: [RelayFailure] = [
            RelaySpeechNegotiation.failure(forUpgradeStatus: 401),
            RelaySpeechNegotiation.failure(forUpgradeStatus: 500),
            RelaySpeechNegotiation.failure(for: .tls),
            RelayFailure.local(.timeout, origin: .stream),
            RelayFailure.local(.streamDropped, origin: .stream),
            RelayFailure.wire(.speechBusy, origin: .stream)
        ]
        for failure in failures {
            XCTAssertFalse(SpeechStreamFailure(.relay(failure)).message.contains(CoreFixtures.token), "\(failure.kind)")
        }
    }

    // MARK: - Helpers

    private let endStream = #"{"type":"endStream"}"#
    private let streamDropped = SpeechStreamFailure(.relay(RelayFailure.local(.streamDropped, origin: .stream)))

    private func makeSession(
        _ transport: FakeWebSocketTransport,
        time: ManualTime = ManualTime(),
        model: SpeechProvider = .nemotron,
        endpoint: String = "https://pc.example.test:11443/"
    ) -> SpeechStreamSession {
        SpeechStreamSession(
            dialect: .relay(pairing: CoreFixtures.pairing(endpoint), model: model),
            transport: transport,
            timing: time.timing
        )
    }

    /// A session whose relay answered `ready` (setup 0.25 s): `start()` has returned and the session is
    /// waiting for the next frame.
    private func startedSession(_ transport: FakeWebSocketTransport, time: ManualTime = ManualTime()) async throws -> SpeechStreamSession {
        let session = makeSession(transport, time: time)
        await transport.deliver(.text(readyFrame("nemotron")))
        try await session.start()
        await transport.waitForReceive()
        return session
    }

    /// One whole stream as the fake lab of protocol 15.6 would answer it: loading, ready, audio, one
    /// turn, `endStream`, the final text, `streamComplete` and a normal close.
    private func runWholeStream(
        _ transport: FakeWebSocketTransport,
        time: ManualTime
    ) async throws -> (events: [SpeechStreamEvent], ending: SpeechStreamEnding) {
        let session = makeSession(transport, time: time)
        await transport.deliver(.text(loadingFrame(0)))
        await transport.deliver(.text(readyFrame("nemotron")))
        try await session.start()

        await session.send(audio: Data(count: 2_560))
        await transport.deliver(.text(#"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#))
        await transport.waitForReceive()

        // First words arrive a second and a half after the microphone opened.
        await time.advance(by: 1.5)
        await transport.deliver(.text(#"{"type":"transcript","turnId":1,"audioProcessedMs":80,"transcript":"Local words"}"#))
        await transport.waitForReceive()

        // One more frame, and 100 bytes that only the flush sends.
        await session.send(audio: Data(count: 2_660))

        let finishing = Task { await session.finish() }
        await transport.waitForSentTexts(count: 1)
        await transport.deliver(.text(#"{"type":"speechComplete","turnId":1,"audioProcessedMs":160,"transcript":"Local words."}"#))
        await transport.deliver(.text(#"{"type":"streamComplete","audioProcessedMs":163.125,"finalizationMs":35}"#))
        await transport.deliver(.closed(code: 1000, reason: ""))
        let ending = await finishing.value

        let events = await allEvents(of: session)
        return (events, ending)
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

    private func unwrapped<Value>(_ value: Value?) throws -> Value {
        try XCTUnwrap(value)
    }
}

// MARK: - Frames and event helpers

private func loadingFrame(_ elapsedSeconds: Int) -> String {
    #"{"type":"loading","provider":"nemotron","elapsedSeconds":\#(elapsedSeconds),"message":"Loading Nemotron on PC · \#(elapsedSeconds) s. Microphone starts when ready."}"#
}

private func readyFrame(_ provider: String) -> String {
    #"{"type":"ready","provider":"\#(provider)","sampleRate":16000,"handshakeMs":250}"#
}

private func errorFrame(_ code: String) -> String {
    #"{"type":"error","code":"\#(code)","message":"The relay's own text."}"#
}

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

private func usageIDs(_ events: [SpeechStreamEvent]) -> [UUID] {
    var ids: [UUID] = []
    for case .usage(let update) in events {
        switch update {
        case .begin(let id), .finish(let id, _), .update(let id, _, _, _):
            ids.append(id)
        case .firstText, .finalization:
            break
        }
    }
    return ids
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
