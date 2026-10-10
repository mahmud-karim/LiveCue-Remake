import Foundation
import LiveCueCore
import XCTest

/// Usage operations and derived values.
final class TranscriptionUsageTests: XCTestCase {
    /// Money comparisons use this tolerance (spec 02 section 10.8).
    private let tolerance = 0.000_000_1

    // MARK: - What a new value holds

    func testInitialValues() {
        let meta = TranscriptionUsage()
        XCTAssertEqual(meta.provider, "meta")
        XCTAssertEqual(meta.hourlyRateUSD, 0.18)
        XCTAssertEqual(meta.pricingDate, "2026-09-28")
        XCTAssertNil(meta.firstTextMs)
        XCTAssertNil(meta.finalizationMs)
        XCTAssertTrue(meta.streams.isEmpty)
        XCTAssertEqual(meta.estimatedSeconds, 0)
        XCTAssertEqual(meta.estimatedUSD, 0)
        XCTAssertEqual(meta.reportedSeconds, 0)
        XCTAssertFalse(meta.isIncomplete)
        XCTAssertEqual(meta.formattedCost, "$0.00000")
        XCTAssertEqual(TranscriptionUsage(provider: "meta"), meta)

        for provider in SpeechProvider.allCases where provider != .meta {
            let usage = TranscriptionUsage(provider: provider.rawValue)
            XCTAssertEqual(usage.provider, provider.rawValue)
            XCTAssertEqual(usage.hourlyRateUSD, 0, provider.rawValue)
            XCTAssertEqual(usage.pricingDate, "2026-09-28", provider.rawValue)
        }
    }

    // MARK: - Old tests (spec 02 sections 10.3, 10.7 and 10.8)

    /// AC-CORE-033, AC-CORE-034 and AC-CORE-035.
    func testMetaUsageOfOneCompletedStream() {
        var usage = TranscriptionUsage(provider: "meta")
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: 2_000, processedMs: 2_000, hasTranscript: true)
        usage.finish(stream, completed: true)

        XCTAssertEqual(usage.reportedSeconds, 2)
        XCTAssertGreaterThan(usage.estimatedUSD, 0)
        XCTAssertFalse(usage.isIncomplete)
    }

    /// AC-CORE-082.
    func testLocalProvidersNeverAccrueAPICost() {
        for provider in [SpeechProvider.nemotron, SpeechProvider.qwen3] {
            var usage = TranscriptionUsage(provider: provider.rawValue)
            let stream = UUID()
            usage.begin(stream)
            usage.update(stream, sentMs: 60_000, processedMs: 60_000, hasTranscript: true)
            usage.finish(stream, completed: true)

            XCTAssertEqual(usage.estimatedUSD, 0, provider.rawValue)
            XCTAssertEqual(usage.reportedSeconds, 60, provider.rawValue)
            XCTAssertEqual(usage.estimatedSeconds, 60, provider.rawValue)
            XCTAssertEqual(usage.formattedCost, "$0.00000", provider.rawValue)
            XCTAssertFalse(usage.isIncomplete, provider.rawValue)
        }
    }

    /// AC-CORE-090 to AC-CORE-094.
    func testLiveEstimateReconcilesToProcessedAudioAndRoundsPerStream() {
        var usage = TranscriptionUsage()
        let first = UUID()
        usage.begin(first)
        usage.update(first, sentMs: 31_900, processedMs: 30_400, hasTranscript: true)
        XCTAssertEqual(usage.estimatedUSD, 31 * 0.000_05, accuracy: tolerance, "While streaming, the larger of sent and processed.")
        XCTAssertEqual(usage.estimatedSeconds, 31)

        usage.finish(first, completed: true)
        XCTAssertEqual(usage.estimatedUSD, 30 * 0.000_05, accuracy: tolerance, "Reconciled to the processed audio.")
        XCTAssertEqual(usage.estimatedSeconds, 30)

        let second = UUID()
        usage.begin(second)
        usage.update(second, sentMs: 1_900, processedMs: 1_900, hasTranscript: true)
        usage.finish(second, completed: true)
        XCTAssertEqual(usage.estimatedSeconds, 31, "30 + 1, not the floor of 30.4 + 1.9.")
        XCTAssertEqual(usage.formattedCost, "$0.00155")
        XCTAssertFalse(usage.isIncomplete)
        XCTAssertEqual(usage.reportedSeconds, 31)
    }

    /// AC-CORE-095 to AC-CORE-097.
    func testEverySecondChangesCounterAndDoesNotCountPausedTime() {
        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: 1_000, processedMs: 1_000, hasTranscript: true)
        XCTAssertEqual(usage.formattedCost, "$0.00005")

        usage.update(stream, sentMs: 2_000, processedMs: 2_000)
        XCTAssertEqual(usage.formattedCost, "$0.00010")

        usage.finish(stream, completed: true)
        usage.update(stream, sentMs: 999_000, processedMs: 999_000)
        XCTAssertEqual(usage.formattedCost, "$0.00010", "A finished stream ignores updates.")
        XCTAssertEqual(usage.streams.first?.sentMs, 2_000)
        XCTAssertEqual(usage.streams.first?.processedMs, 2_000)
    }

    /// AC-CORE-098 to AC-CORE-100.
    func testProgressIsCumulativeNotSummedAndRejectsInvalidNumbers() {
        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        usage.begin(stream)
        XCTAssertEqual(usage.streams.count, 1)

        usage.update(stream, sentMs: 3_000, processedMs: 2_000)
        usage.update(stream, sentMs: -1, processedMs: .nan)
        usage.update(stream, processedMs: 1_000)
        XCTAssertEqual(usage.reportedSeconds, 2)
        XCTAssertEqual(usage.estimatedSeconds, 3)
        XCTAssertEqual(usage.streams.first?.sentMs, 3_000)
        XCTAssertEqual(usage.streams.first?.processedMs, 2_000)
    }

    /// AC-CORE-101 to AC-CORE-103.
    func testInterruptedStreamAndFailureBeforeTranscript() {
        var usage = TranscriptionUsage()
        let failed = UUID()
        usage.begin(failed)
        usage.update(failed, sentMs: 10_000, processedMs: 6_000, hasTranscript: false)
        usage.finish(failed, completed: false)
        XCTAssertTrue(usage.isIncomplete)
        XCTAssertEqual(usage.estimatedUSD, 0)
        XCTAssertEqual(usage.reportedSeconds, 6, "Its audio still counts as reported (CORE-A16).")

        let dropped = UUID()
        usage.begin(dropped)
        usage.update(dropped, sentMs: 5_000, processedMs: 4_000, hasTranscript: true)
        usage.finish(dropped, completed: false)
        XCTAssertEqual(usage.estimatedUSD, 0.000_2, accuracy: tolerance)
        XCTAssertEqual(usage.estimatedSeconds, 4)
        XCTAssertTrue(usage.isIncomplete)
    }

    /// AC-CORE-107.
    func testOnDeviceProvidersCostNothing() {
        XCTAssertEqual(TranscriptionUsage(provider: "parakeet").estimatedUSD, 0)
        XCTAssertEqual(TranscriptionUsage(provider: "voz").estimatedUSD, 0)

        // Even with audio, a provider without a rate costs nothing.
        var usage = TranscriptionUsage(provider: "parakeet")
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: 3_600_000, processedMs: 3_600_000, hasTranscript: true)
        XCTAssertEqual(usage.estimatedSeconds, 3_600)
        XCTAssertEqual(usage.estimatedUSD, 0)
        XCTAssertEqual(usage.formattedCost, "$0.00000")
    }

    // MARK: - Supplementary (AC-CORE-S17)

    func testUpdatesAndFinishesForAnUnknownStreamAreIgnored() {
        var usage = TranscriptionUsage()
        let untouched = usage
        usage.update(UUID(), sentMs: 5_000, processedMs: 5_000, hasTranscript: true)
        usage.finish(UUID(), completed: true)
        usage.finish(UUID(), completed: false)
        XCTAssertEqual(usage, untouched)
        XCTAssertTrue(usage.streams.isEmpty)
        XCTAssertFalse(usage.isIncomplete)

        let known = UUID()
        usage.begin(known)
        usage.update(UUID(), sentMs: 5_000)
        XCTAssertEqual(usage.streams, [SpeechStreamUsage(id: known)])
    }

    func testFinishingAStreamTwiceKeepsTheFirstOutcome() {
        var usage = TranscriptionUsage()
        let completed = UUID()
        usage.begin(completed)
        usage.update(completed, sentMs: 2_000, processedMs: 2_000, hasTranscript: true)
        usage.finish(completed, completed: true)
        usage.finish(completed, completed: false)
        XCTAssertEqual(usage.streams[0].state, SpeechStreamUsage.completed)
        XCTAssertFalse(usage.isIncomplete)

        let interrupted = UUID()
        usage.begin(interrupted)
        usage.finish(interrupted, completed: false)
        usage.finish(interrupted, completed: true)
        XCTAssertEqual(usage.streams[1].state, SpeechStreamUsage.interrupted)

        // Beginning a finished stream again does not reopen it.
        usage.begin(completed)
        usage.begin(interrupted)
        XCTAssertEqual(usage.streams.count, 2)
        XCTAssertEqual(usage.streams.map(\.state), [SpeechStreamUsage.completed, SpeechStreamUsage.interrupted])
    }

    func testACompletedStreamWithoutAProcessedValueIsCostedFromSentAudioAndIsIncomplete() {
        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: 7_900, hasTranscript: true)
        usage.finish(stream, completed: true)

        XCTAssertNil(usage.streams[0].processedMs)
        XCTAssertEqual(usage.estimatedSeconds, 7)
        XCTAssertEqual(usage.estimatedUSD, 7 * 0.000_05, accuracy: tolerance)
        XCTAssertEqual(usage.reportedSeconds, 0)
        XCTAssertTrue(usage.isIncomplete)
    }

    // MARK: - Positions

    func testPositionsNeverDecreaseAndInvalidNumbersAreIgnored() {
        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        XCTAssertEqual(usage.streams[0], SpeechStreamUsage(id: stream, sentMs: 0, processedMs: nil, hasTranscript: false, state: "streaming"))

        usage.update(stream, sentMs: 5_000)
        XCTAssertEqual(usage.streams[0].sentMs, 5_000)
        XCTAssertNil(usage.streams[0].processedMs, "A position that was not supplied stays absent.")

        usage.update(stream, sentMs: 4_000, processedMs: 3_000)
        XCTAssertEqual(usage.streams[0].sentMs, 5_000)
        XCTAssertEqual(usage.streams[0].processedMs, 3_000)

        usage.update(stream, processedMs: 2_500)
        XCTAssertEqual(usage.streams[0].processedMs, 3_000)

        for invalid in [-1, -0.001, Double.nan, Double.infinity, -Double.infinity] {
            usage.update(stream, sentMs: invalid, processedMs: invalid)
        }
        XCTAssertEqual(usage.streams[0].sentMs, 5_000)
        XCTAssertEqual(usage.streams[0].processedMs, 3_000)

        usage.update(stream, sentMs: 0, processedMs: 0)
        XCTAssertEqual(usage.streams[0].sentMs, 5_000, "Zero is valid and changes nothing.")
        XCTAssertEqual(usage.streams[0].processedMs, 3_000)
    }

    func testAProcessedPositionOfZeroIsPresent() {
        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, processedMs: 0)
        XCTAssertEqual(usage.streams[0].processedMs, 0)

        // So a stream completed at zero is complete, not "completed with nothing reported".
        usage.finish(stream, completed: true)
        XCTAssertFalse(usage.isIncomplete)
    }

    func testHasTranscriptIsOredIn() {
        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        XCTAssertFalse(usage.streams[0].hasTranscript)
        usage.update(stream, sentMs: 100)
        XCTAssertFalse(usage.streams[0].hasTranscript)
        usage.update(stream, hasTranscript: true)
        XCTAssertTrue(usage.streams[0].hasTranscript)
        usage.update(stream, sentMs: 200, hasTranscript: false)
        XCTAssertTrue(usage.streams[0].hasTranscript, "Once true, it stays true.")
    }

    // MARK: - Derived values

    func testBillableSecondsOfEachStreamState() {
        // Streaming: the larger of sent and processed, also when the provider is ahead of the phone.
        var streaming = TranscriptionUsage()
        let ahead = UUID()
        streaming.begin(ahead)
        streaming.update(ahead, sentMs: 2_400, processedMs: 5_999)
        XCTAssertEqual(streaming.estimatedSeconds, 5)
        XCTAssertEqual(streaming.reportedSeconds, 5)
        XCTAssertFalse(streaming.isIncomplete, "A stream that is still open is not incomplete.")

        // Completed: the processed audio, even when less was processed than sent.
        var completed = streaming
        completed.update(ahead, sentMs: 9_000)
        XCTAssertEqual(completed.estimatedSeconds, 9)
        completed.finish(ahead, completed: true)
        XCTAssertEqual(completed.estimatedSeconds, 5)

        // Interrupted with text: the processed audio.
        var interrupted = TranscriptionUsage()
        let dropped = UUID()
        interrupted.begin(dropped)
        interrupted.update(dropped, sentMs: 9_000, processedMs: 3_999, hasTranscript: true)
        interrupted.finish(dropped, completed: false)
        XCTAssertEqual(interrupted.estimatedSeconds, 3)

        // Interrupted with text but nothing reported: the sent audio.
        var unreported = TranscriptionUsage()
        let silent = UUID()
        unreported.begin(silent)
        unreported.update(silent, sentMs: 9_000, hasTranscript: true)
        unreported.finish(silent, completed: false)
        XCTAssertEqual(unreported.estimatedSeconds, 9)
        XCTAssertEqual(unreported.reportedSeconds, 0)
    }

    func testSecondsAreRoundedDownPerStreamBeforeSumming() {
        var usage = TranscriptionUsage()
        for _ in 0..<4 {
            let stream = UUID()
            usage.begin(stream)
            usage.update(stream, sentMs: 999, processedMs: 999, hasTranscript: true)
            usage.finish(stream, completed: true)
        }
        XCTAssertEqual(usage.estimatedSeconds, 0, "Four streams of 0.999 s are no whole second.")
        XCTAssertEqual(usage.reportedSeconds, 0)
        XCTAssertEqual(usage.formattedCost, "$0.00000")

        let whole = UUID()
        usage.begin(whole)
        usage.update(whole, sentMs: 1_000)
        XCTAssertEqual(usage.estimatedSeconds, 1)
        XCTAssertEqual(usage.reportedSeconds, 0)
    }

    func testEstimatedUSDUsesTheStoredRate() throws {
        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: 3_600_000, processedMs: 3_600_000, hasTranscript: true)
        usage.finish(stream, completed: true)
        XCTAssertEqual(usage.estimatedSeconds, 3_600)
        XCTAssertEqual(usage.estimatedUSD, 0.18, accuracy: tolerance)
        XCTAssertEqual(usage.formattedCost, "$0.18000")

        // A session keeps the rate it was recorded at.
        usage.hourlyRateUSD = 0.36
        XCTAssertEqual(usage.estimatedUSD, 0.36, accuracy: tolerance)
        XCTAssertEqual(usage.formattedCost, "$0.36000")
        let reloaded = try LiveCueJSON.decode(TranscriptionUsage.self, from: LiveCueJSON.encode(usage))
        XCTAssertEqual(reloaded.hourlyRateUSD, 0.36)
        XCTAssertEqual(reloaded.estimatedUSD, 0.36, accuracy: tolerance)
    }

    func testFormattedCostHasFiveDecimalsAndADot() {
        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        let expected: [(seconds: Double, text: String)] = [
            (0, "$0.00000"), (1, "$0.00005"), (9, "$0.00045"), (20, "$0.00100"), (31, "$0.00155"),
            (600, "$0.03000"), (3_600, "$0.18000"), (36_000, "$1.80000"), (360_000, "$18.00000")
        ]
        for row in expected {
            usage.update(stream, sentMs: row.seconds * 1_000)
            XCTAssertEqual(usage.formattedCost, row.text, "\(row.seconds) s")
            XCTAssertFalse(usage.formattedCost.contains(","), "\(row.seconds) s")
        }
    }

    func testAStoredValueOutOfRangeDoesNotTrap() throws {
        let json = """
        {"provider":"meta","hourlyRateUSD":0.18,"pricingDate":"2026-09-28","streams":[\
        {"id":"6F9619FF-8B86-4011-B42D-00C04FC964FF","sentMs":1e300,"processedMs":1e300,"hasTranscript":true,"state":"completed"},\
        {"id":"7F9619FF-8B86-4011-B42D-00C04FC964FF","sentMs":-5000,"processedMs":-5000,"hasTranscript":true,"state":"completed"},\
        {"id":"8F9619FF-8B86-4011-B42D-00C04FC964FF","sentMs":2500,"hasTranscript":true,"state":"somethingNew"}]}
        """
        let usage = try LiveCueJSON.decode(TranscriptionUsage.self, from: Data(json.utf8))
        XCTAssertEqual(usage.streams.count, 3)
        XCTAssertEqual(usage.estimatedSeconds, Int.max, "A sum beyond the range saturates.")
        XCTAssertEqual(usage.reportedSeconds, Int.max)
        XCTAssertTrue(usage.estimatedUSD.isFinite)

        let small = """
        {"provider":"meta","hourlyRateUSD":0.18,"pricingDate":"2026-09-28","streams":[\
        {"id":"7F9619FF-8B86-4011-B42D-00C04FC964FF","sentMs":-5000,"processedMs":-5000,"hasTranscript":true,"state":"completed"},\
        {"id":"8F9619FF-8B86-4011-B42D-00C04FC964FF","sentMs":2500,"hasTranscript":true,"state":"somethingNew"}]}
        """
        let modest = try LiveCueJSON.decode(TranscriptionUsage.self, from: Data(small.utf8))
        XCTAssertEqual(modest.estimatedSeconds, 2, "A negative position counts as nothing; an unknown state is costed like a finished one.")
        XCTAssertEqual(modest.reportedSeconds, 0)
        XCTAssertFalse(modest.isIncomplete)
    }

    // MARK: - apply(_:)

    func testApplyEqualsTheDirectCalls() {
        let first = UUID()
        let second = UUID()
        let unknown = UUID()

        var direct = TranscriptionUsage()
        direct.begin(first)
        direct.begin(first)
        direct.update(first, sentMs: 31_900, processedMs: 30_400, hasTranscript: true)
        direct.update(first, sentMs: nil, processedMs: 30_900, hasTranscript: false)
        direct.update(first, sentMs: -1, processedMs: .nan, hasTranscript: false)
        direct.update(unknown, sentMs: 1, processedMs: 1, hasTranscript: true)
        direct.finish(first, completed: true)
        direct.finish(first, completed: false)
        direct.begin(second)
        direct.update(second, sentMs: 5_000, processedMs: nil, hasTranscript: false)
        direct.finish(second, completed: false)
        direct.finish(unknown, completed: true)

        var applied = TranscriptionUsage()
        let updates: [UsageUpdate] = [
            .begin(id: first),
            .begin(id: first),
            .update(id: first, sentMs: 31_900, processedMs: 30_400, hasTranscript: true),
            .update(id: first, sentMs: nil, processedMs: 30_900, hasTranscript: false),
            .update(id: first, sentMs: -1, processedMs: .nan, hasTranscript: false),
            .update(id: unknown, sentMs: 1, processedMs: 1, hasTranscript: true),
            .finish(id: first, completed: true),
            .finish(id: first, completed: false),
            .begin(id: second),
            .update(id: second, sentMs: 5_000, processedMs: nil, hasTranscript: false),
            .finish(id: second, completed: false),
            .finish(id: unknown, completed: true)
        ]
        for update in updates {
            applied.apply(update)
        }

        XCTAssertEqual(applied, direct)
        XCTAssertEqual(
            applied.streams,
            [
                SpeechStreamUsage(id: first, sentMs: 31_900, processedMs: 30_900, hasTranscript: true, state: SpeechStreamUsage.completed),
                SpeechStreamUsage(id: second, sentMs: 5_000, processedMs: nil, hasTranscript: false, state: SpeechStreamUsage.interrupted)
            ]
        )
        XCTAssertNil(applied.firstTextMs, "The stream events do not touch the two timings.")
        XCTAssertNil(applied.finalizationMs)
    }

    func testFirstTextIsAppliedOncePerSession() {
        var usage = TranscriptionUsage()
        usage.apply(.firstText(milliseconds: 840.5))
        XCTAssertEqual(usage.firstTextMs, 840.5)

        usage.apply(.firstText(milliseconds: 120))
        usage.apply(.firstText(milliseconds: 9_000))
        XCTAssertEqual(usage.firstTextMs, 840.5, "Later values, also from a later stream, are ignored.")
        XCTAssertNil(usage.finalizationMs)
        XCTAssertTrue(usage.streams.isEmpty)
    }

    func testFinalizationReplacesThePreviousValue() {
        var usage = TranscriptionUsage()
        usage.apply(.finalization(milliseconds: 310.2))
        XCTAssertEqual(usage.finalizationMs, 310.2)
        usage.apply(.finalization(milliseconds: 95))
        XCTAssertEqual(usage.finalizationMs, 95)
        usage.apply(.finalization(milliseconds: 0))
        XCTAssertEqual(usage.finalizationMs, 0)
        XCTAssertNil(usage.firstTextMs)
    }

    func testATimingThatCannotBeStoredIsIgnored() throws {
        var usage = TranscriptionUsage()
        for invalid in [Double.nan, Double.infinity, -Double.infinity, -1] {
            usage.apply(.firstText(milliseconds: invalid))
            usage.apply(.finalization(milliseconds: invalid))
        }
        XCTAssertNil(usage.firstTextMs)
        XCTAssertNil(usage.finalizationMs)

        // The first valid value is then the one that is kept, and the usage can always be saved.
        usage.apply(.firstText(milliseconds: 0))
        usage.apply(.finalization(milliseconds: 12.5))
        usage.apply(.finalization(milliseconds: .nan))
        XCTAssertEqual(usage.firstTextMs, 0)
        XCTAssertEqual(usage.finalizationMs, 12.5)
        XCTAssertEqual(try LiveCueJSON.decode(TranscriptionUsage.self, from: LiveCueJSON.encode(usage)), usage)
    }

    // MARK: - interruptOpenStreams()

    func testInterruptOpenStreamsClosesOnlyTheStreamsStillStreaming() {
        var usage = TranscriptionUsage()
        let completed = UUID()
        let interrupted = UUID()
        let open = UUID()
        let alsoOpen = UUID()
        usage.begin(completed)
        usage.update(completed, sentMs: 4_000, processedMs: 4_000, hasTranscript: true)
        usage.finish(completed, completed: true)
        usage.begin(interrupted)
        usage.finish(interrupted, completed: false)
        usage.begin(open)
        usage.update(open, sentMs: 8_000, processedMs: 6_500, hasTranscript: true)
        usage.begin(alsoOpen)
        XCTAssertFalse(usage.streams.allSatisfy { $0.state != SpeechStreamUsage.streaming })
        XCTAssertEqual(usage.estimatedSeconds, 4 + 0 + 8 + 0)

        usage.interruptOpenStreams()
        XCTAssertEqual(
            usage.streams.map(\.state),
            [SpeechStreamUsage.completed, SpeechStreamUsage.interrupted, SpeechStreamUsage.interrupted, SpeechStreamUsage.interrupted]
        )
        XCTAssertEqual(usage.streams.map(\.id), [completed, interrupted, open, alsoOpen])
        XCTAssertEqual(usage.streams[2].sentMs, 8_000)
        XCTAssertEqual(usage.streams[2].processedMs, 6_500)
        XCTAssertTrue(usage.isIncomplete)
        XCTAssertEqual(usage.estimatedSeconds, 4 + 0 + 6 + 0, "The open stream is now costed from its processed audio.")

        // The streams are closed for good.
        let closed = usage
        usage.update(open, sentMs: 99_000, processedMs: 99_000, hasTranscript: true)
        usage.finish(open, completed: true)
        usage.interruptOpenStreams()
        XCTAssertEqual(usage, closed)
    }

    func testInterruptOpenStreamsWithNothingOpenChangesNothing() {
        var empty = TranscriptionUsage()
        empty.interruptOpenStreams()
        XCTAssertEqual(empty, TranscriptionUsage())

        var finished = TranscriptionUsage()
        let stream = UUID()
        finished.begin(stream)
        finished.update(stream, sentMs: 2_000, processedMs: 2_000, hasTranscript: true)
        finished.finish(stream, completed: true)
        let before = finished
        finished.interruptOpenStreams()
        XCTAssertEqual(finished, before)
        XCTAssertFalse(finished.isIncomplete)
    }
}
