import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Provider `apple` (D-28) with a fake system recogniser: tentative results as the partial, final results
/// committed as segments while listening, Pause, Resume and Stop, and each failure with its text. No test
/// here reaches the real recogniser, which the simulator may not have.
final class AppleTranscriberTests: XCTestCase {
    private static func tentativeResult(_ text: String) -> SystemSpeechResult {
        SystemSpeechResult(text: text, isFinal: false, startSeconds: 0, endSeconds: 0)
    }

    private static func finalResult(_ text: String, from start: Double, to end: Double) -> SystemSpeechResult {
        SystemSpeechResult(text: text, isFinal: true, startSeconds: start, endSeconds: end)
    }

    // MARK: - Start

    @MainActor
    func testStartBeginsARecognitionAndOpensTheMicrophone() async throws {
        let rig = AppleRig()
        try await rig.start()

        let begins = await rig.engine.beginCount
        XCTAssertEqual(begins, 1)
        XCTAssertEqual(rig.audio.requests, [AudioCaptureRequest(target: .float32(sampleRate: 16_000), queueLimit: 128)])
        XCTAssertTrue(rig.audio.isRunning)
        // The one event of the start was emitted before it returned.
        XCTAssertEqual(rig.transcriber.emittedEventCount, 1)
        await rig.log.wait(for: 1)
        XCTAssertEqual(rig.log.lines, ["status Listening"])
        XCTAssertEqual(rig.log.events.first?.generation, rig.generation)
        XCTAssertEqual(rig.models.statusLine, "Listening")
        XCTAssertEqual(rig.transcriber.provider, .apple)
        await rig.end()
    }

    @MainActor
    func testStartWithoutTheInstalledAssetFailsAndOpensNothing() async {
        let rig = AppleRig(installed: false)
        let failure = await rig.startFailure()

        XCTAssertEqual(failure, TranscriberFailure(.notPrepared, message: "Prepare Apple speech first."))
        XCTAssertTrue(rig.audio.requests.isEmpty)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
        let begins = await rig.engine.beginCount
        XCTAssertEqual(begins, 0)
        await rig.end()
    }

    @MainActor
    func testARecogniserThatCannotBeginFailsTheStartWithItsOwnText() async {
        let rig = AppleRig()
        await rig.engine.setBeginError(true)
        let failure = await rig.startFailure()

        XCTAssertEqual(failure, TranscriberFailure(.processingFailed, message: "Apple speech could not start on this iPhone."))
        XCTAssertTrue(rig.audio.requests.isEmpty, "The microphone opens only once the recogniser runs.")
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)

        // Nothing is left of the failed start: a finalisation has nothing to do.
        try? await rig.transcriber.finalize()
        let finishes = await rig.engine.finishCount
        XCTAssertEqual(finishes, 0)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
        await rig.end()
    }

    @MainActor
    func testStartWithoutAUsableMicrophoneFailsAndEndsTheRecognition() async {
        let rig = AppleRig()
        rig.audio.startFailure = .noUsableMicrophone
        let failure = await rig.startFailure()

        XCTAssertEqual(failure, TranscriberFailure(.noMicrophone, message: "No usable microphone."))
        XCTAssertEqual(rig.transcriber.emittedEventCount, 0)
        XCTAssertFalse(rig.audio.isRunning)
        let cancels = await rig.engine.cancelCount
        XCTAssertEqual(cancels, 1)
        await rig.end()
    }

    // MARK: - While listening

    @MainActor
    func testTentativeResultsAreThePartialAndFinalResultsBecomeSegmentsAtOnce() async throws {
        let rig = AppleRig()
        try await rig.start()

        await rig.engine.emit(AppleTranscriberTests.tentativeResult("what is"))
        await rig.log.waitForLine("partial what is")
        await rig.engine.emit(AppleTranscriberTests.tentativeResult(" what is the main "))
        await rig.log.waitForLine("partial what is the main")
        // Final while still listening: committed now, not on Pause.
        await rig.engine.emit(AppleTranscriberTests.finalResult("  What is the main advantage?  ", from: 0.5, to: 2.25))
        await rig.log.wait(for: 5)
        await rig.engine.emit(AppleTranscriberTests.tentativeResult("and"))
        await rig.log.waitForLine("partial and")
        await rig.engine.emit(AppleTranscriberTests.finalResult("And why?", from: 2.25, to: 3))
        await rig.log.wait(for: 8)

        XCTAssertEqual(rig.log.lines, [
            "status Listening",
            "partial what is",
            "partial what is the main",
            "segment What is the main advantage? 0.50-2.25",
            "partial ",
            "partial and",
            "segment And why? 2.25-3.00",
            "partial "
        ])
        XCTAssertTrue(rig.audio.isRunning, "Committing a segment does not pause anything.")
        XCTAssertEqual(rig.log.events.map(\.generation), Array(repeating: rig.generation, count: 8))
        await rig.end()
    }

    @MainActor
    func testAnEmptyFinalResultCommitsNothing() async throws {
        let rig = AppleRig()
        try await rig.start()

        await rig.engine.emit(AppleTranscriberTests.finalResult("   ", from: 0, to: 1))
        await rig.log.wait(for: 2)

        XCTAssertEqual(rig.log.lines, ["status Listening", "partial "])
        await rig.end()
    }

    @MainActor
    func testSegmentTimesIncludeTheStreamOffset() async throws {
        let rig = AppleRig()
        try await rig.start(offset: 30)

        await rig.engine.emit(AppleTranscriberTests.finalResult("After the pause.", from: 0.5, to: 2.25))
        await rig.log.waitForLine("segment After the pause. 30.50-32.25")
        // A result whose end lies before its start keeps a length of zero.
        await rig.engine.emit(AppleTranscriberTests.finalResult("Odd times.", from: 4, to: 1))
        await rig.log.waitForLine("segment Odd times. 34.00-34.00")
        await rig.end()
    }

    @MainActor
    func testEveryBufferGoesToTheRecogniser() async throws {
        let rig = AppleRig()
        try await rig.start()

        rig.audio.feed(.float32(AppleRig.audio(samples: 1_600)))
        rig.audio.feed(.float32(AppleRig.audio(samples: 800)))
        await SpeechTest.eventually { await rig.engine.processed == 2 }

        let samples = await rig.engine.processedSamples
        XCTAssertEqual(samples, 2_400)
        await rig.log.wait(for: 1)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 1, "Handing a buffer over says nothing.")
        XCTAssertEqual(rig.log.lines, ["status Listening"])
        await rig.end()
    }

    @MainActor
    func testLevelsOfTheCaptureArePassedOn() async throws {
        let rig = AppleRig()
        try await rig.start()
        rig.audio.send(.level(0.75))
        await rig.log.waitForLine("level 0.75")
        await rig.end()
    }

    // MARK: - Pause, Resume and Stop

    @MainActor
    func testPauseCommitsWhatTheRecogniserStillHeldAndKeepsTheAudioSession() async throws {
        let rig = AppleRig()
        try await rig.start()
        await rig.engine.emit(AppleTranscriberTests.tentativeResult("and the last"))
        await rig.log.waitForLine("partial and the last")
        await rig.engine.setFinishResults([AppleTranscriberTests.finalResult("And the last words.", from: 3, to: 4.5)])
        // Still in the hand-off queue when Pause is tapped: it is handed over before the recogniser finishes.
        rig.audio.feed(.float32(AppleRig.audio(samples: 1_600)))

        try await rig.transcriber.finalize()

        XCTAssertEqual(rig.audio.stops, [false], "On-device providers keep the audio session active on Pause (IOS-B6).")
        XCTAssertFalse(rig.audio.isRunning)
        let processed = await rig.engine.processed
        let finishes = await rig.engine.finishCount
        XCTAssertEqual(processed, 1)
        XCTAssertEqual(finishes, 1)
        // Everything was emitted before the call returned: the last segment, the cleared partial, "Paused".
        XCTAssertEqual(rig.transcriber.emittedEventCount, 5)
        await rig.log.wait(for: 5)
        XCTAssertEqual(rig.log.lines, [
            "status Listening",
            "partial and the last",
            "segment And the last words. 3.00-4.50",
            "partial ",
            "status Paused"
        ])
        XCTAssertEqual(rig.models.statusLine, "Paused")
        XCTAssertEqual(rig.clock.sleeperCount, 0, "The wait for the recogniser ended with the finalisation.")

        // Paused: nothing is finalised twice (IOS-B7), and a late result shows nothing.
        try await rig.transcriber.finalize()
        await rig.engine.emit(AppleTranscriberTests.tentativeResult("late"))
        await SpeechTest.settle()
        let finishesAfter = await rig.engine.finishCount
        XCTAssertEqual(finishesAfter, 1)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 5)
        XCTAssertEqual(rig.audio.stops, [false])
        await rig.end()
    }

    @MainActor
    func testResumeBeginsANewRecognitionWithItsOwnGeneration() async throws {
        let rig = AppleRig()
        try await rig.start()
        try await rig.transcriber.finalize()

        let second = UUID()
        try await rig.transcriber.start(TranscriberStart(generation: second, streamOffsetSeconds: 12))
        let begins = await rig.engine.beginCount
        XCTAssertEqual(begins, 2)
        XCTAssertEqual(rig.audio.requests.count, 2)

        await rig.engine.emit(AppleTranscriberTests.finalResult("Second stretch.", from: 1, to: 2))
        await rig.log.waitForLine("segment Second stretch. 13.00-14.00")
        XCTAssertEqual(rig.log.events.last?.generation, second)
        XCTAssertEqual(rig.models.statusLine, "Listening")
        await rig.end()
    }

    @MainActor
    func testStopFinalisesAndShutdownReleasesTheAudioSession() async throws {
        let rig = AppleRig()
        try await rig.start()
        await rig.engine.setFinishResults([AppleTranscriberTests.finalResult("Goodbye.", from: 0, to: 1)])

        // Stop: the controller finalises, then shuts the transcriber down.
        try await rig.transcriber.finalize()
        await rig.log.waitForLine("segment Goodbye. 0.00-1.00")
        let emitted = rig.transcriber.emittedEventCount
        await rig.transcriber.shutdown()

        XCTAssertEqual(rig.audio.stops, [false, true], "Stop releases the audio session.")
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted, "A shutdown emits nothing.")
        XCTAssertEqual(rig.models.statusLine, "Ready")

        // Idempotent, and a result after the end shows nothing.
        await rig.transcriber.shutdown()
        await rig.engine.emit(AppleTranscriberTests.tentativeResult("late"))
        await SpeechTest.settle()
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        await rig.end()
    }

    @MainActor
    func testShutdownWhileListeningEndsTheRecognition() async throws {
        let rig = AppleRig()
        try await rig.start()

        await rig.transcriber.shutdown()

        XCTAssertEqual(rig.audio.stops, [true])
        let cancels = await rig.engine.cancelCount
        let finishes = await rig.engine.finishCount
        XCTAssertEqual(cancels, 1)
        XCTAssertEqual(finishes, 0)
        XCTAssertEqual(rig.transcriber.emittedEventCount, 1)
        await rig.end()
    }

    // MARK: - Failures

    @MainActor
    func testARecogniserThatStopsPausesTheSessionOnce() async throws {
        let rig = AppleRig()
        try await rig.start()
        await rig.engine.emit(AppleTranscriberTests.finalResult("Kept.", from: 0, to: 1))
        await rig.log.wait(for: 3)

        await rig.engine.failResults()
        await rig.log.waitForLine("failed processingFailed Transcription stopped on this iPhone. Resume to continue.")
        XCTAssertEqual(rig.audio.stops, [false], "The capture stops; the audio session stays.")
        XCTAssertFalse(rig.audio.isRunning)

        // The session controller finalises after an on-device failure (T-10). Nothing more is asked of
        // the recogniser, and the finalisation reports no second failure.
        try await rig.transcriber.finalize()
        let finishes = await rig.engine.finishCount
        XCTAssertEqual(finishes, 0)
        XCTAssertEqual(rig.audio.stops, [false], "The capture is not stopped twice.")
        await rig.log.waitForLine("status Paused")
        XCTAssertEqual(rig.log.lines, [
            "status Listening",
            "segment Kept. 0.00-1.00",
            "partial ",
            "failed processingFailed Transcription stopped on this iPhone. Resume to continue.",
            "partial ",
            "status Paused"
        ])
        await rig.end()
    }

    @MainActor
    func testABufferTheRecogniserRefusesPausesTheSession() async throws {
        let rig = AppleRig()
        try await rig.start()
        await rig.engine.setProcessError(true)

        rig.audio.feed(.float32(AppleRig.audio(samples: 1_600)))
        await rig.log.waitForLine("failed processingFailed Transcription stopped on this iPhone. Resume to continue.")
        XCTAssertEqual(rig.audio.stops, [false])

        try await rig.transcriber.finalize()
        let finishes = await rig.engine.finishCount
        XCTAssertEqual(finishes, 0)
        await rig.end()
    }

    @MainActor
    func testCaptureFailuresPauseWithTheirTextsAndKeepWhatWasSaid() async throws {
        let cases: [(event: AudioCaptureEvent, line: String)] = [
            (.interrupted, "failed microphoneInterrupted Microphone interrupted. Resume when your call or other audio finishes."),
            (.routeLost, "failed routeChanged Microphone route changed. Resume to use the current microphone."),
            (.overflow, "failed processingOverflow Transcription could not keep up. Recording paused; resume to continue.")
        ]
        for item in cases {
            let rig = AppleRig()
            try await rig.start()
            await rig.engine.setFinishResults([AppleTranscriberTests.finalResult("Said before it.", from: 0, to: 2)])

            rig.audio.send(item.event)
            await rig.log.waitForLine(item.line)
            XCTAssertEqual(rig.audio.stops, [false], item.line)

            // The recogniser itself is fine: the finalisation after the failure collects its last words.
            try await rig.transcriber.finalize()
            let finishes = await rig.engine.finishCount
            XCTAssertEqual(finishes, 1, item.line)
            await rig.log.waitForLine("status Paused")
            XCTAssertEqual(rig.log.lines, [
                "status Listening",
                item.line,
                "segment Said before it. 0.00-2.00",
                "partial ",
                "status Paused"
            ])
            await rig.end()
        }
    }

    @MainActor
    func testAFinalisationTheRecogniserFailsThrowsAndStillPauses() async throws {
        let rig = AppleRig()
        try await rig.start()
        await rig.engine.setFinishError(true)

        do {
            try await rig.transcriber.finalize()
            XCTFail("The finalisation should have thrown.")
        } catch {
            XCTAssertEqual(error as? TranscriberFailure, TranscriberFailureText.processingFailed)
        }
        await rig.log.waitForLine("status Paused")
        XCTAssertEqual(rig.log.lines, ["status Listening", "partial ", "status Paused"])
        XCTAssertFalse(rig.log.lines.contains { $0.hasPrefix("failed") }, "An unclean end is the thrown failure, not an event.")
        XCTAssertEqual(rig.audio.stops, [false])
        await rig.end()
    }

    @MainActor
    func testARecogniserThatDoesNotAnswerCannotHoldPauseForEver() async throws {
        let rig = AppleRig()
        try await rig.start()
        await rig.engine.setFinishGate(FakeGate())

        let finalizing = Task { try await rig.transcriber.finalize() }
        // The finalisation is waiting for the recogniser; its limit is the only sleep.
        await rig.clock.waitForSleepers()
        XCTAssertEqual(rig.transcriber.emittedEventCount, 1)

        await rig.clock.advance(by: AppleTranscriber.finalizeLimitSeconds)
        do {
            try await finalizing.value
            XCTFail("The finalisation should have thrown.")
        } catch {
            XCTAssertEqual(error as? TranscriberFailure, TranscriberFailureText.processingFailed)
        }
        let cancels = await rig.engine.cancelCount
        XCTAssertEqual(cancels, 1, "The recognition was ended.")
        await rig.log.waitForLine("status Paused")
        XCTAssertEqual(rig.log.lines, ["status Listening", "partial ", "status Paused"])
        XCTAssertEqual(AppleTranscriber.finalizeLimitSeconds, 10)
        await rig.end()
    }
}

/// An `AppleTranscriber` on fakes, with the language asset installed unless a test says otherwise.
@MainActor private final class AppleRig {
    let clock: ManualClock
    let audio: FakeAudioCapture
    let engine: FakeSystemSpeech
    let models: OnDeviceModelManager
    let transcriber: AppleTranscriber
    let log: TranscriberEventLog
    let generation = UUID()

    init(installed: Bool = true) {
        let clock = ManualClock()
        let audio = FakeAudioCapture()
        let engine = FakeSystemSpeech(known: installed ? .installed : .needsDownload)
        let models = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: FakeBatchEngine(), parakeet: FakeStreamingEngine()),
            activity: SessionActivity(),
            clock: clock,
            systemSpeech: engine
        )
        // What the live factory does: the recogniser is handed over only once its asset is installed.
        let transcriber = AppleTranscriber(engine: models.systemSpeechEngine, audio: audio, clock: clock, models: models)

        self.clock = clock
        self.audio = audio
        self.engine = engine
        self.models = models
        self.transcriber = transcriber
        self.log = TranscriberEventLog(transcriber)
    }

    func start(offset: Double = 0) async throws {
        try await transcriber.start(TranscriberStart(generation: generation, streamOffsetSeconds: offset))
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
