import Foundation

/// A Meta event after the sanitiser: only the members the app uses.
public struct MetaEvent: Equatable, Sendable {
    /// "" for an event to ignore.
    public var type: String
    public var turnId: Int?
    public var audioProcessedMs: Double?
    /// The wire member `final`.
    public var isFinal: Bool?
    public var transcript: String?

    public init(
        type: String,
        turnId: Int? = nil,
        audioProcessedMs: Double? = nil,
        isFinal: Bool? = nil,
        transcript: String? = nil
    ) {
        self.type = type
        self.turnId = turnId
        self.audioProcessedMs = audioProcessedMs
        self.isFinal = isFinal
        self.transcript = transcript
    }

    public var isIgnored: Bool {
        type.isEmpty
    }

    /// The event as the caption state machine reads it. The processed position is 0 when absent.
    public var transcriptEvent: StreamTranscriptEvent {
        let processedMs = audioProcessedMs ?? 0
        switch type {
        case "speechStart":
            return .speechStart(turn: turnId, processedMs: processedMs)
        case "transcript":
            return .transcript(turn: turnId, text: transcript, processedMs: processedMs)
        case "speechComplete":
            return .speechComplete(turn: turnId, text: transcript, processedMs: processedMs)
        default:
            return .other
        }
    }
}
