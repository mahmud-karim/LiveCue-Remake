import Foundation

/// Cuts a byte stream into fixed frames; the remainder is flushed once at the end.
public struct PCMFramer: Sendable {
    private let frameBytes: Int
    /// Bytes received and not yet part of a full frame. Always shorter than one frame.
    private var pending = Data()

    /// 3,840 for Meta, 2,560 for PC speech.
    public init(frameBytes: Int) {
        // A frame holds at least one byte, so the cutting loop always ends.
        self.frameBytes = max(1, frameBytes)
    }

    /// Zero or more full frames.
    public mutating func append(_ bytes: Data) -> [Data] {
        pending.append(bytes)
        guard pending.count >= frameBytes else { return [] }

        var frames: [Data] = []
        var start = pending.startIndex
        while pending.endIndex - start >= frameBytes {
            let end = start + frameBytes
            // `Data(_:)` copies the slice, so every frame starts at index 0.
            frames.append(Data(pending[start..<end]))
            start = end
        }
        pending = Data(pending[start...])
        return frames
    }

    /// The remainder, or nil when empty. An odd trailing byte is dropped, so the frame is even and not empty.
    public mutating func flush() -> Data? {
        let evenCount = pending.count - pending.count % 2
        let remainder = Data(pending.prefix(evenCount))
        pending = Data()
        return remainder.isEmpty ? nil : remainder
    }
}
