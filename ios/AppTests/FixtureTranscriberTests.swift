import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The speech fixtures of section 4.9: the fixture transcriber, its factory, and the fixture engine
/// loader. They open no microphone and no socket, and their time is the clock they are given.
final class FixtureTranscriberTests: XCTestCase {
    // MARK: - Start

    @MainActor
    func testAStreamingStartSaysListeningOpensUsageAndCommitsTheFixtureSegment() async throws {
        let rig = FixtureRig(.meta)
        try await rig.start()

        // All three were emitted before the start returned.
        XCTAssertEqual(rig.transcriber.emittedEventCount, 3)
        await rig.log.wait(for: 3)
        XCTAssertEqual(rig.log.lines, [
            "status Listening",
            "usage begin",
            "segment What is the main advantage of local transcription? 0.00-3.00"
        ])
        XCTAssertTrue(rig.log.events.allSatisfy { $0.generation == rig.generation })
        XCTAssertEqual(rig.transcriber.provider, .meta)
        await rig.end()
    }

    @MainActor
    func testTheSegmentStartsAtTheStreamOffset() async throws {
        let rig = FixtureRig(.nemotron)
        try await rig.start(offset: 42)

        await rig.log.wait(for: 3)
        XCTAssertEqual(rig.log.lines.last, "segment What is the main advantage of local transcription? 42.00-45.00")
        await rig.end()
    }

    @MainActor
    func testAnOnDeviceProviderHasNoUsage() async throws {
        for provider in [SpeechProvider.voz, SpeechProvider.parakeet] {
            let rig = FixtureRig(provider)
            try await rig.start()
            await rig.tick()
            await rig.tick()
            try await rig.transcriber.finalize()

            await rig.log.wait(for: rig.transcriber.emittedEventCount)
            XCTAssertEqual(rig.log.lines, [
                "status Listening",
                "segment What is the main advantage of local transcription? 0.00-3.00",
                "level 0.4",
                "level 0.4",
                "status Paused"
            ], "\(provider)")
            await rig.end()
        }
    }

    // MARK: - The one-second loop

    @MainActor
    func testFixtureUsageGainsOneSecondPerSecond() async throws {
        let rig = FixtureRig(.meta)
        try await rig.start()

        await rig.tick()
        XCTAssertEqual(rig.transcriber.emittedEventCount, 5)
        XCTAssertEqual(Array(rig.log.lines.dropFirst(3)), [
            "usage update sent=1000.0 processed=1000.0 text=true",
            "level 0.4"
        ])

        // Two more seconds.
        await rig.tick()
        await rig.tick()
        XCTAssertEqual(rig.transcriber.emittedEventCount, 9)
        XCTAssertEqual(Array(rig.log.lines.dropFirst(5)), [
            "usage update sent=2000.0 processed=2000.0 text=true",
            "level 0.4",
            "usage update sent=3000.0 processed=3000.0 text=true",
            "level 0.4"
        ])

        // Three seconds of audio are billable while the stream is open: the cost label moves.
        let usage = rig.usage()
        XCTAssertEqual(usage.streams.count, 1)
        XCTAssertEqual(usage.estimatedSeconds, 3)

        // Less than a second later nothing new has happened.
        await rig.clock.advance(by: 0.5)
        await SpeechTest.settle()
        XCTAssertEqual(rig.transcriber.emittedEventCount, 9)
        await rig.end()
    }

    // MARK: - Pause, Resume and the end

    @MainActor
    func testPauseClosesTheUsageStreamAndResumeOpensANewOne() async throws {
        let rig = FixtureRig(.meta)
        try await rig.start()
        await rig.tick()

        try await rig.transcriber.finalize()
        let paused = rig.transcriber.emittedEventCount
        await rig.log.wait(for: paused)
        XCTAssertEqual(Array(rig.log.lines.suffix(2)), ["usage finish true", "status Paused"])
        XCTAssertEqual(rig.clock.sleeperCount, 0, "The loop has stopped.")

        // Paused: time passes and nothing is added.
        await rig.clock.advance(by: 5)
        await SpeechTest.settle()
        XCTAssertEqual(rig.transcriber.emittedEventCount, paused)
        let pending = try await rig.transcriber.transcribePending()
        XCTAssertEqual(pending, 0)
        try await rig.transcriber.finalize()
        XCTAssertEqual(rig.transcriber.emittedEventCount, paused, "Nothing is finalised twice.")

        // Resume: a new generation, the elapsed seconds as the offset, a new usage stream.
        let second = UUID()
        try await rig.transcriber.start(TranscriberStart(generation: second, streamOffsetSeconds: 6))
        await rig.tick()
        XCTAssertEqual(rig.transcriber.emittedEventCount, paused + 5)
        let resumed = rig.log.events.dropFirst(paused)
        XCTAssertTrue(resumed.allSatisfy { $0.generation == second })
        XCTAssertEqual(resumed.map(TranscriberEventLog.line), [
            "status Listening",
            "usage begin",
            "segment What is the main advantage of local transcription? 6.00-9.00",
            "usage update sent=1000.0 processed=1000.0 text=true",
            "level 0.4"
        ])

        let usage = rig.usage()
        XCTAssertEqual(usage.streams.count, 2)
        XCTAssertEqual(usage.streams.first?.state, SpeechStreamUsage.completed)
        XCTAssertEqual(usage.streams.last?.state, SpeechStreamUsage.streaming)
        XCTAssertEqual(usage.estimatedSeconds, 2)
        await rig.end()
    }

    @MainActor
    func testShutdownStopsTheLoopAndEmitsNothing() async throws {
        let rig = FixtureRig(.qwen3)
        try await rig.start()
        await rig.clock.waitForSleepers()
        let emitted = rig.transcriber.emittedEventCount

        await rig.transcriber.shutdown()
        XCTAssertEqual(rig.clock.sleeperCount, 0)
        await rig.clock.advance(by: 3)
        await SpeechTest.settle()
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)

        // Idempotent, and a finalisation afterwards has nothing to do.
        await rig.transcriber.shutdown()
        try await rig.transcriber.finalize()
        XCTAssertEqual(rig.transcriber.emittedEventCount, emitted)
        await rig.end()
    }

    // MARK: - The factory and the engines

    @MainActor
    func testTheFactoryMakesAFixtureTranscriberForEveryProvider() async {
        let factory = FixtureTranscriberFactory(clock: ManualClock())
        for provider in SpeechProvider.allCases {
            let transcriber = factory.makeTranscriber(for: provider)
            XCTAssertTrue(transcriber is FixtureTranscriber)
            XCTAssertEqual(transcriber.provider, provider)
        }
        // A new one for every session.
        XCTAssertFalse(factory.makeTranscriber(for: .meta) === factory.makeTranscriber(for: .meta))
    }

    @MainActor
    func testTheFixtureLoaderReportsHalfWaitsTwoSecondsAndReturnsAnEngine() async throws {
        let clock = ManualClock()
        let models = OnDeviceModelManager(loader: FixtureEngineLoader(clock: clock), activity: SessionActivity(), clock: clock)

        let preparing = Task { try await models.prepare(.voz) }
        await clock.waitForSleepers()
        await SpeechTest.waitUntil { models.preparation?.fraction == 0.5 }
        XCTAssertTrue(models.isPreparing)
        XCTAssertEqual(models.statusLine, "Downloading Voz…")
        XCTAssertNotNil(models.preparation?.counterText)
        XCTAssertFalse(models.isLoaded(for: .voz))

        await clock.advance(by: FixtureValues.preparationHoldSeconds)
        try await preparing.value
        XCTAssertTrue(models.isLoaded(for: .voz))
        XCTAssertEqual(models.statusLine, "Ready")
        XCTAssertNil(models.preparation)

        // The same for Parakeet, with a file count.
        let reloading = Task { try await models.prepare(.parakeet) }
        await clock.waitForSleepers()
        await SpeechTest.waitUntil { models.preparation?.fraction == 0.5 }
        XCTAssertEqual(models.statusLine, "Downloading Parakeet…")
        XCTAssertEqual(models.preparation?.counterText, "3 / 6 files")
        await clock.advance(by: 2)
        try await reloading.value
        XCTAssertTrue(models.isLoaded(for: .parakeet))
        XCTAssertFalse(models.isLoaded(for: .voz))
    }

    @MainActor
    func testTheFixtureEnginesHearNothing() async throws {
        let batch = try await FixtureBatchEngine().transcribe(samples: [Float](repeating: 0, count: 32_000))
        XCTAssertEqual(batch, BatchRecognition(words: [], duration: 2))

        let streaming = FixtureStreamingEngine()
        await streaming.reset()
        await streaming.setPartialHandler { _ in }
        try await streaming.process(samples: [0, 0, 0])
        let text = try await streaming.finish()
        XCTAssertEqual(text, "")
        await streaming.cleanup()
    }
}

/// A `FixtureTranscriber` on a clock that only moves when told.
@MainActor private final class FixtureRig {
    let clock: ManualClock
    let transcriber: FixtureTranscriber
    let log: TranscriberEventLog
    let generation = UUID()

    init(_ provider: SpeechProvider) {
        let clock = ManualClock()
        let transcriber = FixtureTranscriber(provider: provider, clock: clock)
        self.clock = clock
        self.transcriber = transcriber
        self.log = TranscriberEventLog(transcriber)
    }

    func start(offset: Double = 0) async throws {
        try await transcriber.start(TranscriberStart(generation: generation, streamOffsetSeconds: offset))
    }

    /// One second passes: the loop ticks once, its events are read, and it sleeps again. Time moves one
    /// tick at a time, so that the loop's next sleep always starts from the time the test has reached.
    func tick() async {
        await clock.waitForSleepers()
        let expected = transcriber.emittedEventCount + (transcriber.provider.streamsAudio ? 2 : 1)
        await clock.advance(by: FixtureTranscriber.tickSeconds)
        await log.wait(for: expected)
        await clock.waitForSleepers()
    }

    /// The usage a session would hold after the events read so far.
    func usage() -> TranscriptionUsage {
        var usage = TranscriptionUsage(provider: transcriber.provider.rawValue)
        for event in log.events {
            if case .usage(let update) = event.kind {
                usage.apply(update)
            }
        }
        return usage
    }

    func end() async {
        await transcriber.shutdown()
        log.stop()
    }
}
