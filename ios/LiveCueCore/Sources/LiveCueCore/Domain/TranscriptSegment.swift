import Foundation

/// A final piece of transcript. Every segment is final (CORE-C2): there is no `isFinal` member, and a
/// stored `isFinal` key is ignored on decode.
public struct TranscriptSegment: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public var text: String
    public var startSeconds: Double
    public var endSeconds: Double

    public init(id: UUID = UUID(), text: String, startSeconds: Double, endSeconds: Double) {
        self.id = id
        self.text = text
        self.startSeconds = startSeconds
        self.endSeconds = endSeconds
    }
}
