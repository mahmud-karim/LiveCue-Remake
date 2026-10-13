import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The Voz column of section 4.2 with a fake engine: recording at once, the batch on Assist and on Pause,
/// the two-second overlap, each failure with its text, and the audio session kept on Pause.
final class VozTranscriberTests: XCTestCase {
    // MARK: - Start

    @MainActor
    func testStartOpensTheMicrophoneAtOnceAndSaysItIsRecording() async throws {
        let rig = VozRig()
        try await rig.start()

        XCTAssertEqual(rig.audio.requests, [AudioCaptureRequest(target: .float32(sampleRate: 16_000), queueLimit: 128)])
        XCTAssertTrue(rig.audio.isRunning)
        // The one event of the start was emitted before it returned.
        XCTAssertEqual(rig.transcriber.emittedEventCount, 1)
        await rig.log.wait(for: 1)
        XCTAssertEqual(rig.log.lines, ["status Recording on this iPhone"])
        XCTAssertEqual(rig.log.events.first?.generation, rig.generation)
        // Model Library shows the same line.
        XCTAssertEqual(rig.models.statusLine, "Recording on this iPhone")
        XCTAssertEqual(rig.transcriber.provider, .voz)
        await rig.end()
    }

    @MainActor
    func testStartWithoutAPreparedModelFailsAndOpensNothing() async {
        let rig = VozRig(prepared: false)
        let failure = await rig.startFailure()

        XCTAssertEqual(failure, TranscriberFailure(.notPrepared, message: "Prepare Voz first."))
        XCTAssertTrue(rig.audio.requests.isEmpty)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")
        await rig.end()
    }

    @MainActor
    func testStartWithoutAUsableMicrophoneFails() async {
        for failure in [AudioCaptureFailure.noUsableMicrophone, AudioCaptureFailure.couldNotStart] {
            let rig = VozRig()
            rig.audio.startFailure = failure
            let thrown = await rig.startFailure()

            XCTAssertEqual(thrown, TranscriberFailure(.noMicrophone, message: "No usable microphone."))
            XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
            XCTAssertFalse(rig.audio.isRunning)
            await rig.end()
        }
    }

    // MARK: - The batch on Assist

    @MainActor
    func testAssistTranscribesWhatIsPendingAndEmitsOneSegment() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()
        await rig.engine.setResult(.success(VozRig.recognition([("Hello", 0.4), ("world", 0.9)], duration: 1)))

        let before = rig.transcriber.emittedEventCount
        let seconds = try await rig.transcriber.transcribePending()

        XCTAssertEqual(seconds, 0, "The clock did not move while the recogniser worked.")
        let calls = await rig.engine.calls
        XCTAssertEqual(calls, [16_000])
        // The three events of the batch were emitted before the call returned.
        XCTAssertEqual(rig.transcriber.emittedEventCount, before + 3)
        await rig.log.wait(for: before + 3)
        XCTAssertEqual(Array(rig.log.lines.dropFirst(before)), [
            "status Transcribing…",
            "segment Hello world 0.00-1.00",
            "status 1.0 s audio → 0.00 s transcription"
        ])
        XCTAssertEqual(rig.models.statusLine, "1.0 s audio → 0.00 s transcription")
        XCTAssertTrue(rig.audio.isRunning, "An Assist does not stop the recording.")
        XCTAssertTrue(rig.audio.stops.isEmpty)
        await rig.end()
    }

    @MainActor
    func testAssistReturnsTheSecondsTheBatchTook() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()
        let gate = FakeGate()
        await rig.engine.setGate(gate)
        await rig.engine.setResult(.success(VozRig.recognition([("Slow", 0.5)], duration: 1)))

        let assisting = Task { try await rig.transcriber.transcribePending() }
        await rig.log.waitForLine("status Transcribing…")
        await rig.clock.advance(by: 1.5)
        await gate.open()
        let seconds = try await assisting.value

        XCTAssertEqual(seconds, 1.5, accuracy: 0.0001)
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines.last, "status 1.0 s audio → 1.50 s transcription")
        await rig.end()
    }

    @MainActor
    func testWithNothingNewAssistReturnsZeroAndAsksNobody() async throws {
        let rig = VozRig()
        try await rig.start()

        let seconds = try await rig.transcriber.transcribePending()
        XCTAssertEqual(seconds, 0)
        let calls = await rig.engine.calls
        XCTAssertEqual(calls, [])
        XCTAssertEqual(rig.transcriber.emittedEventCount, 1, "Only the start's status.")
        await rig.end()
    }

    @MainActor
    func testOnlyWordsAfterThePreviousBatchAreKeptAndTwoSecondsStayAsContext() async throws {
        let rig = VozRig()
        try await rig.start()

        rig.audio.feed(.float32(VozRig.audio(seconds: 3)))
        await SpeechTest.settle()
        await rig.engine.setResult(.success(VozRig.recognition([("one", 1.0), ("two", 2.9)], duration: 3)))
        _ = try await rig.transcriber.transcribePending()

        // One more second arrives. The next batch hears the last two seconds again, then the new one.
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()
        await rig.engine.setResult(.success(VozRig.recognition([("two", 1.9), ("three", 2.6)], duration: 3)))
        _ = try await rig.transcriber.transcribePending()

        var calls = await rig.engine.calls
        XCTAssertEqual(calls, [48_000, 48_000])
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        let segments = rig.log.lines.filter { $0.hasPrefix("segment") }
        XCTAssertEqual(segments, ["segment one two 0.00-3.00", "segment three 0.00-3.00"], "No word is repeated.")

        // Nothing arrived since: there is nothing to transcribe.
        let emitted = rig.transcriber.emittedEventCount
        let seconds = try await rig.transcriber.transcribePending()
        XCTAssertEqual(seconds, 0)
        calls = await rig.engine.calls
        XCTAssertEqual(calls.count, 2)
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        await rig.end()
    }

    @MainActor
    func testABatchWithoutNewWordsEmitsNoSegment() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()
        // The default result of the fake: no words.
        _ = try await rig.transcriber.transcribePending()

        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines, [
            "status Recording on this iPhone",
            "status Transcribing…",
            "status 1.0 s audio → 0.00 s transcription"
        ])
        await rig.end()
    }

    @MainActor
    func testAnEngineErrorThrowsTheOnDeviceTextAndKeepsTheAudio() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()
        await rig.engine.setResult(.failure(FakeEngineFailure()))

        do {
            _ = try await rig.transcriber.transcribePending()
            XCTFail("The batch should have failed.")
        } catch {
            // Never the recogniser's own text (R-08).
            XCTAssertEqual(
                error as? TranscriberFailure,
                TranscriberFailure(.processingFailed, message: "Transcription stopped on this iPhone. Resume to continue.")
            )
        }
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines, [
            "status Recording on this iPhone",
            "status Transcribing…",
            "status Recording on this iPhone"
        ])
        XCTAssertTrue(rig.audio.isRunning, "The recording goes on; only this batch failed.")

        // The audio is still pending: the next batch hears it.
        await rig.engine.setResult(.success(VozRig.recognition([("Again", 0.5)], duration: 1)))
        _ = try await rig.transcriber.transcribePending()
        let calls = await rig.engine.calls
        XCTAssertEqual(calls, [16_000, 16_000])
        await rig.log.waitForLine("segment Again 0.00-1.00")
        await rig.end()
    }

    @MainActor
    func testAResultForAStartThatHasEndedIsDiscarded() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()
        let gate = FakeGate()
        await rig.engine.setGate(gate)
        await rig.engine.setResult(.success(VozRig.recognition([("Stale", 0.5)], duration: 1)))

        let assisting = Task { try await rig.transcriber.transcribePending() }
        await rig.log.waitForLine("status Transcribing…")
        // The stream is started again while the recogniser still works on the old audio.
        try await rig.transcriber.start(TranscriberStart(generation: UUID(), streamOffsetSeconds: 0))
        await gate.open()
        _ = try await assisting.value

        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("segment") }, "The old audio is gone; so are its words.")
        await rig.end()
    }

    // MARK: - Pause and Stop

    @MainActor
    func testPauseTranscribesWhatIsQueuedAndKeepsTheAudioSession() async throws {
        let rig = VozRig()
        try await rig.start()
        // Still in the hand-off queue when Pause is tapped.
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await rig.engine.setResult(.success(VozRig.recognition([("Final", 0.5), ("words", 0.9)], duration: 1)))

        try await rig.transcriber.finalize()

        XCTAssertEqual(rig.audio.stops, [false], "On-device providers keep the audio session active on Pause (IOS-B6).")
        XCTAssertFalse(rig.audio.isRunning)
        let calls = await rig.engine.calls
        XCTAssertEqual(calls, [16_000])
        // Everything was emitted before the call returned, the text before "Paused".
        XCTAssertEqual(rig.transcriber.emittedEventCount, 5)
        await rig.log.wait(for: 5)
        XCTAssertEqual(rig.log.lines, [
            "status Recording on this iPhone",
            "status Transcribing…",
            "segment Final words 0.00-1.00",
            "status 1.0 s audio → 0.00 s transcription",
            "status Paused"
        ])
        XCTAssertEqual(rig.models.statusLine, "Paused")

        // While paused there is nothing pending, and nothing is finalised twice.
        let seconds = try await rig.transcriber.transcribePending()
        XCTAssertEqual(seconds, 0)
        try await rig.transcriber.finalize()
        XCTAssertEqual(rig.transcriber.emittedEventCount, 5)
        XCTAssertEqual(rig.audio.stops, [false])
        await rig.end()
    }

    @MainActor
    func testPauseWithAnEngineErrorThrowsAndIsPausedAllTheSame() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await rig.engine.setResult(.failure(FakeEngineFailure()))

        do {
            try await rig.transcriber.finalize()
            XCTFail("The finalisation should have failed.")
        } catch {
            XCTAssertEqual((error as? TranscriberFailure)?.reason, .processingFailed)
        }
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines.last, "status Paused")
        XCTAssertEqual(rig.audio.stops, [false])
        XCTAssertFalse(rig.audio.isRunning, "Capture is down either way.")
        await rig.end()
    }

    @MainActor
    func testPauseWaitsForARunningBatch() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()
        let gate = FakeGate()
        await rig.engine.setGate(gate)
        await rig.engine.setResult(.success(VozRig.recognition([("First", 0.5)], duration: 1)))

        let assisting = Task { try await rig.transcriber.transcribePending() }
        await rig.log.waitForLine("status Transcribing…")
        let pausing = Task { try await rig.transcriber.finalize() }
        await SpeechTest.settle()
        XCTAssertTrue(rig.audio.stops.isEmpty, "The finalisation waits its turn; the recording goes on.")

        await gate.open()
        _ = try await assisting.value
        try await pausing.value

        XCTAssertEqual(rig.audio.stops, [false])
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines, [
            "status Recording on this iPhone",
            "status Transcribing…",
            "segment First 0.00-1.00",
            "status 1.0 s audio → 0.00 s transcription",
            "status Paused"
        ])
        let calls = await rig.engine.calls
        XCTAssertEqual(calls, [16_000], "The batch of the Pause found nothing new.")
        await rig.end()
    }

    @MainActor
    func testResumeStartsWithAnEmptyBuffer() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()

        // A new start without a finalisation in between: what was pending is dropped.
        let second = UUID()
        try await rig.transcriber.start(TranscriberStart(generation: second, streamOffsetSeconds: 40))
        let seconds = try await rig.transcriber.transcribePending()
        XCTAssertEqual(seconds, 0)
        let calls = await rig.engine.calls
        XCTAssertEqual(calls, [])

        XCTAssertEqual(rig.audio.requests.count, 2)
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.events.last?.generation, second)
        await rig.end()
    }

    // MARK: - Failures

    @MainActor
    func testCaptureFailuresStopTheRecordingKeepTheSessionAndFailOnce() async throws {
        let rows: [(event: AudioCaptureEvent, reason: TranscriberFailure.Reason, message: String)] = [
            (.overflow, .processingOverflow, "Transcription could not keep up. Recording paused; resume to continue."),
            (.interrupted, .microphoneInterrupted, "Microphone interrupted. Resume when your call or other audio finishes."),
            (.routeLost, .routeChanged, "Microphone route changed. Resume to use the current microphone.")
        ]
        for row in rows {
            let rig = VozRig()
            try await rig.start()
            rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
            await SpeechTest.settle()

            rig.audio.send(row.event)
            let failed = "failed \(row.reason) \(row.message)"
            await rig.log.waitForLine(failed)
            XCTAssertEqual(rig.log.lines, ["status Recording on this iPhone", failed], "\(row.event)")
            XCTAssertEqual(rig.audio.stops, [false], "The capture stops; the audio session stays.")
            XCTAssertFalse(rig.audio.isRunning)

            // The session controller finalises next (T-10), so what was recorded is transcribed.
            await rig.engine.setResult(.success(VozRig.recognition([("Kept", 0.5)], duration: 1)))
            try await rig.transcriber.finalize()
            XCTAssertEqual(rig.audio.stops, [false], "Nothing is stopped twice.")
            await rig.log.wait(for: rig.transcriber.emittedEventCount)
            XCTAssertEqual(Array(rig.log.lines.suffix(4)), [
                "status Transcribing…",
                "segment Kept 0.00-1.00",
                "status 1.0 s audio → 0.00 s transcription",
                "status Paused"
            ])
            XCTAssertEqual(rig.log.lines.filter { $0.hasPrefix("failed") }.count, 1)
            XCTAssertTrue(rig.log.events.allSatisfy { $0.generation == rig.generation })
            await rig.end()
        }
    }

    @MainActor
    func testThirtyMinutesOfPendingAudioPauseWithTheCapTextAndAreTranscribed() async throws {
        let rig = VozRig()
        try await rig.start()

        // Nine blocks of 3,200,000 samples are exactly thirty minutes. One sample more does not fit.
        let block = [Float](repeating: 0, count: 3_200_000)
        for _ in 0..<9 {
            rig.audio.feed(.float32(block))
        }
        rig.audio.feed(.float32([0]))
        let failed = "failed pendingAudioLimit Pending recording reached 30 minutes and was transcribed. Resume to keep recording."
        await rig.log.waitForLine(failed)
        XCTAssertEqual(rig.audio.stops, [false])
        XCTAssertEqual(rig.log.lines, ["status Recording on this iPhone", failed])

        // The pause that follows transcribes the thirty minutes, as the text says.
        await rig.engine.setResult(.success(VozRig.recognition([("Long", 1_799)], duration: 1_800)))
        try await rig.transcriber.finalize()
        let calls = await rig.engine.calls
        XCTAssertEqual(calls, [PendingAudioBuffer.capacitySamples])
        XCTAssertEqual(calls, [28_800_000])
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(Array(rig.log.lines.suffix(3)), [
            "segment Long 0.00-1800.00",
            "status 1800.0 s audio → 0.00 s transcription",
            "status Paused"
        ])
        await rig.end()
    }

    // MARK: - Levels and shutdown

    @MainActor
    func testLevelsOfTheCaptureArePassedOn() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.send(.level(0.5))
        await rig.log.waitForLine("level 0.5")
        await rig.end()
    }

    @MainActor
    func testShutdownReleasesTheAudioSessionEmitsNothingAndSaysReady() async throws {
        let rig = VozRig()
        try await rig.start()
        rig.audio.feed(.float32(VozRig.audio(seconds: 1)))
        await SpeechTest.settle()
        let emitted = rig.transcriber.emittedEventCount

        await rig.transcriber.shutdown()
        XCTAssertEqual(rig.audio.stops, [true], "A full stop releases the audio session.")
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        XCTAssertEqual(rig.models.statusLine, "Ready")

        // The buffer is gone with the session.
        let seconds = try await rig.transcriber.transcribePending()
        XCTAssertEqual(seconds, 0)
        let calls = await rig.engine.calls
        XCTAssertEqual(calls, [])

        // Idempotent.
        await rig.transcriber.shutdown()
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        await rig.end()
    }
}

/// A `VozTranscriber` on fakes, with the model already loaded unless a test says otherwise.
@MainActor private final class VozRig {
    let clock: ManualClock
    let audio: FakeAudioCapture
    let engine: FakeBatchEngine
    let models: OnDeviceModelManager
    let transcriber: VozTranscriber
    let log: TranscriberEventLog
    let generation = UUID()

    init(prepared: Bool = true) {
        let clock = ManualClock()
        let audio = FakeAudioCapture()
        let engine = FakeBatchEngine()
        let models = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: engine, parakeet: FakeStreamingEngine()),
            activity: SessionActivity(),
            clock: clock
        )
        if prepared {
            models.installPreloaded(variant: TranscriptionModel.voz.variant, voz: engine, parakeet: nil)
        }
        let transcriber = VozTranscriber(engine: prepared ? engine : nil, audio: audio, clock: clock, models: models)

        self.clock = clock
        self.audio = audio
        self.engine = engine
        self.models = models
        self.transcriber = transcriber
        self.log = TranscriberEventLog(transcriber)
    }

    func start() async throws {
        try await transcriber.start(TranscriberStart(generation: generation, streamOffsetSeconds: 0))
    }

    func startFailure() async -> TranscriberFailure? {
        do {
            try await start()
            return nil
        } catch {
            return error as? TranscriberFailure
        }
    }

    func end() async {
        await transcriber.shutdown()
        log.stop()
    }

    /// Silence of the given length at 16 kHz.
    static func audio(seconds: Int) -> [Float] {
        [Float](repeating: 0, count: seconds * 16_000)
    }

    /// Words by text and end time; each starts a tenth of a second before it ends.
    static func recognition(_ words: [(String, Double)], duration: Double) -> BatchRecognition {
        BatchRecognition(
            words: words.map { RecognizedWord(text: $0.0, start: max(0, $0.1 - 0.1), end: $0.1) },
            duration: duration
        )
    }
}
