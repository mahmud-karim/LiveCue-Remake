import Foundation

/// Voz pending audio (spec 01 section 3.3): 16 kHz mono samples, 30-minute cap, 2-second overlap.
public struct PendingAudioBuffer: Sendable {
    public static let sampleRate = 16_000
    /// 30 minutes.
    public static let capacitySamples = 28_800_000
    /// 2 seconds.
    public static let overlapSamples = 32_000

    public struct Snapshot: Sendable {
        public let samples: [Float]
        /// Words ending at or before this are not new.
        public let cursorSeconds: Double
    }

    public struct TimedWord: Equatable, Sendable {
        public var text: String
        public var end: Double

        public init(text: String, end: Double) {
            self.text = text
            self.end = end
        }
    }

    public init() {}

    public var sampleCount: Int {
        // STUB(WP-04)
        0
    }

    public var hasUntranscribedAudio: Bool {
        // STUB(WP-04)
        false
    }

    /// False, appending nothing, when the chunk would pass the cap.
    public mutating func append(_ chunk: [Float]) -> Bool {
        // STUB(WP-04)
        true
    }

    /// nil when there is nothing new.
    public func snapshot() -> Snapshot? {
        // STUB(WP-04)
        nil
    }

    /// Keeps the words whose end is after the snapshot's cursor, joins them with spaces and trims; then
    /// trims the buffer to the snapshot's last 2 seconds (keeping samples appended since) and moves the cursor.
    public mutating func complete(_ snapshot: Snapshot, words: [TimedWord]) -> String {
        // STUB(WP-04)
        ""
    }

    public mutating func reset() {
        // STUB(WP-04)
    }
}
