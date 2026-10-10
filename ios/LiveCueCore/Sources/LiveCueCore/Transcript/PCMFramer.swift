import Foundation

/// Cuts a byte stream into fixed frames; the remainder is flushed once at the end.
public struct PCMFramer: Sendable {
    private let frameBytes: Int

    /// 3,840 for Meta, 2,560 for PC speech.
    public init(frameBytes: Int) {
        self.frameBytes = frameBytes
    }

    /// Zero or more full frames.
    public mutating func append(_ bytes: Data) -> [Data] {
        // STUB(WP-04)
        []
    }

    /// The remainder, or nil when empty. An odd trailing byte is dropped, so the frame is even and not empty.
    public mutating func flush() -> Data? {
        // STUB(WP-04)
        nil
    }
}
