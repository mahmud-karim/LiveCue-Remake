import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The rolling recogniser of Fluid Speech Mini (D-29) on a fake window engine: when a pass runs, how much
/// audio it is given, the text that reaches the partial handler, the last pass of a stretch, and what an
/// engine error does.
final class RollingWindowRecognizerTests: XCTestCase {
    /// The texts the partial handler was called with. The recogniser calls it off the main actor.
    private final class PartialLog: @unchecked Sendable {
        private let lock = NSLock()
        private var texts: [String] = []

        func add(_ text: String) {
            lock.withLock { () -> Void in
                texts.append(text)
            }
        }

        var all: [String] {
            lock.withLock { texts }
        }
    }

    private func word(_ text: String, _ start: Double, _ end: Double) -> RecognizedWord {
        RecognizedWord(text: text, start: start, end: end)
    }

    private func heard(_ text: String, _ words: [RecognizedWord] = []) -> Result<WindowRecognition, any Error> {
        .success(WindowRecognition(text: text, words: words))
    }

    /// Audio at 16 kHz with one value, so a test can tell which audio a pass was given.
    private func audio(seconds: Double, of value: Float = 0.5) -> [Float] {
        [Float](repeating: value, count: Int((seconds * 16_000).rounded()))
    }

    private func makeRecognizer(_ engine: FakeWindowEngine) async -> (RollingWindowRecognizer, PartialLog) {
        let recognizer = RollingWindowRecognizer(engine: engine)
        let log = PartialLog()
        await recognizer.setPartialHandler { text in
            log.add(text)
        }
        return (recognizer, log)
    }

    // MARK: - Constants

    func testTheWindowIsFifteenSecondsAndAPassComesEverySecond() {
        XCTAssertEqual(RollingWindowRecognizer.sampleRate, 16_000)
        XCTAssertEqual(RollingWindowRecognizer.windowSamples, 240_000)
        XCTAssertEqual(RollingWindowRecognizer.passSamples, 16_000)
    }

    // MARK: - When a pass runs

    func testNothingIsTranscribedBeforeASecondOfAudioHasComeIn() async throws {
        let engine = FakeWindowEngine()
        let (recognizer, log) = await makeRecognizer(engine)

        let first = try await recognizer.process(samples: audio(seconds: 0.5))
        let second = try await recognizer.process(samples: audio(seconds: 0.4))
        XCTAssertFalse(first)
        XCTAssertFalse(second)
        var calls = await engine.calls
        XCTAssertEqual(calls, [])
        XCTAssertEqual(log.all, [])

        // The buffer that completes the second is transcribed with the ones before it.
        let third = try await recognizer.process(samples: audio(seconds: 0.1))
        XCTAssertTrue(third)
        calls = await engine.calls
        XCTAssertEqual(calls, [16_000])
    }

    func testAPassRunsAfterEverySecondOfNewAudio() async throws {
        let engine = FakeWindowEngine()
        // Someone is talking all the time: nothing is committed, so no audio goes.
        await engine.answer(
            heard("one", [word("one", 0.2, 0.9)]),
            heard("one two", [word("one", 0.2, 0.9), word("two", 1.1, 1.9)]),
            heard("one two three", [word("one", 0.2, 0.9), word("two", 1.1, 1.9), word("three", 2.1, 2.9)])
        )
        let (recognizer, log) = await makeRecognizer(engine)

        var passes: [Bool] = []
        for _ in 0..<30 {
            passes.append(try await recognizer.process(samples: audio(seconds: 0.1)))
        }

        // Every tenth buffer of a tenth of a second.
        XCTAssertEqual(passes.enumerated().filter(\.element).map(\.offset), [9, 19, 29])
        let calls = await engine.calls
        XCTAssertEqual(calls, [16_000, 32_000, 48_000])
        XCTAssertEqual(log.all, ["one", "one two", "one two three"])
    }

    func testAPassWaitsForTheShortestWindowTheEngineTakes() async throws {
        let engine = FakeWindowEngine(minimumSamples: 20_000)
        let (recognizer, _) = await makeRecognizer(engine)

        // A second has come in, but the engine takes nothing shorter than 1.25 s.
        let early = try await recognizer.process(samples: audio(seconds: 1))
        XCTAssertFalse(early)
        let enough = try await recognizer.process(samples: audio(seconds: 0.25))
        XCTAssertTrue(enough)
        let calls = await engine.calls
        XCTAssertEqual(calls, [20_000])
    }

    // MARK: - Text and audio after a pass

    func testThePartialHandlerGetsTheTextOnScreenAfterEveryPass() async throws {
        let engine = FakeWindowEngine()
        let question = [word("what", 0.2, 0.5), word("is", 0.6, 0.9), word("the", 1.2, 1.5), word("main", 1.5, 1.7)]
        await engine.answer(
            heard("what is", Array(question.prefix(2))),
            heard("what is the main", question),
            // A third second without a word: "main" ended 1.3 s ago, so all four words are committed.
            heard("what is the main", question),
            heard("advantage", [word("advantage", 0.5, 1.2)])
        )
        let (recognizer, log) = await makeRecognizer(engine)

        for _ in 0..<4 {
            try await recognizer.process(samples: audio(seconds: 1))
        }

        XCTAssertEqual(log.all, ["what is", "what is the main", "what is the main", "what is the main advantage"])
        // After the pause only the last 0.3 s are kept: the fourth pass hears 1.3 s.
        let calls = await engine.calls
        XCTAssertEqual(calls, [16_000, 32_000, 48_000, 20_800])

        let text = try await recognizer.finish()
        XCTAssertEqual(text, "what is the main advantage")
    }

    func testSilenceIsCutDownToHalfASecond() async throws {
        let engine = FakeWindowEngine()
        let (recognizer, log) = await makeRecognizer(engine)

        for _ in 0..<4 {
            try await recognizer.process(samples: audio(seconds: 1))
        }

        // Three seconds without a word are longer than two: half a second stays for the fourth pass.
        let calls = await engine.calls
        XCTAssertEqual(calls, [16_000, 32_000, 48_000, 24_000])
        XCTAssertEqual(log.all, ["", "", "", ""])
        let text = try await recognizer.finish()
        XCTAssertEqual(text, "")
    }

    func testNoPassIsGivenMoreThanFifteenSeconds() async throws {
        let engine = FakeWindowEngine()
        await engine.answer(heard("a long talk without times"), heard("and more"))
        let (recognizer, log) = await makeRecognizer(engine)

        // Seventeen seconds at once: two seconds of one value, then fifteen that begin with a mark.
        var samples = audio(seconds: 2, of: 0.25)
        samples.append(0.75)
        samples.append(contentsOf: audio(seconds: 15, of: 0.5).dropFirst())
        XCTAssertEqual(samples.count, 272_000)
        let passed = try await recognizer.process(samples: samples)

        XCTAssertTrue(passed)
        var calls = await engine.calls
        let firsts = await engine.firsts
        XCTAssertEqual(calls, [240_000], "The model's input is 15 seconds.")
        XCTAssertEqual(firsts, [0.75], "The newest audio is the audio that is heard.")
        // A window that full is committed whole, and 0.2 s of it stay.
        XCTAssertEqual(log.all, ["a long talk without times"])

        try await recognizer.process(samples: audio(seconds: 1))
        calls = await engine.calls
        XCTAssertEqual(calls, [240_000, 19_200])
        XCTAssertEqual(log.all, ["a long talk without times", "a long talk without times and more"])
    }

    // MARK: - The end of a stretch

    func testFinishTranscribesShortAudioFilledUpWithSilence() async throws {
        let engine = FakeWindowEngine()
        await engine.answer(heard("hi", [word("hi", 0.02, 0.08)]))
        let (recognizer, log) = await makeRecognizer(engine)

        // A tenth of a second: too little for a pass, and less than the engine takes.
        try await recognizer.process(samples: audio(seconds: 0.1))
        let text = try await recognizer.finish()

        XCTAssertEqual(text, "hi")
        let calls = await engine.calls
        let firsts = await engine.firsts
        let lasts = await engine.lasts
        XCTAssertEqual(calls, [4_800], "Filled up to the shortest window.")
        XCTAssertEqual(firsts, [0.5])
        XCTAssertEqual(lasts, [0], "With silence.")
        XCTAssertEqual(log.all, [], "The text of the last pass is returned, not shown.")
    }

    func testFinishHearsWhatCameInAfterTheLastPass() async throws {
        let engine = FakeWindowEngine()
        await engine.answer(
            heard("one", [word("one", 0.2, 0.9)]),
            heard("one two", [word("one", 0.2, 0.9), word("two", 1.0, 1.4)])
        )
        let (recognizer, _) = await makeRecognizer(engine)

        try await recognizer.process(samples: audio(seconds: 1))
        try await recognizer.process(samples: audio(seconds: 0.5))
        let text = try await recognizer.finish()

        XCTAssertEqual(text, "one two")
        let calls = await engine.calls
        XCTAssertEqual(calls, [16_000, 24_000])
    }

    func testFinishWithoutNewAudioAsksTheEngineNothing() async throws {
        let engine = FakeWindowEngine()
        await engine.answer(heard("one", [word("one", 0.2, 0.9)]))
        let (recognizer, _) = await makeRecognizer(engine)

        // Nothing came in at all.
        let empty = try await recognizer.finish()
        XCTAssertEqual(empty, "")
        var calls = await engine.calls
        XCTAssertEqual(calls, [])

        // The last pass heard everything there is: its open words are the end of the text.
        try await recognizer.process(samples: audio(seconds: 1))
        let text = try await recognizer.finish()
        XCTAssertEqual(text, "one")
        calls = await engine.calls
        XCTAssertEqual(calls, [16_000])

        // The stretch is over: finishing again returns nothing of it.
        let again = try await recognizer.finish()
        XCTAssertEqual(again, "")
    }

    func testResetStartsANewStretchWithoutAudioOrText() async throws {
        let engine = FakeWindowEngine()
        await engine.answer(heard("one", [word("one", 0.2, 0.9)]), heard("two", [word("two", 0.2, 0.9)]))
        let (recognizer, log) = await makeRecognizer(engine)

        try await recognizer.process(samples: audio(seconds: 1))
        try await recognizer.process(samples: audio(seconds: 0.5))
        await recognizer.reset()
        try await recognizer.process(samples: audio(seconds: 1))

        let calls = await engine.calls
        XCTAssertEqual(calls, [16_000, 16_000], "The audio of the stretch before is gone.")
        // The handler stays across a reset.
        XCTAssertEqual(log.all, ["one", "two"])
        let text = try await recognizer.finish()
        XCTAssertEqual(text, "two")
    }

    func testCleanupReleasesTheEngine() async throws {
        let engine = FakeWindowEngine()
        let (recognizer, log) = await makeRecognizer(engine)
        try await recognizer.process(samples: audio(seconds: 0.5))

        await recognizer.cleanup()

        let cleanups = await engine.cleanupCount
        XCTAssertEqual(cleanups, 1)
        let text = try await recognizer.finish()
        XCTAssertEqual(text, "")
        XCTAssertEqual(log.all, [])
    }

    // MARK: - Failures

    func testAnEngineErrorIsThrownByProcess() async throws {
        let engine = FakeWindowEngine()
        await engine.answer(heard("one", [word("one", 0.2, 0.9)]), .failure(FakeEngineFailure()))
        let (recognizer, log) = await makeRecognizer(engine)
        try await recognizer.process(samples: audio(seconds: 1))

        do {
            try await recognizer.process(samples: audio(seconds: 1))
            XCTFail("The pass should have failed.")
        } catch {
            XCTAssertTrue(error is FakeEngineFailure)
        }
        XCTAssertEqual(log.all, ["one"], "A failed pass shows nothing.")

        // What was heard before the failure is kept. No audio is new, so nothing is asked again.
        let text = try await recognizer.finish()
        XCTAssertEqual(text, "one")
        let calls = await engine.calls
        XCTAssertEqual(calls, [16_000, 32_000])
    }

    func testAFailedLastPassCostsOnlyItsOwnWords() async throws {
        let engine = FakeWindowEngine()
        await engine.answer(heard("one", [word("one", 0.2, 0.9)]), .failure(FakeEngineFailure()))
        let (recognizer, _) = await makeRecognizer(engine)
        try await recognizer.process(samples: audio(seconds: 1))
        try await recognizer.process(samples: audio(seconds: 0.5))

        let text = try await recognizer.finish()

        XCTAssertEqual(text, "one")
        let calls = await engine.calls
        XCTAssertEqual(calls, [16_000, 24_000])
    }
}
