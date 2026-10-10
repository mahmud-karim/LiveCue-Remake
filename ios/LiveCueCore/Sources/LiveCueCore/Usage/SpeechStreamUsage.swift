import Foundation

/// The usage record of one speech stream.
public struct SpeechStreamUsage: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public var sentMs: Double
    public var processedMs: Double?
    public var hasTranscript: Bool
    public var state: String

    public static let streaming = "streaming"
    public static let completed = "completed"
    public static let interrupted = "interrupted"

    public init(
        id: UUID,
        sentMs: Double = 0,
        processedMs: Double? = nil,
        hasTranscript: Bool = false,
        state: String = SpeechStreamUsage.streaming
    ) {
        self.id = id
        self.sentMs = sentMs
        self.processedMs = processedMs
        self.hasTranscript = hasTranscript
        self.state = state
    }
}
