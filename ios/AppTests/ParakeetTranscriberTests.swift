import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The Parakeet column of section 4.2 with a fake engine: the reset at start, every buffer processed and
/// timed, the partial text, one segment on Pause, each failure with its text, and the audio session kept
/// on Pause.
final class ParakeetTranscriberTests: XCTestCase {
    // MARK: - Start

    @MainActor
    func testStartResetsTheRecogniserAndOpensTheMicrophoneAtOnce() async throws {
        let rig = ParakeetRig()
        try await rig.start()

        let resets = await rig.engine.resetCount
        XCTAssertEqual(resets, 1)
        XCTAssertEqual(rig.audio.requests, [AudioCaptureRequest(target: .float32(sampleRate: 16_000), queueLimit: 128)])
        XCTAssertTrue(rig.audio.isRunning)
        // The one event of the start was emitted before it returned.
        XCTAssertEqual(rig.transcriber.emittedEventCount, 1)
        await rig.log.wait(for: 1)
        XCTAssertEqual(rig.log.lines, ["status Listening"])
        XCTAssertEqual(rig.log.events.first?.generation, rig.generation)
        XCTAssertEqual(rig.models.statusLine, "Listening")
        XCTAssertEqual(rig.transcriber.provider, .parakeet)
        await rig.end()
    }

    @MainActor
    func testStartWithoutAPreparedModelFailsAndOpensNothing() async {
        let rig = ParakeetRig(prepared: false)
        let failure = await rig.startFailure()

        XCTAssertEqual(failure, TranscriberFailure(.notPrepared, message: "Prepare Parakeet first."))
        XCTAssertTrue(rig.audio.requests.isEmpty)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
        await rig.end()
    }

    @MainActor
    func testStartWithoutAUsableMicrophoneFailsAndNothingRuns() async {
        let rig = ParakeetRig()
        rig.audio.startFailure = .noUsableMicrophone
        let failure = await rig.startFailure()

        XCTAssertEqual(failure, TranscriberFailure(.noMicrophone, message: "No usable microphone."))
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
        XCTAssertFalse(rig.audio.isRunning)

        // Nothing is left of the failed start: a finalisation has nothing to do.
        try? await rig.transcriber.finalize()
        let finishes = await rig.engine.finishCount
        XCTAssertEqual(finishes, 0)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
        await rig.end()
    }

    // MARK: - While listening

    @MainActor
    func testEveryBufferGoesToTheRecogniserAndItsTimeIsReported() async throws {
        let rig = ParakeetRig()
        try await rig.start()

        rig.audio.feed(.float32(ParakeetRig.audio(samples: 1_600)))
        await rig.log.wait(for: 3)
        rig.audio.feed(.float32(ParakeetRig.audio(samples: 800)))
        await rig.log.wait(for: 5)

        XCTAssertEqual(rig.log.lines, [
            "status Listening",
            "lastChunk 0.0",
            "status Last buffer processing: 0 ms",
            "lastChunk 0.0",
            "status Last buffer processing: 0 ms"
        ])
        let processed = await rig.engine.processed
        let samples = await rig.engine.processedSamples
        XCTAssertEqual(processed, 2)
        XCTAssertEqual(samples, 2_400)
        XCTAssertEqual(rig.models.statusLine, "Last buffer processing: 0 ms")
        await rig.end()
    }

    @MainActor
    func testThePartialIsWhatTheCallbackLastReported() async throws {
        let rig = ParakeetRig()
        try await rig.start()

        await rig.engine.emitPartial("what is")
        await rig.log.waitForLine("partial what is")
        await rig.engine.emitPartial("what is the main")
        await rig.log.waitForLine("partial what is the main")

        XCTAssertEqual(rig.log.lines, ["status Listening", "partial what is", "partial what is the main"])
        XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("segment") }, "Nothing is committed while listening.")
        await rig.end()
    }

    @MainActor
    func testLevelsOfTheCaptureArePassedOn() async throws {
        let rig = ParakeetRig()
        try await rig.start()
        rig.audio.send(.level(0.75))
        await rig.log.waitForLine("level 0.75")
        await rig.end()
    }

    // MARK: - Pause and Stop

    @MainActor
    func testPauseCommitsOneSegmentAndKeepsTheAudioSession() async throws {
        let rig = ParakeetRig()
        try await rig.start()
        await rig.engine.setFinal("  What is the main advantage?  ")
        // Still in the hand-off queue when Pause is tapped: it is processed before the final text is asked for.
        rig.audio.feed(.float32(ParakeetRig.audio(samples: 1_600)))

        try await rig.transcriber.finalize()

        XCTAssertEqual(rig.audio.stops, [false], "On-device providers keep the audio session active on Pause (IOS-B6).")
        XCTAssertFalse(rig.audio.isRunning)
        let processed = await rig.engine.processed
        let finishes = await rig.engine.finishCount
        XCTAssertEqual(processed, 1)
        XCTAssertEqual(finishes, 1)
        // Everything was emitted before the call returned: the segment, the cleared partial, "Paused".
        XCTAssertEqual(rig.transcriber.emittedEventCount, 4)
        await rig.log.wait(for: 4)
        XCTAssertEqual(rig.log.lines, [
            "status Listening",
            "segment What is the main advantage? 0.00-0.00",
            "partial ",
            "status Paused"
        ])
        XCTAssertEqual(rig.models.statusLine, "Paused")

        // Paused: nothing is finalised twice (IOS-B7), and a late callback shows nothing.
        try await rig.transcriber.finalize()
        await rig.engine.emitPartial("late")
        await SpeechTest.settle()
        let finishesAfter = await rig.engine.finishCount
        XCTAssertEqual(finishesAfter, 1)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 4)
        XCTAssertEqual(rig.audio.stops, [false])
        await rig.end()
    }

    @MainActor
    func testPauseWithoutRecognisedTextCommitsNothing() async throws {
        let rig = ParakeetRig()
        try await rig.start()
        await rig.engine.setFinal("   ")

        try await rig.transcriber.finalize()
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines, ["status Listening", "partial ", "status Paused"])
        await rig.end()
    }

    @MainActor
    func testAFinalTextThatCannotBeReadMakesThePauseThrow() async throws {
        let rig = ParakeetRig()
        try await rig.start()
        await rig.engine.setFinishError(true)

        do {
            try await rig.transcriber.finalize()
            XCTFail("The finalisation should have failed.")
        } catch {
            XCTAssertEqual(
                error as? TranscriberFailure,
                TranscriberFailure(.processingFailed, message: "Transcription stopped on this iPhone. Resume to continue.")
            )
        }
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines, ["status Listening", "partial ", "status Paused"])
        XCTAssertEqual(rig.audio.stops, [false])
        XCTAssertFalse(rig.audio.isRunning, "Capture is down either way.")
        await rig.end()
    }

    @MainActor
    func testResumeResetsTheRecogniserAgain() async throws {
        let rig = ParakeetRig()
        try await rig.start()
        try await rig.transcriber.finalize()

        let second = UUID()
        try await rig.transcriber.start(TranscriberStart(generation: second, streamOffsetSeconds: 25))
        let resets = await rig.engine.resetCount
        XCTAssertEqual(resets, 2)
        XCTAssertEqual(rig.audio.requests.count, 2)

        // The callback of the new start is the one that shows.
        await rig.engine.emitPartial("again")
        await rig.log.waitForLine("partial again")
        XCTAssertEqual(rig.log.events.last?.generation, second)
        await rig.end()
    }

    // MARK: - Failures

    @MainActor
    func testAProcessingErrorPausesWithTheOnDeviceTextAndTheTextSoFarIsKept() async throws {
        let rig = ParakeetRig()
        try await rig.start()
        await rig.engine.setProcessError(true)

        rig.audio.feed(.float32(ParakeetRig.audio(samples: 1_600)))
        // Never the recogniser's own text (R-08).
        let failed = "failed processingFailed Transcription stopped on this iPhone. Resume to continue."
        await rig.log.waitForLine(failed)
        XCTAssertEqual(rig.log.lines, ["status Listening", failed])
        XCTAssertEqual(rig.audio.stops, [false], "The capture stops; the audio session stays.")
        XCTAssertFalse(rig.audio.isRunning)

        // The session controller finalises next (T-10), so what was recognised is kept.
        await rig.engine.setFinal("Kept words")
        try await rig.transcriber.finalize()
        XCTAssertEqual(rig.audio.stops, [false], "Nothing is stopped twice.")
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines, [
            "status Listening",
            failed,
            "segment Kept words 0.00-0.00",
            "partial ",
            "status Paused"
        ])
        XCTAssertTrue(rig.log.events.allSatisfy { $0.generation == rig.generation })
        await rig.end()
    }

    @MainActor
    func testCaptureFailuresStopTheRecordingKeepTheSessionAndFailOnce() async throws {
        let rows: [(event: AudioCaptureEvent, reason: TranscriberFailure.Reason, message: String)] = [
            (.overflow, .processingOverflow, "Transcription could not keep up. Recording paused; resume to continue."),
            (.interrupted, .microphoneInterrupted, "Microphone interrupted. Resume when your call or other audio finishes."),
            (.routeLost, .routeChanged, "Microphone route changed. Resume to use the current microphone.")
        ]
        for row in rows {
            let rig = ParakeetRig()
            try await rig.start()

            rig.audio.send(row.event)
            let failed = "failed \(row.reason) \(row.message)"
            await rig.log.waitForLine(failed)
            XCTAssertEqual(rig.log.lines, ["status Listening", failed], "\(row.event)")
            XCTAssertEqual(rig.audio.stops, [false])
            XCTAssertFalse(rig.audio.isRunning)

            await rig.engine.setFinal("Recognised before the failure")
            try await rig.transcriber.finalize()
            XCTAssertEqual(rig.audio.stops, [false])
            await rig.log.wait(for: rig.transcriber.emittedEventCount)
            XCTAssertEqual(Array(rig.log.lines.suffix(3)), [
                "segment Recognised before the failure 0.00-0.00",
                "partial ",
                "status Paused"
            ])
            XCTAssertEqual(rig.log.lines.filter { $0.hasPrefix("failed") }.count, 1)
            await rig.end()
        }
    }

    @MainActor
    func testBuffersQueuedAtAFailureAreStillProcessedBeforeTheFinalText() async throws {
        let rig = ParakeetRig()
        try await rig.start()

        // Two buffers are waiting when the microphone is interrupted.
        rig.audio.feed(.float32(ParakeetRig.audio(samples: 1_600)))
        rig.audio.feed(.float32(ParakeetRig.audio(samples: 1_600)))
        rig.audio.send(.interrupted)
        await rig.log.wait { rig.log.lines.contains { $0.hasPrefix("failed microphoneInterrupted") } }

        try await rig.transcriber.finalize()
        let processed = await rig.engine.processed
        XCTAssertEqual(processed, 2, "The audio that was captured is recognised.")
        await rig.log.wait(for: rig.transcriber.emittedEventCount)
        XCTAssertEqual(rig.log.lines.last, "status Paused")
        XCTAssertEqual(rig.log.lines.filter { $0.hasPrefix("failed") }.count, 1)
        await rig.end()
    }

    // MARK: - The rest of the interface

    @MainActor
    func testNothingIsPendingAndShutdownReleasesTheAudioSession() async throws {
        let rig = ParakeetRig()
        try await rig.start()
        let emitted = rig.transcriber.emittedEventCount

        let seconds = try await rig.transcriber.transcribePending()
        XCTAssertEqual(seconds, 0)

        await rig.transcriber.shutdown()
        XCTAssertEqual(rig.audio.stops, [true], "A full stop releases the audio session.")
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        XCTAssertEqual(rig.models.statusLine, "Ready")
        // The recogniser belongs to the model manager: the transcriber does not release it.
        let cleanups = await rig.engine.cleanupCount
        let finishes = await rig.engine.finishCount
        XCTAssertEqual(cleanups, 0)
        XCTAssertEqual(finishes, 0)

        // Idempotent, and a callback after the end shows nothing.
        await rig.transcriber.shutdown()
        await rig.engine.emitPartial("late")
        await SpeechTest.settle()
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        await rig.end()
    }
}

/// A `ParakeetTranscriber` on fakes, with the model already loaded unless a test says otherwise.
@MainActor private final class ParakeetRig {
    let clock: ManualClock
    let audio: FakeAudioCapture
    let engine: FakeStreamingEngine
    let models: OnDeviceModelManager
    let transcriber: ParakeetTranscriber
    let log: TranscriberEventLog
    let generation = UUID()

    init(prepared: Bool = true) {
        let clock = ManualClock()
        let audio = FakeAudioCapture()
        let engine = FakeStreamingEngine()
        let models = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: FakeBatchEngine(), parakeet: engine),
            activity: SessionActivity(),
            clock: clock
        )
        if prepared {
            models.installPreloaded(variant: TranscriptionModel.parakeet.variant, voz: nil, parakeet: engine)
        }
        let transcriber = ParakeetTranscriber(engine: prepared ? engine : nil, audio: audio, clock: clock, models: models)

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

    /// Silence at 16 kHz.
    static func audio(samples: Int) -> [Float] {
        [Float](repeating: 0, count: samples)
    }
}
