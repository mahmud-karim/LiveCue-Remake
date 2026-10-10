import Foundation

/// The input of the caption state machine.
public enum StreamTranscriptEvent: Equatable, Sendable {
    case speechStart(turn: Int?, processedMs: Double)
    case transcript(turn: Int?, text: String?, processedMs: Double)
    case speechComplete(turn: Int?, text: String?, processedMs: Double)
    /// speechEnd, audioProgress, streamComplete, unknown.
    case other
}
