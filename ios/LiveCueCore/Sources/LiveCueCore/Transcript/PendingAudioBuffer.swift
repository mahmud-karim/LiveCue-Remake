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
        /// Which state of the buffer this was taken from. A snapshot is used once.
        fileprivate let generation: Int
    }

    public struct TimedWord: Equatable, Sendable {
        public var text: String
        public var end: Double

        public init(text: String, end: Double) {
            self.text = text
            self.end = end
        }
    }

    /// The overlap kept from the last transcription, then everything captured since.
    private var samples: [Float] = []
    /// How many samples at the front were already transcribed: the overlap. The cursor is its end.
    private var transcribedSamples = 0
    /// Changes whenever a snapshot stops describing the buffer: at `complete` and at `reset`.
    private var generation = 0

    public init() {}

    public var sampleCount: Int {
        samples.count
    }

    public var hasUntranscribedAudio: Bool {
        samples.count > transcribedSamples
    }

    /// False, appending nothing, when the chunk would pass the cap.
    public mutating func append(_ chunk: [Float]) -> Bool {
        guard samples.count + chunk.count <= PendingAudioBuffer.capacitySamples else { return false }
        samples.append(contentsOf: chunk)
        return true
    }

    /// nil when there is nothing new.
    public func snapshot() -> Snapshot? {
        guard hasUntranscribedAudio else { return nil }
        return Snapshot(
            samples: samples,
            cursorSeconds: Double(transcribedSamples) / Double(PendingAudioBuffer.sampleRate),
            generation: generation
        )
    }

    /// Keeps the words whose end is after the snapshot's cursor, joins them with spaces and trims; then
    /// trims the buffer to the snapshot's last 2 seconds (keeping samples appended since) and moves the cursor.
    public mutating func complete(_ snapshot: Snapshot, words: [TimedWord]) -> String {
        // A snapshot from before a reset, or one already completed, no longer describes this buffer:
        // its words would be repeated or belong to audio that is gone.
        guard snapshot.generation == generation, snapshot.samples.count <= samples.count else { return "" }

        let text = words
            .filter { $0.end > snapshot.cursorSeconds }
            .map(\.text)
            .joined(separator: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)

        // The snapshot's audio sits at the front of the buffer; what was appended since follows it.
        let overlap = min(snapshot.samples.count, PendingAudioBuffer.overlapSamples)
        samples.removeFirst(snapshot.samples.count - overlap)
        transcribedSamples = overlap
        generation += 1
        return text
    }

    public mutating func reset() {
        samples = []
        transcribedSamples = 0
        generation += 1
    }
}
