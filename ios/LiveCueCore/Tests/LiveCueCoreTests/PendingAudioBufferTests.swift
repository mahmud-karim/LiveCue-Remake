import Foundation
import LiveCueCore
import XCTest

/// Voz pending audio (spec 01 section 3.3): the 30-minute cap, the word cursor and the 2-second overlap.
final class PendingAudioBufferTests: XCTestCase {
    private typealias Word = PendingAudioBuffer.TimedWord

    private func seconds(_ count: Double, of value: Float = 0) -> [Float] {
        [Float](repeating: value, count: Int(count * Double(PendingAudioBuffer.sampleRate)))
    }

    // MARK: - Constants

    func testTheConstantsAreThoseOfTheSpec() {
        XCTAssertEqual(PendingAudioBuffer.sampleRate, 16_000)
        XCTAssertEqual(PendingAudioBuffer.capacitySamples, 16_000 * 60 * 30)
        XCTAssertEqual(PendingAudioBuffer.overlapSamples, 16_000 * 2)
    }

    func testANewBufferIsEmpty() {
        let buffer = PendingAudioBuffer()
        XCTAssertEqual(buffer.sampleCount, 0)
        XCTAssertFalse(buffer.hasUntranscribedAudio)
        XCTAssertNil(buffer.snapshot())
    }

    // MARK: - The 30-minute cap

    func testTheCapRefusesTheChunkThatWouldPassIt() {
        var buffer = PendingAudioBuffer()
        // 30 minutes in 100-second chunks.
        let chunk = seconds(100)
        for _ in 0..<18 {
            XCTAssertTrue(buffer.append(chunk))
        }
        XCTAssertEqual(buffer.sampleCount, PendingAudioBuffer.capacitySamples)

        // Full: one more sample is refused and nothing of it is kept.
        XCTAssertFalse(buffer.append([0.5]))
        XCTAssertFalse(buffer.append(chunk))
        XCTAssertEqual(buffer.sampleCount, PendingAudioBuffer.capacitySamples)

        // A chunk that adds nothing does not pass the cap.
        XCTAssertTrue(buffer.append([]))
        XCTAssertTrue(buffer.hasUntranscribedAudio)
    }

    func testAChunkThatWouldPassTheCapIsNotAppendedInPart() {
        var buffer = PendingAudioBuffer()
        let chunk = seconds(100)
        for _ in 0..<17 {
            XCTAssertTrue(buffer.append(chunk))
        }
        let before = buffer.sampleCount
        XCTAssertEqual(before, PendingAudioBuffer.capacitySamples - chunk.count)

        // 100 seconds and one sample would pass the cap: refused whole.
        XCTAssertFalse(buffer.append(chunk + [0]))
        XCTAssertEqual(buffer.sampleCount, before)

        // Exactly up to the cap is accepted.
        XCTAssertTrue(buffer.append(chunk))
        XCTAssertEqual(buffer.sampleCount, PendingAudioBuffer.capacitySamples)
    }

    func testTheOverlapCountsTowardsTheCap() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(10)))
        let snapshot = try XCTUnwrap(buffer.snapshot())
        _ = buffer.complete(snapshot, words: [])
        XCTAssertEqual(buffer.sampleCount, PendingAudioBuffer.overlapSamples)

        // 2 seconds are held, so 29 minutes 58 seconds fit and one sample more does not.
        let room = PendingAudioBuffer.capacitySamples - PendingAudioBuffer.overlapSamples
        XCTAssertFalse(buffer.append([Float](repeating: 0, count: room + 1)))
        XCTAssertTrue(buffer.append([Float](repeating: 0, count: room)))
        XCTAssertEqual(buffer.sampleCount, PendingAudioBuffer.capacitySamples)
    }

    // MARK: - The word cursor

    func testTheFirstBatchKeepsEveryWordAndJoinsThemWithSpaces() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(5)))
        XCTAssertTrue(buffer.hasUntranscribedAudio)

        let snapshot = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(snapshot.samples.count, 80_000)
        XCTAssertEqual(snapshot.cursorSeconds, 0)

        let text = buffer.complete(snapshot, words: [
            Word(text: "Hello", end: 0.4),
            Word(text: "there", end: 0.9),
            Word(text: "everyone.", end: 4.8)
        ])
        XCTAssertEqual(text, "Hello there everyone.")
    }

    func testOnlyWordsEndingAfterTheCursorAreKept() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(5)))
        let first = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(buffer.complete(first, words: [Word(text: "First", end: 1), Word(text: "batch.", end: 4.9)]), "First batch.")

        // The buffer now starts with the last 2 seconds of that batch; the cursor is their end.
        XCTAssertTrue(buffer.append(seconds(3)))
        let second = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(second.samples.count, 80_000)
        XCTAssertEqual(second.cursorSeconds, 2)

        // The recogniser hears the overlap again. Words that end at or before the cursor are not new.
        let text = buffer.complete(second, words: [
            Word(text: "batch.", end: 1.9),
            Word(text: "edge", end: 2.0),
            Word(text: "Second", end: 2.01),
            Word(text: "batch.", end: 4.5)
        ])
        XCTAssertEqual(text, "Second batch.")
    }

    func testTheTextIsTrimmedAndMayBeEmpty() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(1)))
        let snapshot = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(buffer.complete(snapshot, words: [Word(text: " Hello", end: 0.3), Word(text: "world \n", end: 0.8)]), "Hello world")

        // Silence: no word at all. The buffer is trimmed all the same.
        XCTAssertTrue(buffer.append(seconds(4)))
        let silence = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(buffer.complete(silence, words: []), "")
        XCTAssertEqual(buffer.sampleCount, PendingAudioBuffer.overlapSamples)
        XCTAssertFalse(buffer.hasUntranscribedAudio)
    }

    // MARK: - The 2-second overlap

    func testTheBufferIsTrimmedToItsLastTwoSeconds() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(3, of: 1)))
        XCTAssertTrue(buffer.append(seconds(2, of: 2)))
        let snapshot = try XCTUnwrap(buffer.snapshot())
        _ = buffer.complete(snapshot, words: [Word(text: "Words", end: 1)])

        XCTAssertEqual(buffer.sampleCount, 32_000)
        // Nothing new: there is no batch to run until more audio arrives.
        XCTAssertFalse(buffer.hasUntranscribedAudio)
        XCTAssertNil(buffer.snapshot())

        // What was kept is the end of the audio: the 2 seconds of value 2.
        XCTAssertTrue(buffer.append([3]))
        let next = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(next.samples.count, 32_001)
        XCTAssertEqual(next.samples.first, 2)
        XCTAssertEqual(next.samples[31_999], 2)
        XCTAssertEqual(next.samples.last, 3)
        XCTAssertEqual(next.cursorSeconds, 2)
    }

    func testAudioAppendedDuringTranscriptionIsKept() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(5, of: 1)))
        let snapshot = try XCTUnwrap(buffer.snapshot())

        // Capture goes on while the recogniser works on the snapshot.
        XCTAssertTrue(buffer.append(seconds(1, of: 2)))
        XCTAssertEqual(snapshot.samples.count, 80_000, "The snapshot does not grow.")

        XCTAssertEqual(buffer.complete(snapshot, words: [Word(text: "Spoken", end: 4)]), "Spoken")

        // The snapshot's last 2 seconds, then the second that arrived meanwhile.
        XCTAssertEqual(buffer.sampleCount, 48_000)
        XCTAssertTrue(buffer.hasUntranscribedAudio)
        let next = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(next.cursorSeconds, 2)
        XCTAssertEqual(next.samples.count, 48_000)
        XCTAssertEqual(next.samples[0], 1)
        XCTAssertEqual(next.samples[31_999], 1)
        XCTAssertEqual(next.samples[32_000], 2)
        XCTAssertEqual(next.samples[47_999], 2)

        // Only what was said in that second is new.
        XCTAssertEqual(buffer.complete(next, words: [Word(text: "Spoken", end: 1.5), Word(text: "since", end: 2.6)]), "since")
    }

    func testABatchShorterThanTheOverlapIsKeptWhole() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(0.5, of: 1)))
        let snapshot = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(buffer.complete(snapshot, words: [Word(text: "Hi", end: 0.4)]), "Hi")

        // Half a second is less than the overlap: all of it stays, and all of it is behind the cursor.
        XCTAssertEqual(buffer.sampleCount, 8_000)
        XCTAssertFalse(buffer.hasUntranscribedAudio)

        XCTAssertTrue(buffer.append(seconds(1, of: 2)))
        let next = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(next.cursorSeconds, 0.5)
        XCTAssertEqual(buffer.complete(next, words: [Word(text: "Hi", end: 0.4), Word(text: "again", end: 1.2)]), "again")
        XCTAssertEqual(buffer.sampleCount, 24_000)
    }

    // MARK: - Reset and stale snapshots

    func testResetEmptiesTheBufferAndTheCursor() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(5)))
        let snapshot = try XCTUnwrap(buffer.snapshot())
        _ = buffer.complete(snapshot, words: [])
        XCTAssertTrue(buffer.append(seconds(1)))

        buffer.reset()
        XCTAssertEqual(buffer.sampleCount, 0)
        XCTAssertFalse(buffer.hasUntranscribedAudio)
        XCTAssertNil(buffer.snapshot())

        XCTAssertTrue(buffer.append(seconds(1)))
        let fresh = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(fresh.cursorSeconds, 0)
        XCTAssertEqual(fresh.samples.count, 16_000)
    }

    func testASnapshotFromBeforeAResetChangesNothing() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(5)))
        let stale = try XCTUnwrap(buffer.snapshot())

        // Capture was restarted while the recogniser worked: its result belongs to audio that is gone.
        buffer.reset()
        XCTAssertTrue(buffer.append(seconds(6)))

        XCTAssertEqual(buffer.complete(stale, words: [Word(text: "Old", end: 1)]), "")
        XCTAssertEqual(buffer.sampleCount, 96_000)
        XCTAssertEqual(buffer.snapshot()?.cursorSeconds, 0)
    }

    func testASnapshotIsCompletedOnce() throws {
        var buffer = PendingAudioBuffer()
        XCTAssertTrue(buffer.append(seconds(5)))
        let snapshot = try XCTUnwrap(buffer.snapshot())
        XCTAssertEqual(buffer.complete(snapshot, words: [Word(text: "Once", end: 1)]), "Once")
        XCTAssertTrue(buffer.append(seconds(1)))

        // The same snapshot again would repeat its words and trim away the new second.
        XCTAssertEqual(buffer.complete(snapshot, words: [Word(text: "Once", end: 1)]), "")
        XCTAssertEqual(buffer.sampleCount, 48_000)
        XCTAssertTrue(buffer.hasUntranscribedAudio)
    }
}
