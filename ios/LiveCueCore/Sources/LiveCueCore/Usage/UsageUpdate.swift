import Foundation

/// A usage event sent by a transcriber.
public enum UsageUpdate: Equatable, Sendable {
    case begin(id: UUID)
    case update(id: UUID, sentMs: Double?, processedMs: Double?, hasTranscript: Bool)
    case finish(id: UUID, completed: Bool)
    /// Applied once per session.
    case firstText(milliseconds: Double)
    /// Replaces the previous value.
    case finalization(milliseconds: Double)
}
