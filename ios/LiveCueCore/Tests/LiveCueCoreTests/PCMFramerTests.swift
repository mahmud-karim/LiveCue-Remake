import Foundation
import LiveCueCore
import XCTest

/// Fixed frames and one final shorter, non-empty, even frame (the framing half of P2-216).
final class PCMFramerTests: XCTestCase {
    /// Bytes that differ from their neighbours, so a frame cut in the wrong place is noticed.
    private func pattern(_ count: Int, from start: Int = 0) -> Data {
        Data((start..<(start + count)).map { UInt8(truncatingIfNeeded: $0 &* 7 &+ $0 / 256) })
    }

    // MARK: - Full frames

    func testFullFramesOfTheGivenSize() {
        var framer = PCMFramer(frameBytes: 2_560)
        let audio = pattern(6_000)

        let frames = framer.append(audio)
        XCTAssertEqual(frames.map(\.count), [2_560, 2_560])
        XCTAssertEqual(frames[0], audio.subdata(in: 0..<2_560))
        XCTAssertEqual(frames[1], audio.subdata(in: 2_560..<5_120))

        // What is left is shorter than a frame and waits for more audio, or for the flush.
        XCTAssertEqual(framer.append(Data()), [])
        let remainder = framer.flush()
        XCTAssertEqual(remainder, audio.subdata(in: 5_120..<6_000))
    }

    func testAnExactMultipleLeavesNothingToFlush() {
        var framer = PCMFramer(frameBytes: 2_560)
        XCTAssertEqual(framer.append(pattern(7_680)).map(\.count), [2_560, 2_560, 2_560])
        XCTAssertNil(framer.flush())
    }

    func testSmallChunksAccumulateIntoFrames() {
        var framer = PCMFramer(frameBytes: 2_560)
        let audio = pattern(3_000)

        XCTAssertEqual(framer.append(audio.subdata(in: 0..<1_000)), [])
        XCTAssertEqual(framer.append(audio.subdata(in: 1_000..<2_000)), [])
        let frames = framer.append(audio.subdata(in: 2_000..<3_000))
        XCTAssertEqual(frames, [audio.subdata(in: 0..<2_560)])
        XCTAssertEqual(framer.flush(), audio.subdata(in: 2_560..<3_000))
    }

    func testTheFramesAreTheInputInOrderWhateverTheChunkSizes() {
        // 4,096 bytes is what a 2,048-frame tap buffer gives at 16-bit mono; the other sizes are odd on purpose.
        let audio = pattern(40_001)
        for chunkSize in [1, 7, 333, 2_559, 2_560, 2_561, 4_096, 9_999, 40_001] {
            var framer = PCMFramer(frameBytes: 2_560)
            var sent = Data()
            var offset = 0
            while offset < audio.count {
                let end = min(offset + chunkSize, audio.count)
                for frame in framer.append(audio.subdata(in: offset..<end)) {
                    XCTAssertEqual(frame.count, 2_560, "chunk size \(chunkSize)")
                    sent.append(frame)
                }
                offset = end
            }
            XCTAssertEqual(sent.count, 38_400, "chunk size \(chunkSize)")

            let remainder = framer.flush()
            // 1,601 bytes are left: the odd last byte is dropped.
            XCTAssertEqual(remainder?.count, 1_600, "chunk size \(chunkSize)")
            sent.append(remainder ?? Data())
            XCTAssertEqual(sent, audio.subdata(in: 0..<40_000), "chunk size \(chunkSize)")
        }
    }

    func testTheMetaFrameSize() {
        var framer = PCMFramer(frameBytes: MetaRealtimeAPI.frameBytes)
        let frames = framer.append(pattern(10_000))
        XCTAssertEqual(frames.map(\.count), [3_840, 3_840])
        XCTAssertEqual(framer.flush()?.count, 2_320)
    }

    // MARK: - The final frame

    func testTheFinalFrameIsShorterNotEmptyAndEven() {
        var framer = PCMFramer(frameBytes: 2_560)
        let audio = pattern(2_560 + 881)

        XCTAssertEqual(framer.append(audio).count, 1)
        let remainder = framer.flush()
        XCTAssertEqual(remainder, audio.subdata(in: 2_560..<(2_560 + 880)), "The odd trailing byte is dropped.")
        XCTAssertEqual((remainder?.count ?? 1) % 2, 0)
    }

    func testNothingIsFlushedWhenNothingOrOnlyOneByteIsLeft() {
        var empty = PCMFramer(frameBytes: 2_560)
        XCTAssertNil(empty.flush())

        // One odd byte cannot make a frame: C-71 forbids an empty frame and an odd one.
        var single = PCMFramer(frameBytes: 2_560)
        XCTAssertEqual(single.append(Data([42])), [])
        XCTAssertNil(single.flush())
    }

    func testTheRemainderIsFlushedOnce() {
        var framer = PCMFramer(frameBytes: 2_560)
        _ = framer.append(pattern(100))
        XCTAssertEqual(framer.flush()?.count, 100)
        XCTAssertNil(framer.flush())

        // After a flush the framer starts again from nothing.
        let next = pattern(2_600, from: 500)
        XCTAssertEqual(framer.append(next), [next.subdata(in: 0..<2_560)])
        XCTAssertEqual(framer.flush(), next.subdata(in: 2_560..<2_600))
    }

    // MARK: - Data values

    func testASliceOfDataIsFramedFromItsOwnStart() {
        // A slice keeps the indices of the data it was cut from.
        let whole = pattern(6_000)
        let slice = whole[1_000..<5_000]
        XCTAssertEqual(slice.startIndex, 1_000)

        var framer = PCMFramer(frameBytes: 2_560)
        let frames = framer.append(slice)
        XCTAssertEqual(frames, [whole.subdata(in: 1_000..<3_560)])
        XCTAssertEqual(framer.flush(), whole.subdata(in: 3_560..<5_000))
    }

    func testEveryFrameIsItsOwnDataStartingAtZero() {
        var framer = PCMFramer(frameBytes: 4)
        var frames = framer.append(Data([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]))
        frames.append(framer.flush() ?? Data())

        XCTAssertEqual(frames, [Data([1, 2, 3, 4]), Data([5, 6, 7, 8]), Data([9, 10])])
        for frame in frames {
            XCTAssertEqual(frame.startIndex, 0)
            XCTAssertEqual(frame[0], frame.first)
        }
    }
}
