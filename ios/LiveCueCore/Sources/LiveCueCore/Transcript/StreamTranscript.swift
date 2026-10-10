import Foundation

/// The caption merge state machine: spec 02 section 6 with CORE-A7 and CORE-A8. A fresh value is made for
/// every stream.
public struct StreamTranscript: Sendable {
    public init() {}

    /// Non-empty partials of unfinished turns, ascending turn number, joined by one space.
    public var partialText: String {
        // STUB(WP-04)
        ""
    }

    /// `transcript` updates the turn its `turn` names when that turn is open (started and not completed),
    /// otherwise the active turn when it is open, otherwise nothing. `speechComplete` for a turn not yet
    /// completed marks it completed and returns a segment when the trimmed text is not empty
    /// (start = the turn's recorded start or 0, end = processedMs / 1000).
    public mutating func apply(_ event: StreamTranscriptEvent) -> TranscriptSegment? {
        // STUB(WP-04)
        nil
    }
}
