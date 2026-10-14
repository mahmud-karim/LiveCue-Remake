import AVFoundation
import LiveCueCore
import Observation
import XCTest
@testable import LiveCueRemake

/// The streaming columns of section 4.2 with a fake capture: `MetaTranscriber` and `RelayTranscriber`
/// over a scripted socket. No test opens a real socket or the microphone.
final class StreamingTranscriberEngineTests: XCTestCase {
    // MARK: - The microphone opens after the handshake

    @MainActor
    func testMetaOpensTheMicrophoneOnlyAfterTheAcknowledgement() async throws {
        let rig = StreamingRig.meta()

        let starting = Task { try await rig.start() }
        await rig.socket.waitForSentTexts(count: 1)
        await rig.socket.waitForReceive()
        XCTAssertTrue(rig.audio.requests.isEmpty, "The handshake is out, but Meta has not answered.")
        XCTAssertFalse(rig.audio.isRunning)

        await rig.socket.deliver(.text(SpeechTest.metaAcknowledgement))
        try await starting.value

        XCTAssertEqual(rig.audio.requests, [AudioCaptureRequest(target: .pcm16(sampleRate: 24_000), queueLimit: 64)])
        XCTAssertTrue(rig.audio.isRunning)

        // Everything the start caused was emitted before it returned.
        let emitted = rig.transcriber.emittedEventCount
        XCTAssertEqual(emitted, 3)
        await rig.log.wait(for: emitted)
        XCTAssertEqual(rig.log.lines, [
            "status Connecting to Meta Muse · cloud…",
            "status Meta direct · 0.00 s setup",
            "usage begin"
        ])
        XCTAssertTrue(rig.log.events.allSatisfy { $0.generation == rig.generation })

        // The key travels in the first message only.
        let url = await rig.socket.openedURL
        let headers = await rig.socket.openedHeaders
        XCTAssertEqual(url, MetaRealtimeAPI.endpoint)
        XCTAssertEqual(headers, [:])
        await rig.end()
    }

    @MainActor
    func testRelayShowsLoadingAndOpensTheMicrophoneOnlyAtReady() async throws {
        let rig = StreamingRig.relay(.nemotron)

        let starting = Task { try await rig.start() }
        await rig.socket.waitForReceive()
        XCTAssertTrue(rig.audio.requests.isEmpty)

        await rig.socket.deliver(.text(SpeechTest.relayLoading(12)))
        await rig.log.waitForLine("status Loading Nemotron 3.5 on PC · 12 s. Microphone starts when ready.")
        XCTAssertTrue(rig.audio.requests.isEmpty, "The model is still loading on the PC.")

        await rig.socket.deliver(.text(SpeechTest.relayReady("nemotron")))
        try await starting.value

        XCTAssertEqual(rig.audio.requests, [AudioCaptureRequest(target: .pcm16(sampleRate: 16_000), queueLimit: 64)])
        let emitted = rig.transcriber.emittedEventCount
        await rig.log.wait(for: emitted)
        XCTAssertEqual(rig.log.lines, [
            "status Connecting to Nemotron 3.5 · PC…",
            "status Loading Nemotron 3.5 on PC · 12 s. Microphone starts when ready.",
            "status Connected · 0.25 s setup",
            "usage begin"
        ])

        let headers = await rig.socket.openedHeaders
        XCTAssertEqual(headers["X-LiveCue-Speech-Model"], "nemotron")
        XCTAssertEqual(headers["Authorization"], "Bearer " + FixtureValues.pairingToken)
        await rig.end()
    }

    @MainActor
    func testQwen3AsksThePCForItsOwnModel() async throws {
        let rig = StreamingRig.relay(.qwen3)
        await rig.socket.deliver(.text(SpeechTest.relayReady("qwen3")))
        try await rig.start()

        XCTAssertEqual(rig.transcriber.provider, .qwen3)
        let headers = await rig.socket.openedHeaders
        XCTAssertEqual(headers["X-LiveCue-Speech-Model"], "qwen3")
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines.first, "status Connecting to Qwen3 1.7B · PC…")
        await rig.end()
    }

    // MARK: - Start failures

    @MainActor
    func testMetaWithoutAKeyFailsWithTheMissingKeyTextAndOpensNothing() async {
        let rig = StreamingRig.meta(key: nil)
        let failure = await rig.startFailure()

        XCTAssertEqual(failure?.reason, .stream)
        XCTAssertEqual(failure?.message, "Add your Meta API key in Transcription. It stays in this iPhone's Keychain.")
        let opens = await rig.socket.openCount
        XCTAssertEqual(opens, 0)
        XCTAssertTrue(rig.audio.requests.isEmpty)
        await rig.end()
    }

    @MainActor
    func testMetaWithAMalformedKeyFailsWithItsText() async {
        let rig = StreamingRig.meta(key: "key with spaces")
        let failure = await rig.startFailure()

        XCTAssertEqual(failure?.message, "This key contains spaces or line breaks. Paste it again exactly as issued.")
        let opens = await rig.socket.openCount
        XCTAssertEqual(opens, 0)
        await rig.end()
    }

    @MainActor
    func testMetaThatDoesNotAcknowledgeFailsWithTheNotAcceptedText() async {
        let rig = StreamingRig.meta()
        await rig.socket.deliver(.text(#"{"type":"error","message":"hidden"}"#))
        let failure = await rig.startFailure()

        XCTAssertEqual(
            failure?.message,
            "Meta did not accept the connection. Check the API key, account access, and billing in Transcription."
        )
        XCTAssertTrue(rig.audio.requests.isEmpty)
        XCTAssertFalse(rig.audio.isRunning)
        // No `failed` event: the start reports its own outcome.
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines, ["status Connecting to Meta Muse · cloud…"])
        await rig.end()
    }

    @MainActor
    func testMetaThatCannotBeReachedFailsWithTheConnectionText() async {
        let rig = StreamingRig.meta(open: .failure(.unreachable))
        let failure = await rig.startFailure()

        XCTAssertEqual(
            failure?.message,
            "Could not connect directly to Meta. Check this iPhone's internet connection and retry. No PC is required for Meta transcription."
        )
        XCTAssertTrue(rig.audio.requests.isEmpty)
        await rig.end()
    }

    @MainActor
    func testRelayWithoutAPairingFailsWithTheNotPairedText() async {
        let rig = StreamingRig.relay(.nemotron, paired: false)
        let failure = await rig.startFailure()

        XCTAssertEqual(failure?.reason, .stream)
        XCTAssertEqual(failure?.message, "Pair this iPhone with the Windows relay first.")
        let opens = await rig.socket.openCount
        XCTAssertEqual(opens, 0)
        XCTAssertTrue(rig.audio.requests.isEmpty)
        await rig.end()
    }

    @MainActor
    func testRelayFailuresBeforeReadyCarryThePhonesTextForTheCode() async {
        let refused = StreamingRig.relay(.nemotron, open: .rejected(status: 401))
        let refusal = await refused.startFailure()
        XCTAssertEqual(refusal?.message, RelayMessages.text(for: RelayWireCode.unauthorized))
        XCTAssertEqual(refusal?.message, "The PC no longer recognizes this pairing. Scan its pairing QR once to reconnect.")
        await refused.end()

        let notReady = StreamingRig.relay(.nemotron)
        await notReady.socket.deliver(.text(#"{"type":"error","code":"MODEL_NOT_READY","message":"hidden"}"#))
        let failure = await notReady.startFailure()
        XCTAssertEqual(failure?.message, "The selected PC model is not ready or is busy. Retry after the other test stops.")
        XCTAssertFalse(failure?.message.contains("hidden") ?? true)
        XCTAssertTrue(notReady.audio.requests.isEmpty)
        await notReady.end()

        let otherModel = StreamingRig.relay(.nemotron)
        await otherModel.socket.deliver(.text(SpeechTest.relayReady("qwen3")))
        let mismatch = await otherModel.startFailure()
        XCTAssertEqual(
            mismatch?.message,
            "PC did not confirm the selected speech model. Update/restart LiveCue Desktop and its tunnel."
        )
        XCTAssertTrue(otherModel.audio.requests.isEmpty, "No microphone for a model that was not confirmed.")
        await otherModel.end()
    }

    @MainActor
    func testRelayThatKeepsLoadingPastTheLimitFailsWithTheTimeoutText() async {
        let rig = StreamingRig.relay(.nemotron)

        let starting = Task { await rig.startFailure() }
        await rig.socket.waitForReceive()
        await rig.socket.deliver(.text(SpeechTest.relayLoading(170)))
        await rig.clock.waitForSleepers()
        await rig.clock.advance(by: RelayConstants.speechConnectSeconds)
        let failure = await starting.value

        XCTAssertEqual(failure?.message, "Your PC did not answer in time. Check that LiveCue Desktop is running, then retry.")
        XCTAssertTrue(rig.audio.requests.isEmpty)
        let closes = await rig.socket.closeCalls
        XCTAssertEqual(closes.count, 1)
        await rig.end()
    }

    /// 1.1.0: a capture that says what it found is quoted, so the user reads why there is no microphone.
    @MainActor
    func testACaptureThatSaysWhatItFoundIsQuotedInTheStartFailure() async {
        let report = AudioStartReport(
            stage: .input,
            isInputAvailable: true,
            routeInputs: ["MicrophoneBuiltIn"],
            sampleRate: 0,
            channelCount: 0,
            converterBuilt: false
        )
        let rig = StreamingRig.meta()
        rig.audio.startFailure = .unusable(report)
        await rig.socket.deliver(.text(SpeechTest.metaAcknowledgement))
        let thrown = await rig.startFailure()

        XCTAssertEqual(
            thrown,
            TranscriberFailure(.noMicrophone, message: "No usable microphone. (input 0 Hz, 0 ch, route: MicrophoneBuiltIn)")
        )
        XCTAssertFalse(rig.audio.isRunning)
        await rig.socket.waitForCloseCalls(count: 1)
        await rig.end()
    }

    @MainActor
    func testWithoutAUsableMicrophoneTheStartFailsAndTheStreamIsClosed() async {
        for failure in [AudioCaptureFailure.noUsableMicrophone, AudioCaptureFailure.couldNotStart] {
            let rig = StreamingRig.meta()
            rig.audio.startFailure = failure
            await rig.socket.deliver(.text(SpeechTest.metaAcknowledgement))
            let thrown = await rig.startFailure()

            XCTAssertEqual(thrown, TranscriberFailure(.noMicrophone, message: "No usable microphone."))
            XCTAssertFalse(rig.audio.isRunning)
            await rig.socket.waitForCloseCalls(count: 1)

            // The usage stream the provider opened is closed as interrupted, and nothing says `failed`.
            let emitted = rig.transcriber.emittedEventCount
            await rig.log.wait(for: emitted)
            XCTAssertEqual(rig.log.lines, [
                "status Connecting to Meta Muse · cloud…",
                "status Meta direct · 0.00 s setup",
                "usage begin",
                "usage finish false"
            ])
            await rig.end()
        }
    }

    // MARK: - While running

    @MainActor
    func testCapturedBuffersAreSentInOrderAsFramesOfTheProvidersSize() async throws {
        let meta = StreamingRig.meta()
        try await meta.startStreaming()
        meta.audio.feed(.pcm16(Data(repeating: 1, count: 3_840)))
        meta.audio.feed(.pcm16(Data(repeating: 2, count: 3_840)))
        await meta.socket.waitForSentFrames(count: 2)
        let metaFrames = await meta.socket.sentFrames
        XCTAssertEqual(metaFrames, [Data(repeating: 1, count: 3_840), Data(repeating: 2, count: 3_840)])
        await meta.log.waitForLine("usage update sent=160.0 processed=- text=false")
        await meta.end()

        let relay = StreamingRig.relay(.nemotron)
        try await relay.startStreaming()
        // Buffers of any length become 80 ms frames of 2,560 bytes.
        relay.audio.feed(.pcm16(Data(repeating: 3, count: 2_000)))
        relay.audio.feed(.pcm16(Data(repeating: 3, count: 3_120)))
        await relay.socket.waitForSentFrames(count: 2)
        let relayFrames = await relay.socket.sentFrames
        XCTAssertEqual(relayFrames, [Data(repeating: 3, count: 2_560), Data(repeating: 3, count: 2_560)])
        await relay.end()
    }

    @MainActor
    func testSessionEventsMapOneToOneAndSegmentsCarryTheStreamOffset() async throws {
        let rig = StreamingRig.relay(.nemotron)
        try await rig.startStreaming(offset: 12.5)
        let started = rig.transcriber.emittedEventCount

        await rig.socket.deliver(.text(#"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#))
        await rig.socket.deliver(.text(#"{"type":"transcript","turnId":1,"audioProcessedMs":80,"transcript":"Local words"}"#))
        await rig.socket.deliver(.text(#"{"type":"speechComplete","turnId":1,"audioProcessedMs":160,"transcript":"Local words."}"#))
        await rig.log.waitForLine("partial ")

        XCTAssertEqual(Array(rig.log.lines.dropFirst(started)), [
            "usage update sent=- processed=0.0 text=false",
            "usage update sent=- processed=80.0 text=true",
            "partial Local words",
            "usage firstText 0.0",
            "status First words · 0.00 s from stream start",
            "usage update sent=- processed=160.0 text=true",
            "segment Local words. 12.50-12.66",
            "partial "
        ])
        XCTAssertTrue(rig.log.events.allSatisfy { $0.generation == rig.generation })
        await rig.end()
    }

    @MainActor
    func testLevelsOfTheCaptureArePassedOn() async throws {
        let rig = StreamingRig.meta()
        try await rig.startStreaming()
        rig.audio.send(.level(0.25))
        await rig.log.waitForLine("level 0.25")
        await rig.end()
    }

    // MARK: - Failures after the start

    @MainActor
    func testCaptureFailuresTearTheStreamDownAndFailLastWithTheirText() async throws {
        let rows: [(event: AudioCaptureEvent, reason: TranscriberFailure.Reason, message: String)] = [
            (.overflow, .uploadOverflow, "Network cannot keep up. Recording paused; resume to retry."),
            (.interrupted, .microphoneInterrupted, "Microphone interrupted. Resume when your call or other audio finishes."),
            (.routeLost, .routeChanged, "Microphone route changed. Resume to use the current microphone.")
        ]
        for provider in [SpeechProvider.meta, SpeechProvider.nemotron] {
            for row in rows {
                let rig = provider == .meta ? StreamingRig.meta() : StreamingRig.relay(provider)
                try await rig.startStreaming()

                rig.audio.send(row.event)
                let failed = "failed \(row.reason) \(row.message)"
                await rig.log.waitForLine(failed)

                // The usage stream is interrupted, the status says so, and `failed` comes last.
                XCTAssertEqual(Array(rig.log.lines.suffix(3)), [
                    "usage finish false",
                    "status Disconnected · resume to retry",
                    failed
                ], "\(provider) \(row.event)")
                XCTAssertEqual(rig.log.lines.count, rig.transcriber.emittedEventCount)
                // Capture is down and the audio session released; the socket was cut by the phone.
                XCTAssertEqual(rig.audio.stops, [true])
                XCTAssertFalse(rig.audio.isRunning)
                let closes = await rig.socket.closeCalls
                XCTAssertEqual(closes, [1001])
                // Nothing reconnects by itself.
                let opens = await rig.socket.openCount
                XCTAssertEqual(opens, 1)
                await rig.end()
            }
        }
    }

    @MainActor
    func testAStreamThatFailsByItselfPausesWithTheProvidersText() async throws {
        // A relay `error` frame: the phone's own text for the code.
        let relay = StreamingRig.relay(.nemotron)
        try await relay.startStreaming()
        await relay.socket.deliver(.text(#"{"type":"error","code":"LAB_DISCONNECTED","message":"hidden"}"#))
        await relay.log.waitForLine("failed stream Local model disconnected. Resume to reconnect.")
        XCTAssertEqual(Array(relay.log.lines.suffix(3)), [
            "usage finish false",
            "status Disconnected · resume to retry",
            "failed stream Local model disconnected. Resume to reconnect."
        ])
        XCTAssertEqual(relay.audio.stops, [true])
        XCTAssertFalse(relay.log.lines.contains { $0.contains("hidden") })
        await relay.end()

        // A Meta `error` event: the fixed provider text, never Meta's own.
        let meta = StreamingRig.meta()
        try await meta.startStreaming()
        await meta.socket.deliver(.text(#"{"type":"error","message":"hidden"}"#))
        await meta.log.waitForLine("failed stream Meta transcription stopped. Check API access, billing, or rate limits before retrying.")
        XCTAssertEqual(meta.audio.stops, [true])
        XCTAssertFalse(meta.log.lines.contains { $0.contains("hidden") })
        XCTAssertEqual(meta.log.lines.last?.hasPrefix("failed stream"), true)
        await meta.end()
    }

    @MainActor
    func testADroppedSocketPausesWithTheConnectionText() async throws {
        let relay = StreamingRig.relay(.qwen3)
        try await relay.startStreaming()
        await relay.socket.drop()
        await relay.log.waitForLine("failed stream The connection to your PC dropped. Resume to reconnect.")
        XCTAssertEqual(relay.audio.stops, [true])
        await relay.end()

        let meta = StreamingRig.meta()
        try await meta.startStreaming()
        await meta.socket.drop()
        await meta.log.waitForLine(
            "failed stream Could not connect directly to Meta. Check this iPhone's internet connection and retry. No PC is required for Meta transcription."
        )
        XCTAssertEqual(meta.audio.stops, [true])
        await meta.end()
    }

    @MainActor
    func testAFailedSendPausesWithTheAudioConnectionText() async throws {
        let rig = StreamingRig.meta()
        try await rig.startStreaming()
        await rig.socket.failSends()
        rig.audio.feed(.pcm16(Data(count: 3_840)))

        await rig.log.waitForLine("failed stream Audio connection failed. Resume to reconnect.")
        XCTAssertEqual(rig.log.lines.suffix(2).first, "status Disconnected · resume to retry")
        XCTAssertEqual(rig.audio.stops, [true])
        await rig.end()
    }

    @MainActor
    func testAStreamThatEndsAroundTheStartIsReportedExactlyOnce() async {
        // The acknowledgement and an error are both waiting: the stream is ready and gone at once.
        let providerText = "Meta transcription stopped. Check API access, billing, or rate limits before retrying."
        let rig = StreamingRig.meta()
        await rig.socket.deliver(.text(SpeechTest.metaAcknowledgement))
        await rig.socket.deliver(.text(#"{"type":"error","message":"hidden"}"#))

        let thrown = await rig.startFailure()
        if let thrown {
            // It ended before `start` returned: the start failed, and no `failed` event follows.
            XCTAssertEqual(thrown, TranscriberFailure(.stream, message: providerText))
            await rig.log.wait(for: rig.transcriber.emittedEventCount)
            XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("failed") })
        } else {
            // It ended after `start` returned: one `failed` event, the last of its generation.
            await rig.log.waitForLine("failed stream " + providerText)
            XCTAssertEqual(rig.log.lines.filter { $0.hasPrefix("failed") }.count, 1)
            XCTAssertEqual(rig.log.lines.last, "failed stream " + providerText)
        }
        XCTAssertFalse(rig.audio.isRunning)
        XCTAssertEqual(rig.audio.stops.count, rig.audio.requests.count, "A capture that was started was stopped.")
        await rig.end()
    }

    @MainActor
    func testResumeOpensANewStreamWithItsOwnUsage() async throws {
        let rig = StreamingRig.meta()
        try await rig.startStreaming()
        await rig.socket.drop()
        await rig.log.wait { rig.log.lines.contains { $0.hasPrefix("failed stream") } }
        let afterFailure = rig.log.lines.count
        var opens = await rig.socket.openCount
        XCTAssertEqual(opens, 1, "Nothing reconnects until Resume.")

        // Resume: a new generation and the elapsed seconds as the offset.
        let second = UUID()
        await rig.socket.deliver(.text(SpeechTest.metaAcknowledgement))
        try await rig.transcriber.start(TranscriberStart(generation: second, streamOffsetSeconds: 30))
        opens = await rig.socket.openCount
        XCTAssertEqual(opens, 2)
        XCTAssertEqual(rig.audio.requests.count, 2)

        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        let later = rig.log.events.dropFirst(afterFailure)
        XCTAssertTrue(later.allSatisfy { $0.generation == second })
        XCTAssertEqual(later.map(TranscriberEventLog.line), [
            "status Connecting to Meta Muse · cloud…",
            "status Meta direct · 0.00 s setup",
            "usage begin"
        ])

        // The session's usage gains one stream per start (AC-IOS-041).
        var usage = TranscriptionUsage(provider: "meta")
        for event in rig.log.events {
            if case .usage(let update) = event.kind {
                usage.apply(update)
            }
        }
        XCTAssertEqual(usage.streams.count, 2)
        await rig.end()
    }

    // MARK: - Finalize

    @MainActor
    func testFinalizeSendsTheQueueThenEndStreamAndReleasesTheAudioSession() async throws {
        let rig = StreamingRig.relay(.nemotron)
        try await rig.startStreaming()
        let started = rig.transcriber.emittedEventCount

        // A buffer that is still in the hand-off queue when Pause is tapped is sent all the same.
        rig.audio.feed(.pcm16(Data(repeating: 7, count: 3_560)))
        let finalizing = Task { try await rig.transcriber.finalize() }
        await rig.socket.waitForSentTexts(count: 1)

        XCTAssertEqual(rig.audio.stops, [true], "A streaming provider releases the audio session on Pause.")
        let frames = await rig.socket.sentFrames
        let texts = await rig.socket.sentTexts
        XCTAssertEqual(frames, [Data(repeating: 7, count: 2_560), Data(repeating: 7, count: 1_000)])
        XCTAssertEqual(texts, [#"{"type":"endStream"}"#])

        await rig.socket.deliver(.text(#"{"type":"streamComplete","audioProcessedMs":222.5,"finalizationMs":250}"#))
        await rig.socket.deliver(.closed(code: 1000, reason: ""))
        try await finalizing.value

        // Everything the finalisation caused was emitted before it returned, and nothing failed.
        let emitted = rig.transcriber.emittedEventCount
        await rig.log.wait(for: emitted)
        let lines = Array(rig.log.lines.dropFirst(started))
        XCTAssertEqual(Array(lines.suffix(4)), [
            "usage update sent=- processed=222.5 text=false",
            "usage finalization 250.0",
            "status Connected · 0.25 s setup · Final 0.25 s",
            "usage finish true"
        ])
        XCTAssertFalse(lines.contains { $0.hasPrefix("failed") })
        XCTAssertFalse(rig.audio.isRunning)
        XCTAssertEqual(rig.clock.sleeperCount, 0, "The limit of the finalisation is no longer waiting.")
        await rig.end()
    }

    @MainActor
    func testMetaFinalizeIsCompletedByANormalCloseAfterEndStream() async throws {
        let rig = StreamingRig.meta()
        try await rig.startStreaming()

        let finalizing = Task { try await rig.transcriber.finalize() }
        await rig.socket.waitForSentTexts(count: 2)
        let texts = await rig.socket.sentTexts
        XCTAssertEqual(texts.last, #"{"type":"endStream"}"#)
        await rig.socket.deliver(.closed(code: 1000, reason: ""))
        try await finalizing.value

        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(Array(rig.log.lines.suffix(3)), [
            "usage finalization 0.0",
            "status Meta direct · 0.00 s setup · Final 0.00 s",
            "usage finish true"
        ])
        XCTAssertEqual(rig.audio.stops, [true])
        await rig.end()
    }

    @MainActor
    func testFinalizeThatDoesNotEndInFifteenSecondsThrowsTheFinaliseText() async throws {
        let rig = StreamingRig.relay(.nemotron)
        try await rig.startStreaming()
        // A turn started and never gets its final text: that is what makes the limit a failure (IOS-A11).
        await rig.socket.deliver(.text(#"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#))
        await rig.socket.waitForReceive()

        let finalizing = Task { () -> TranscriberFailure? in
            do {
                try await rig.transcriber.finalize()
                return nil
            } catch {
                return error as? TranscriberFailure
            }
        }
        await rig.socket.waitForSentTexts(count: 1)
        // The PC never closes. Both limits of the finalisation are waiting.
        await rig.clock.waitForSleepers(count: 2)
        await rig.clock.advance(by: 15)
        let failure = await finalizing.value

        XCTAssertEqual(failure, TranscriberFailure(.stream, message: "Could not finalize the transcript. Some final words may be missing."))
        // The stream is recorded as interrupted, and the call reports the outcome itself.
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertTrue(rig.log.lines.contains("usage finish false"))
        XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("failed") })
        XCTAssertEqual(rig.audio.stops, [true])
        let closes = await rig.socket.closeCalls
        XCTAssertEqual(closes.count, 1)
        await rig.end()
    }

    @MainActor
    func testFinalizeEndedByAnotherCloseCodeThrowsTheFinaliseText() async throws {
        let rig = StreamingRig.meta()
        try await rig.startStreaming()
        // A turn started and never gets its final text: only then is such a close a failure (IOS-A11).
        await rig.socket.deliver(.text(#"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#))
        await rig.socket.waitForReceive()

        let finalizing = Task { () -> TranscriberFailure? in
            do {
                try await rig.transcriber.finalize()
                return nil
            } catch {
                return error as? TranscriberFailure
            }
        }
        await rig.socket.waitForSentTexts(count: 2)
        await rig.socket.deliver(.closed(code: 1011, reason: ""))
        let failure = await finalizing.value

        XCTAssertEqual(failure?.message, "Could not finalize the transcript. Some final words may be missing.")
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertTrue(rig.log.lines.contains("usage finish false"))
        XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("failed") }, "A failure inside finalize is the call's own outcome.")
        await rig.end()
    }

    // IOS-A11 as changed in 1.1.0: on a real iPhone the cloud provider did not close with code 1000, and
    // every Stop showed the finalise alert.

    @MainActor
    func testMetaFinalizeIsCompletedAfterAQuietPeriodWhenMetaDoesNotClose() async throws {
        let rig = StreamingRig.meta()
        try await rig.startStreaming()

        let finalizing = Task { try await rig.transcriber.finalize() }
        await rig.socket.waitForSentTexts(count: 2)
        // The two 15 s limits and the quiet period after `endStream`.
        await rig.clock.waitForSleepers(count: 3)
        await rig.clock.advance(by: SpeechStreamSession.quietSeconds)
        // No throw: Pause and Stop show no alert.
        try await finalizing.value

        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(Array(rig.log.lines.suffix(3)), [
            "usage finalization 0.0",
            "status Meta direct · 0.00 s setup · Final 0.00 s",
            "usage finish true"
        ])
        XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("failed") })
        let closes = await rig.socket.closeCalls
        XCTAssertEqual(closes, [1000], "The phone closes the stream itself.")
        XCTAssertEqual(rig.clock.sleeperCount, 0)
        XCTAssertEqual(rig.audio.stops, [true])
        await rig.end()
    }

    @MainActor
    func testMetaFinalizeIsCompletedByACloseWithoutAStatusCodeOrADrop() async throws {
        // No status code (1005), going away (1001), another code with nothing missing, and a drop.
        let endings: [WebSocketMessage?] = [
            .closed(code: 1005, reason: ""),
            .closed(code: 1001, reason: ""),
            .closed(code: 1011, reason: ""),
            nil
        ]
        for ending in endings {
            let rig = StreamingRig.meta()
            try await rig.startStreaming()
            // A turn that got its final text before Stop.
            await rig.socket.deliver(.text(#"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#))
            await rig.socket.deliver(.text(#"{"type":"speechComplete","turnId":1,"audioProcessedMs":900,"transcript":"All of it."}"#))
            await rig.socket.waitForReceive()

            let finalizing = Task { try await rig.transcriber.finalize() }
            await rig.socket.waitForSentTexts(count: 2)
            if let ending {
                await rig.socket.deliver(ending)
            } else {
                await rig.socket.drop()
            }
            try await finalizing.value

            await rig.log.wait(for: rig.transcriber.emittedEventCount)
            XCTAssertEqual(rig.log.lines.last, "usage finish true", "\(String(describing: ending))")
            XCTAssertTrue(rig.log.lines.contains { $0.hasPrefix("segment All of it.") })
            XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("failed") })
            await rig.end()
        }
    }

    @MainActor
    func testMetaFinalizeWaitsForTheFinalTextOfAStartedTurn() async throws {
        let rig = StreamingRig.meta()
        try await rig.startStreaming()
        await rig.socket.deliver(.text(#"{"type":"speechStart","turnId":1,"audioProcessedMs":0}"#))
        await rig.socket.waitForReceive()

        let finalizing = Task { try await rig.transcriber.finalize() }
        await rig.socket.waitForSentTexts(count: 2)
        // Only the two limits wait: the turn has no final text, so no quiet period runs.
        await rig.clock.waitForSleepers(count: 2)
        await rig.clock.advance(by: 4)
        XCTAssertEqual(rig.clock.sleeperCount, 2)

        await rig.socket.deliver(.text(#"{"type":"speechComplete","turnId":1,"audioProcessedMs":900,"transcript":"The last words."}"#))
        await rig.clock.waitForSleepers(count: 3)
        await rig.clock.advance(by: SpeechStreamSession.quietSeconds)
        try await finalizing.value

        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertTrue(rig.log.lines.contains { $0.hasPrefix("segment The last words.") })
        XCTAssertEqual(rig.log.lines.last, "usage finish true")
        await rig.end()
    }

    @MainActor
    func testRelayFinalizeThatTimesOutWithNothingMissingIsCompleted() async throws {
        let rig = StreamingRig.relay(.nemotron)
        try await rig.startStreaming()

        let finalizing = Task { try await rig.transcriber.finalize() }
        await rig.socket.waitForSentTexts(count: 1)
        // The PC never closes and never says `streamComplete`. No turn started, so nothing is missing.
        await rig.clock.waitForSleepers(count: 2)
        await rig.clock.advance(by: 15)
        try await finalizing.value

        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines.last, "usage finish true")
        XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("failed") })
        let closes = await rig.socket.closeCalls
        XCTAssertEqual(closes.count, 1)
        XCTAssertEqual(rig.clock.sleeperCount, 0)
        await rig.end()
    }

    @MainActor
    func testFinalizeWithNothingRunningDoesNothing() async throws {
        let rig = StreamingRig.meta()
        try await rig.transcriber.finalize()
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
        XCTAssertTrue(rig.audio.stops.isEmpty)

        // Also after a failure: the stream is down already.
        try await rig.startStreaming()
        rig.audio.send(.interrupted)
        await rig.log.wait { rig.log.lines.contains { $0.hasPrefix("failed") } }
        let emitted = rig.transcriber.emittedEventCount
        try await rig.transcriber.finalize()
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        XCTAssertEqual(rig.audio.stops, [true])
        await rig.end()
    }

    // MARK: - Shutdown and the rest of the interface

    @MainActor
    func testShutdownStopsEverythingAndEmitsNothing() async throws {
        let rig = StreamingRig.relay(.nemotron)
        try await rig.startStreaming()
        let emitted = rig.transcriber.emittedEventCount

        await rig.transcriber.shutdown()
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        XCTAssertEqual(rig.audio.stops, [true])
        XCTAssertFalse(rig.audio.isRunning)
        let closes = await rig.socket.closeCalls
        XCTAssertEqual(closes, [1001])

        // Idempotent.
        await rig.transcriber.shutdown()
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        XCTAssertEqual(rig.audio.stops, [true, true])
        await rig.end()
    }

    @MainActor
    func testStreamingProvidersHaveNothingPending() async throws {
        let meta = StreamingRig.meta()
        let relay = StreamingRig.relay(.nemotron)
        let metaSeconds = try await meta.transcriber.transcribePending()
        let relaySeconds = try await relay.transcriber.transcribePending()
        XCTAssertEqual(metaSeconds, 0)
        XCTAssertEqual(relaySeconds, 0)
        XCTAssertEqual(meta.transcriber.emittedEventCount, 0)
        await meta.end()
        await relay.end()
    }

    @MainActor
    func testTheLiveFactoryBuildsTheTranscriberOfEachProvider() async {
        let clock = ManualClock()
        let models = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: FakeBatchEngine(), parakeet: FakeStreamingEngine()),
            activity: SessionActivity(),
            clock: clock
        )
        let factory = LiveTranscriberFactory(
            audio: FakeAudioCapture(),
            transport: ScriptedSpeechSocket(),
            secrets: FakeSecrets(),
            pairingStore: FakePairingStore(),
            clock: clock,
            models: models
        )

        XCTAssertTrue(factory.makeTranscriber(for: .meta) is MetaTranscriber)
        XCTAssertTrue(factory.makeTranscriber(for: .nemotron) is RelayTranscriber)
        XCTAssertTrue(factory.makeTranscriber(for: .qwen3) is RelayTranscriber)
        XCTAssertTrue(factory.makeTranscriber(for: .voz) is VozTranscriber)
        XCTAssertTrue(factory.makeTranscriber(for: .parakeet) is ParakeetTranscriber)
        XCTAssertTrue(factory.makeTranscriber(for: .apple) is AppleTranscriber)
        // Fluid Speech Mini has the transcriber of Parakeet: its recogniser streams too (D-29).
        XCTAssertTrue(factory.makeTranscriber(for: .fluid) is ParakeetTranscriber)
        for provider in SpeechProvider.allCases {
            XCTAssertEqual(factory.makeTranscriber(for: provider).provider, provider)
        }

        // Nothing is loaded: an on-device transcriber says what is missing.
        let missing = [
            (SpeechProvider.voz, "Prepare Voz first."),
            (SpeechProvider.parakeet, "Prepare Parakeet first."),
            (SpeechProvider.apple, "Prepare Apple speech first."),
            (SpeechProvider.fluid, "Prepare Fluid Speech Mini first.")
        ]
        for (provider, text) in missing {
            let transcriber = factory.makeTranscriber(for: provider)
            do {
                try await transcriber.start(TranscriberStart(generation: UUID(), streamOffsetSeconds: 0))
                XCTFail("The start should have failed.")
            } catch {
                XCTAssertEqual(error as? TranscriberFailure, TranscriberFailure(.notPrepared, message: text))
            }
        }
    }

    // MARK: - Status lines

    @MainActor
    func testStatusLinesReadAsTheArchitectureSaysThem() async {
        XCTAssertEqual(TranscriberStatusText.connecting(.meta), "Connecting to Meta Muse · cloud…")
        XCTAssertEqual(TranscriberStatusText.connecting(.parakeet), "Connecting to Parakeet · iPhone…")
        XCTAssertEqual(
            TranscriberStatusText.loading(.qwen3, seconds: 7),
            "Loading Qwen3 1.7B on PC · 7 s. Microphone starts when ready."
        )
        XCTAssertEqual(TranscriberStatusText.connected(.meta, setupSeconds: 1.234), "Meta direct · 1.23 s setup")
        XCTAssertEqual(TranscriberStatusText.connected(.nemotron, setupSeconds: 0.5), "Connected · 0.50 s setup")
        XCTAssertEqual(TranscriberStatusText.firstWords(seconds: 2), "First words · 2.00 s from stream start")
        XCTAssertEqual(
            TranscriberStatusText.appendingFinal("First words · 2.00 s from stream start", seconds: 0.31),
            "First words · 2.00 s from stream start · Final 0.31 s"
        )
        XCTAssertEqual(TranscriberStatusText.lastBuffer(milliseconds: 41.6), "Last buffer processing: 42 ms")
        XCTAssertEqual(
            TranscriberStatusText.batch(audioSeconds: 12.34, transcriptionSeconds: 0.876),
            "12.3 s audio → 0.88 s transcription"
        )
        XCTAssertEqual(TranscriberStatusText.recordingOnDevice, "Recording on this iPhone")
        XCTAssertEqual(TranscriberStatusText.disconnected, "Disconnected · resume to retry")
        XCTAssertEqual(TranscriberStatusText.finishing, "Finishing the transcript…")

        // A time is never negative, and a value that is not a number shows as zero.
        XCTAssertEqual(TranscriberStatusText.connected(.meta, setupSeconds: -1), "Meta direct · 0.00 s setup")
        XCTAssertEqual(TranscriberStatusText.lastBuffer(milliseconds: .nan), "Last buffer processing: 0 ms")
        XCTAssertEqual(TranscriberStatusText.loading(.nemotron, seconds: -3), "Loading Nemotron 3.5 on PC · 0 s. Microphone starts when ready.")
    }
}

// MARK: - The audio pipeline

/// `AudioTapConverter` and `AudioSessionPolicy` with buffers made by hand: the conversion per provider,
/// the bounded hand-off queue, the level rule and the two notifications. The engine, the audio session
/// and the microphone are not touched.
final class AudioPipelineTests: XCTestCase {
    @MainActor
    func testThePolicyIsPlayAndRecordInMeasurementMode() async {
        XCTAssertEqual(AudioSessionPolicy.category, .playAndRecord)
        XCTAssertEqual(AudioSessionPolicy.mode, .measurement)
        XCTAssertTrue(AudioSessionPolicy.options.contains(.defaultToSpeaker))
        XCTAssertTrue(AudioSessionPolicy.options.contains(.allowBluetooth))
        XCTAssertEqual(AudioSessionPolicy.tapBufferFrames, 2_048)
        XCTAssertEqual(AudioSessionPolicy.levelsPerSecond, 24)
    }

    @MainActor
    func testOnlyAnInterruptionThatBeganIsReported() async {
        let began = Notification(
            name: AVAudioSession.interruptionNotification,
            object: nil,
            userInfo: [AVAudioSessionInterruptionTypeKey: AVAudioSession.InterruptionType.began.rawValue]
        )
        let ended = Notification(
            name: AVAudioSession.interruptionNotification,
            object: nil,
            userInfo: [AVAudioSessionInterruptionTypeKey: AVAudioSession.InterruptionType.ended.rawValue]
        )
        let empty = Notification(name: AVAudioSession.interruptionNotification)

        XCTAssertEqual(AudioSessionPolicy.event(forInterruption: began), .interrupted)
        XCTAssertNil(AudioSessionPolicy.event(forInterruption: ended), "Nothing resumes by itself.")
        XCTAssertNil(AudioSessionPolicy.event(forInterruption: empty))
    }

    @MainActor
    func testOnlyADeviceThatWentAwayIsARouteLoss() async {
        func change(_ reason: AVAudioSession.RouteChangeReason) -> Notification {
            Notification(
                name: AVAudioSession.routeChangeNotification,
                object: nil,
                userInfo: [AVAudioSessionRouteChangeReasonKey: reason.rawValue]
            )
        }

        XCTAssertEqual(AudioSessionPolicy.event(forRouteChange: change(.oldDeviceUnavailable)), .routeLost)
        XCTAssertNil(AudioSessionPolicy.event(forRouteChange: change(.newDeviceAvailable)))
        XCTAssertNil(AudioSessionPolicy.event(forRouteChange: change(.categoryChange)))
        XCTAssertNil(AudioSessionPolicy.event(forRouteChange: Notification(name: AVAudioSession.routeChangeNotification)))
    }

    @MainActor
    func testTheObserversReportIntoTheEventStream() async throws {
        let format = try XCTUnwrap(AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16_000, channels: 1, interleaved: false))
        let converter = try XCTUnwrap(
            AudioTapConverter(inputFormat: format, request: AudioCaptureRequest(target: .float32(sampleRate: 16_000), queueLimit: 4))
        )
        let interruption = AudioSessionPolicy.interruptionObserver(reportingTo: converter)
        let routeChange = AudioSessionPolicy.routeChangeObserver(reportingTo: converter)

        interruption(
            Notification(
                name: AVAudioSession.interruptionNotification,
                object: nil,
                userInfo: [AVAudioSessionInterruptionTypeKey: AVAudioSession.InterruptionType.began.rawValue]
            )
        )
        routeChange(
            Notification(
                name: AVAudioSession.routeChangeNotification,
                object: nil,
                userInfo: [AVAudioSessionRouteChangeReasonKey: AVAudioSession.RouteChangeReason.oldDeviceUnavailable.rawValue]
            )
        )
        routeChange(
            Notification(
                name: AVAudioSession.routeChangeNotification,
                object: nil,
                userInfo: [AVAudioSessionRouteChangeReasonKey: AVAudioSession.RouteChangeReason.newDeviceAvailable.rawValue]
            )
        )
        converter.finish()

        let events = await SpeechTest.collected(converter.streams.events)
        XCTAssertEqual(events, [.interrupted, .routeLost])
    }

    @MainActor
    func testHardwareBuffersBecomePCM16At24kHzForMeta() async throws {
        // What an iPhone microphone delivers: Float32, 48 kHz, one channel. One second of a 440 Hz tone
        // at half of full scale, in ten buffers.
        let format = try XCTUnwrap(AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48_000, channels: 1, interleaved: false))
        let converter = try XCTUnwrap(
            AudioTapConverter(inputFormat: format, request: AudioCaptureRequest(target: .pcm16(sampleRate: 24_000), queueLimit: 64))
        )
        let tap = AudioTapConverter.makeTapBlock(for: converter)
        let step: Float = 2 * Float.pi * 440 / 48_000
        for index in 0..<10 {
            let buffer = try makeBuffer(format, frames: 4_800) { frame in
                0.5 * sin(step * Float(index * 4_800 + frame))
            }
            tap(buffer, AVAudioTime(sampleTime: AVAudioFramePosition(index * 4_800), atRate: 48_000))
        }
        converter.finish()

        let chunks = await SpeechTest.collected(converter.streams.chunks)
        var bytes = 0
        for chunk in chunks {
            guard case .pcm16(let data) = chunk else {
                XCTFail("A streaming target gets PCM16 bytes.")
                return
            }
            XCTAssertEqual(data.count % 2, 0)
            bytes += data.count
        }
        // One second at 24 kHz, 16 bits, one channel: 48,000 bytes, less what the resampler still holds.
        XCTAssertEqual(Double(bytes), 48_000, accuracy: 2_400)

        // The level is the RMS of a converted buffer: 0.5 / √2 for a sine.
        let events = await SpeechTest.collected(converter.streams.events)
        let levels: [Float] = events.compactMap { event in
            if case .level(let value) = event { return value }
            return nil
        }
        XCTAssertEqual(events.count, levels.count, "Nothing but levels: the queue had room.")
        XCTAssertGreaterThanOrEqual(levels.count, 5)
        XCTAssertLessThanOrEqual(levels.count, 10)
        XCTAssertEqual(try XCTUnwrap(levels.last), 0.3536, accuracy: 0.03)
    }

    @MainActor
    func testHardwareBuffersBecomeFloat32At16kHzForOnDeviceProviders() async throws {
        // Two channels at 44.1 kHz, as some external microphones deliver.
        let format = try XCTUnwrap(AVAudioFormat(standardFormatWithSampleRate: 44_100, channels: 2))
        let converter = try XCTUnwrap(
            AudioTapConverter(inputFormat: format, request: AudioCaptureRequest(target: .float32(sampleRate: 16_000), queueLimit: 128))
        )
        let tap = AudioTapConverter.makeTapBlock(for: converter)
        let step: Float = 2 * Float.pi * 300 / 44_100
        for index in 0..<10 {
            let buffer = try makeBuffer(format, frames: 4_410) { frame in
                0.25 * sin(step * Float(index * 4_410 + frame))
            }
            tap(buffer, AVAudioTime(sampleTime: AVAudioFramePosition(index * 4_410), atRate: 44_100))
        }
        converter.finish()

        let chunks = await SpeechTest.collected(converter.streams.chunks)
        var samples: [Float] = []
        for chunk in chunks {
            guard case .float32(let values) = chunk else {
                XCTFail("An on-device target gets Float samples.")
                return
            }
            samples.append(contentsOf: values)
        }
        // One second at 16 kHz, one channel, and still the tone: neither silence nor clipped.
        XCTAssertEqual(Double(samples.count), 16_000, accuracy: 800)
        let peak = samples.map { abs($0) }.max() ?? 0
        XCTAssertGreaterThan(peak, 0.2)
        XCTAssertLessThanOrEqual(peak, 0.6)
    }

    @MainActor
    func testAFullQueueKeepsTheOldestBuffersAndSaysSoOnce() async throws {
        // Input and target are the same format, so every chunk is its buffer, sample for sample.
        let format = try XCTUnwrap(AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16_000, channels: 1, interleaved: false))
        let converter = try XCTUnwrap(
            AudioTapConverter(inputFormat: format, request: AudioCaptureRequest(target: .float32(sampleRate: 16_000), queueLimit: 3))
        )
        let tap = AudioTapConverter.makeTapBlock(for: converter)
        // Nobody reads: the fourth, fifth and sixth buffer do not fit.
        for index in 0..<6 {
            let buffer = try makeBuffer(format, frames: 800) { _ in Float(index + 1) / 10 }
            tap(buffer, AVAudioTime(sampleTime: AVAudioFramePosition(index * 800), atRate: 16_000))
        }
        converter.finish()

        let chunks = await SpeechTest.collected(converter.streams.chunks)
        XCTAssertEqual(chunks.count, 3, "Buffers already queued stay readable after the capture stopped.")
        for (index, chunk) in chunks.enumerated() {
            guard case .float32(let values) = chunk else {
                XCTFail("Float samples were asked for.")
                return
            }
            // The first three buffers, in order: the newer ones were refused.
            let expected = Float(index + 1) / 10
            XCTAssertFalse(values.isEmpty)
            XCTAssertTrue(values.allSatisfy { $0 == expected }, "Chunk \(index) is not buffer \(index).")
        }

        let events = await SpeechTest.collected(converter.streams.events)
        XCTAssertEqual(events.filter { $0 == .overflow }.count, 1, "The overflow is said once.")
    }

    @MainActor
    func testLevelsArePublishedAtMostTwentyFourTimesASecond() async throws {
        let format = try XCTUnwrap(AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16_000, channels: 1, interleaved: false))
        let converter = try XCTUnwrap(
            AudioTapConverter(inputFormat: format, request: AudioCaptureRequest(target: .float32(sampleRate: 16_000), queueLimit: 128))
        )
        let tap = AudioTapConverter.makeTapBlock(for: converter)
        // One second of audio in a hundred buffers of 10 ms.
        for index in 0..<100 {
            let buffer = try makeBuffer(format, frames: 160) { _ in 0.5 }
            tap(buffer, AVAudioTime(sampleTime: AVAudioFramePosition(index * 160), atRate: 16_000))
        }
        converter.finish()

        let events = await SpeechTest.collected(converter.streams.events)
        // The first buffer publishes; after that one level per 667 frames or more, which is every fifth
        // buffer here. A hundred buffers made a hundred chunks and far fewer levels.
        XCTAssertLessThanOrEqual(events.count, 24)
        XCTAssertGreaterThanOrEqual(events.count, 12)
        XCTAssertEqual(events.first, .level(0.5), "The level of a constant 0.5 is 0.5.")
        XCTAssertTrue(events.allSatisfy { $0 == .level(0.5) })
        let chunks = await SpeechTest.collected(converter.streams.chunks)
        XCTAssertEqual(chunks.count, 100, "Every buffer is handed on; only the level is thinned.")
    }

    @MainActor
    func testTheLevelIsTheRootMeanSquareBetweenZeroAndOne() async {
        func floatLevel(_ samples: [Float]) -> Float {
            samples.withUnsafeBufferPointer { AudioTapConverter.level(ofFloat: $0) }
        }
        func pcmLevel(_ samples: [Int16]) -> Float {
            samples.withUnsafeBufferPointer { AudioTapConverter.level(ofPCM16: $0) }
        }

        XCTAssertEqual(floatLevel([0.5, -0.5, 0.5, -0.5]), 0.5, accuracy: 0.0001)
        XCTAssertEqual(floatLevel([0, 0, 0]), 0)
        XCTAssertEqual(floatLevel([]), 0)
        XCTAssertEqual(floatLevel([3, -3]), 1, "Clamped to 1.")
        XCTAssertEqual(floatLevel([.nan, 0.5]), 0)
        // 16-bit samples are divided by 32,768.
        XCTAssertEqual(pcmLevel([16_384, -16_384]), 0.5, accuracy: 0.0001)
        XCTAssertEqual(pcmLevel([Int16.min, Int16.min]), 1, accuracy: 0.0001)
        XCTAssertEqual(pcmLevel([]), 0)
    }

    @MainActor
    func testATargetWithoutASampleRateIsRefused() async throws {
        let format = try XCTUnwrap(AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48_000, channels: 1, interleaved: false))
        XCTAssertTrue(AudioSessionPolicy.isUsable(format))
        XCTAssertNil(AudioTapConverter(inputFormat: format, request: AudioCaptureRequest(target: .pcm16(sampleRate: 0), queueLimit: 64)))
    }

    /// A Float32 buffer with the same value in every channel.
    private func makeBuffer(_ format: AVAudioFormat, frames: Int, value: (Int) -> Float) throws -> AVAudioPCMBuffer {
        let buffer = try XCTUnwrap(AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(frames)))
        buffer.frameLength = AVAudioFrameCount(frames)
        let channels = try XCTUnwrap(buffer.floatChannelData)
        for channel in 0..<Int(format.channelCount) {
            for frame in 0..<frames {
                channels[channel][frame] = value(frame)
            }
        }
        return buffer
    }
}

// MARK: - Shared helpers of the speech tests

/// Small helpers the five speech test files share.
enum SpeechTest {
    /// How long a wait for something expected may take before the test is failed. Real waits end in
    /// milliseconds; the limit only keeps a wrong expectation from holding the whole run.
    static let patienceNanoseconds: UInt64 = 20_000_000_000

    static let metaAcknowledgement = #"{"sessionId":"synthetic-session"}"#

    static func relayLoading(_ elapsedSeconds: Int) -> String {
        #"{"type":"loading","provider":"nemotron","elapsedSeconds":\#(elapsedSeconds),"message":"Loading on the PC."}"#
    }

    static func relayReady(_ provider: String) -> String {
        #"{"type":"ready","provider":"\#(provider)","sampleRate":16000,"handshakeMs":250}"#
    }

    /// Everything a stream produces, to its end.
    static func collected<Element: Sendable>(_ stream: AsyncStream<Element>) async -> [Element] {
        var all: [Element] = []
        for await element in stream {
            all.append(element)
        }
        return all
    }

    /// Lets tasks that are already waiting for the main actor run. For work that never leaves the main
    /// actor; anything else is awaited through an event or a fake's own wait.
    @MainActor static func settle() async {
        for _ in 0..<20 {
            await Task.yield()
        }
    }

    /// Waits for something that happens on another executor and gives no signal of its own: asks,
    /// yields, asks again.
    @MainActor static func eventually(
        _ isMet: @escaping @MainActor () async -> Bool,
        file: StaticString = #filePath,
        line: UInt = #line
    ) async {
        for _ in 0..<200_000 {
            if await isMet() {
                return
            }
            await Task.yield()
        }
        XCTFail("The condition was never met.", file: file, line: line)
    }

    /// Returns when a condition on observable state holds. It is tried again whenever a value it read
    /// changes, so no test sleeps or guesses. After `patienceNanoseconds` the test is failed.
    @MainActor static func waitUntil(
        _ isMet: @escaping @MainActor () -> Bool,
        file: StaticString = #filePath,
        line: UInt = #line
    ) async {
        let wait = ObservedChangeWait()
        let watchdog = Task { @MainActor in
            try? await Task.sleep(nanoseconds: SpeechTest.patienceNanoseconds)
            if !Task.isCancelled {
                wait.giveUp()
            }
        }
        while !isMet() {
            if wait.gaveUp {
                XCTFail("The observed state never became what the test expects.", file: file, line: line)
                break
            }
            await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
                wait.hold(continuation)
                withObservationTracking {
                    _ = isMet()
                } onChange: {
                    // Called just before the value changes: look again once the change is made.
                    Task { @MainActor in
                        wait.wake()
                    }
                }
            }
        }
        watchdog.cancel()
    }
}

/// The one waiter of `SpeechTest.waitUntil`: woken when something observed changed, or when patience
/// ran out.
@MainActor final class ObservedChangeWait {
    private var held: CheckedContinuation<Void, Never>?
    private(set) var gaveUp = false

    func hold(_ continuation: CheckedContinuation<Void, Never>) {
        held = continuation
    }

    func wake() {
        let continuation = held
        held = nil
        continuation?.resume()
    }

    func giveUp() {
        gaveUp = true
        wake()
    }
}

/// Reads a transcriber's events as they are emitted and lets a test wait for the one it expects.
@MainActor final class TranscriberEventLog {
    private struct Waiter {
        let id: Int
        let isMet: () -> Bool
        let continuation: CheckedContinuation<Void, Never>
    }

    private(set) var events: [TranscriberEvent] = []
    private var waiters: [Waiter] = []
    private var nextWaiterID = 0
    private var reading: Task<Void, Never>?

    init(_ transcriber: any SpeechTranscriber) {
        let stream = transcriber.events
        reading = Task { [weak self] in
            for await event in stream {
                self?.record(event)
            }
        }
    }

    /// One line per event, in order.
    var lines: [String] {
        events.map { TranscriberEventLog.line($0) }
    }

    /// Returns when at least `count` events were read.
    func wait(for count: Int, file: StaticString = #filePath, line: UInt = #line) async {
        await wait(until: { self.events.count >= count }, file: file, line: line)
    }

    func waitForLine(_ expected: String, file: StaticString = #filePath, line: UInt = #line) async {
        await wait(until: { self.lines.contains(expected) }, file: file, line: line)
    }

    /// Returns when the condition holds. After `SpeechTest.patienceNanoseconds` it fails the test, says
    /// what was read so far, and returns.
    func wait(until isMet: @escaping () -> Bool, file: StaticString = #filePath, line: UInt = #line) async {
        if isMet() { return }
        nextWaiterID += 1
        let id = nextWaiterID
        let watchdog = Task {
            try? await Task.sleep(nanoseconds: SpeechTest.patienceNanoseconds)
            if !Task.isCancelled {
                self.giveUp(id, file: file, line: line)
            }
        }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiters.append(Waiter(id: id, isMet: isMet, continuation: continuation))
        }
        watchdog.cancel()
    }

    func stop() {
        reading?.cancel()
        reading = nil
    }

    private func record(_ event: TranscriberEvent) {
        events.append(event)
        var stillWaiting: [Waiter] = []
        var ready: [Waiter] = []
        for waiter in waiters {
            if waiter.isMet() {
                ready.append(waiter)
            } else {
                stillWaiting.append(waiter)
            }
        }
        waiters = stillWaiting
        for waiter in ready {
            waiter.continuation.resume()
        }
    }

    private func giveUp(_ id: Int, file: StaticString, line: UInt) {
        guard let index = waiters.firstIndex(where: { $0.id == id }) else { return }
        let waiter = waiters.remove(at: index)
        XCTFail("The expected event did not arrive. Read so far: \(lines)", file: file, line: line)
        waiter.continuation.resume()
    }

    nonisolated static func line(_ event: TranscriberEvent) -> String {
        switch event.kind {
        case .status(let line):
            return "status \(line)"
        case .partial(let text):
            return "partial \(text)"
        case .segment(let segment):
            return "segment \(segment.text) \(seconds(segment.startSeconds))-\(seconds(segment.endSeconds))"
        case .level(let value):
            return "level \(value)"
        case .usage(let update):
            switch update {
            case .begin:
                return "usage begin"
            case .update(_, let sentMs, let processedMs, let hasTranscript):
                return "usage update sent=\(optional(sentMs)) processed=\(optional(processedMs)) text=\(hasTranscript)"
            case .finish(_, let completed):
                return "usage finish \(completed)"
            case .firstText(let milliseconds):
                return "usage firstText \(milliseconds)"
            case .finalization(let milliseconds):
                return "usage finalization \(milliseconds)"
            }
        case .lastChunk(let milliseconds):
            return "lastChunk \(milliseconds)"
        case .failed(let failure):
            return "failed \(failure.reason) \(failure.message)"
        }
    }

    private nonisolated static func seconds(_ value: Double) -> String {
        String(format: "%.2f", locale: Locale(identifier: "en_US_POSIX"), value)
    }

    private nonisolated static func optional(_ value: Double?) -> String {
        guard let value else { return "-" }
        return "\(value)"
    }
}

/// A streaming transcriber on fakes: the real `MetaTranscriber` or `RelayTranscriber`, a hand-fed
/// microphone, a scripted socket and a clock that only moves when told.
@MainActor final class StreamingRig {
    let clock: ManualClock
    let audio: FakeAudioCapture
    let socket: ScriptedSpeechSocket
    let transcriber: any SpeechTranscriber
    let log: TranscriberEventLog
    /// The generation of `start()`.
    let generation = UUID()

    private init(clock: ManualClock, audio: FakeAudioCapture, socket: ScriptedSpeechSocket, transcriber: any SpeechTranscriber) {
        self.clock = clock
        self.audio = audio
        self.socket = socket
        self.transcriber = transcriber
        self.log = TranscriberEventLog(transcriber)
    }

    static func meta(key: String? = "synthetic-meta-unit-key", open: ScriptedSpeechSocket.OpenResult = .connection) -> StreamingRig {
        let clock = ManualClock()
        let audio = FakeAudioCapture()
        let socket = ScriptedSpeechSocket(open: open)
        var stored: [SecretKind: String] = [:]
        if let key {
            stored[.metaAPIKey] = key
        }
        let transcriber = MetaTranscriber(audio: audio, transport: socket, secrets: FakeSecrets(stored), clock: clock)
        return StreamingRig(clock: clock, audio: audio, socket: socket, transcriber: transcriber)
    }

    static func relay(
        _ model: SpeechProvider,
        paired: Bool = true,
        open: ScriptedSpeechSocket.OpenResult = .connection
    ) -> StreamingRig {
        let clock = ManualClock()
        let audio = FakeAudioCapture()
        let socket = ScriptedSpeechSocket(open: open)
        let transcriber = RelayTranscriber(
            model: model,
            audio: audio,
            transport: socket,
            pairingStore: FakePairingStore(paired ? FixtureValues.pairing : nil),
            clock: clock
        )
        return StreamingRig(clock: clock, audio: audio, socket: socket, transcriber: transcriber)
    }

    func start(offset: Double = 0) async throws {
        try await transcriber.start(TranscriberStart(generation: generation, streamOffsetSeconds: offset))
    }

    /// The failure a start throws; nil when it returned.
    func startFailure() async -> TranscriberFailure? {
        do {
            try await start()
            return nil
        } catch {
            XCTAssertTrue(error is TranscriberFailure, "A start throws nothing but TranscriberFailure.")
            return error as? TranscriberFailure
        }
    }

    /// Starts with the provider's answer already waiting, and waits until the start's events were read.
    func startStreaming(offset: Double = 0) async throws {
        if transcriber.provider == .meta {
            await socket.deliver(.text(SpeechTest.metaAcknowledgement))
        } else {
            await socket.deliver(.text(SpeechTest.relayReady(transcriber.provider.rawValue)))
        }
        try await start(offset: offset)
        await log.wait(for: transcriber.emittedEventCount)
    }

    func end() async {
        await transcriber.shutdown()
        log.stop()
    }
}

/// A scripted WebSocket that records what was sent. It opens no socket.
///
/// Messages given to `deliver` wait in a queue until `receive()` asks for them. After a `.closed` message
/// was received, and after the client called `close`, `receive()` and the sends throw
/// `TransportFailure.other`. A later `open` is a new connection on the same script, so a test can follow
/// a stream that is opened again.
actor ScriptedSpeechSocket: WebSocketTransport {
    enum OpenResult: Sendable {
        case connection
        case rejected(status: Int)
        case failure(TransportFailure)
    }

    private enum Condition {
        case texts(Int)
        case frames(Int)
        case closes(Int)
        case receiving
    }

    private struct Waiter {
        let id: Int
        let condition: Condition
        let continuation: CheckedContinuation<Void, Never>
    }

    private let openResult: OpenResult
    private var inbound: [Result<WebSocketMessage, TransportFailure>] = []
    private var receiver: CheckedContinuation<WebSocketMessage, any Error>?
    private var sendsFail = false
    private var ended = true
    private var waiters: [Waiter] = []
    private var nextWaiterID = 0

    private(set) var openCount = 0
    private(set) var openedURL: URL?
    private(set) var openedHeaders: [String: String] = [:]
    /// Text messages sent by the client, in order.
    private(set) var sentTexts: [String] = []
    /// Binary messages sent by the client, in order.
    private(set) var sentFrames: [Data] = []
    /// The code of every `close`, in order.
    private(set) var closeCalls: [Int] = []

    init(open: OpenResult = .connection) {
        openResult = open
    }

    // MARK: Scripting

    /// The next `receive()` returns it.
    func deliver(_ message: WebSocketMessage) {
        push(.success(message))
    }

    /// The next `receive()` throws: the socket dropped with no close frame.
    func drop() {
        push(.failure(.other))
    }

    /// Later sends throw.
    func failSends() {
        sendsFail = true
    }

    // MARK: Waiting

    func waitForSentTexts(count: Int, file: StaticString = #filePath, line: UInt = #line) async {
        await wait(for: .texts(count), "\(count) text message(s)", file: file, line: line)
    }

    func waitForSentFrames(count: Int, file: StaticString = #filePath, line: UInt = #line) async {
        await wait(for: .frames(count), "\(count) audio frame(s)", file: file, line: line)
    }

    func waitForCloseCalls(count: Int, file: StaticString = #filePath, line: UInt = #line) async {
        await wait(for: .closes(count), "\(count) close call(s)", file: file, line: line)
    }

    /// Returns when a `receive()` is waiting with nothing left in the queue.
    func waitForReceive(file: StaticString = #filePath, line: UInt = #line) async {
        await wait(for: .receiving, "a waiting receive", file: file, line: line)
    }

    // MARK: WebSocketTransport

    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection {
        openCount += 1
        openedURL = url
        openedHeaders = headers
        switch openResult {
        case .connection:
            ended = false
            sendsFail = false
            return ScriptedSpeechConnection(socket: self)
        case .rejected(let status):
            throw WebSocketUpgradeRejected(status: status)
        case .failure(let failure):
            throw failure
        }
    }

    // MARK: The connection's side

    fileprivate func connectionSend(text: String) throws {
        guard !sendsFail, !ended else { throw TransportFailure.other }
        sentTexts.append(text)
        wake()
    }

    fileprivate func connectionSend(data: Data) throws {
        guard !sendsFail, !ended else { throw TransportFailure.other }
        sentFrames.append(data)
        wake()
    }

    fileprivate func connectionReceive() async throws -> WebSocketMessage {
        if !inbound.isEmpty {
            return try take(inbound.removeFirst())
        }
        guard !ended else { throw TransportFailure.other }
        return try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<WebSocketMessage, any Error>) in
            receiver = continuation
            wake()
        }
    }

    fileprivate func connectionClose(code: Int) {
        closeCalls.append(code)
        ended = true
        if let waiting = receiver {
            receiver = nil
            waiting.resume(throwing: TransportFailure.other)
        }
        wake()
    }

    // MARK: Internals

    private func push(_ item: Result<WebSocketMessage, TransportFailure>) {
        guard let waiting = receiver else {
            inbound.append(item)
            return
        }
        receiver = nil
        do {
            waiting.resume(returning: try take(item))
        } catch {
            waiting.resume(throwing: error)
        }
    }

    /// Unwraps a queued item and remembers that a close frame ends the connection.
    private func take(_ item: Result<WebSocketMessage, TransportFailure>) throws -> WebSocketMessage {
        let message = try item.get()
        if case .closed = message {
            ended = true
        }
        return message
    }

    private func isMet(_ condition: Condition) -> Bool {
        switch condition {
        case .texts(let count): return sentTexts.count >= count
        case .frames(let count): return sentFrames.count >= count
        case .closes(let count): return closeCalls.count >= count
        case .receiving: return receiver != nil
        }
    }

    /// Returns when the condition holds. After `SpeechTest.patienceNanoseconds` it fails the test and
    /// returns.
    private func wait(for condition: Condition, _ what: String, file: StaticString, line: UInt) async {
        if isMet(condition) { return }
        nextWaiterID += 1
        let id = nextWaiterID
        let watchdog = Task {
            try? await Task.sleep(nanoseconds: SpeechTest.patienceNanoseconds)
            if !Task.isCancelled {
                await self.giveUp(id, what, file: file, line: line)
            }
        }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiters.append(Waiter(id: id, condition: condition, continuation: continuation))
        }
        watchdog.cancel()
    }

    private func giveUp(_ id: Int, _ what: String, file: StaticString, line: UInt) {
        guard let index = waiters.firstIndex(where: { $0.id == id }) else { return }
        let waiter = waiters.remove(at: index)
        XCTFail(
            "The socket never saw \(what). Texts: \(sentTexts.count), frames: \(sentFrames.count), closes: \(closeCalls), opens: \(openCount).",
            file: file,
            line: line
        )
        waiter.continuation.resume()
    }

    private func wake() {
        var stillWaiting: [Waiter] = []
        var ready: [Waiter] = []
        for waiter in waiters {
            if isMet(waiter.condition) {
                ready.append(waiter)
            } else {
                stillWaiting.append(waiter)
            }
        }
        waiters = stillWaiting
        for waiter in ready {
            waiter.continuation.resume()
        }
    }
}

/// The connection handed out by `ScriptedSpeechSocket.open`. Everything goes back to the socket.
private struct ScriptedSpeechConnection: WebSocketConnection {
    let socket: ScriptedSpeechSocket

    func send(text: String) async throws {
        try await socket.connectionSend(text: text)
    }

    func send(data: Data) async throws {
        try await socket.connectionSend(data: data)
    }

    func receive() async throws -> WebSocketMessage {
        try await socket.connectionReceive()
    }

    func close(code: Int, reason: String) async {
        await socket.connectionClose(code: code)
    }
}
