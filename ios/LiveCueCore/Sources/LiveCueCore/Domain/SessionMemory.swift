import Foundation

/// The rolling memory of a session and the marker of the last segment it covers.
public struct SessionMemory: Codable, Equatable, Sendable {
    public var summary: String
    public var facts: [String]
    public var openQuestions: [String]
    /// A string that is not a UUID fails decoding of the whole value; a relay reply is therefore never
    /// decoded directly (see `RelayClient.assist`).
    public var throughSegmentId: UUID?

    public init(
        summary: String = "",
        facts: [String] = [],
        openQuestions: [String] = [],
        throughSegmentId: UUID? = nil
    ) {
        self.summary = summary
        self.facts = facts
        self.openQuestions = openQuestions
        self.throughSegmentId = throughSegmentId
    }
}
