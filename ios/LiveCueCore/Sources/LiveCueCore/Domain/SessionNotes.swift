import Foundation

/// The notes a summary leaves on a finished session.
public struct SessionNotes: Codable, Equatable, Sendable {
    public var summary: String
    public var keyPoints: [String]
    public var actionItems: [String]

    public init(summary: String = "", keyPoints: [String] = [], actionItems: [String] = []) {
        self.summary = summary
        self.keyPoints = keyPoints
        self.actionItems = actionItems
    }
}
