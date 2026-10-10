import Foundation

/// One saved answer.
public struct AssistantTurn: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public var createdAt: Date
    public var request: String?
    public var detectedQuestion: String
    public var answer: String
    public var details: String
    public var performance: AssistPerformance?

    /// The detected question of every OpenRouter answer. The UI hides it (CORE-B5).
    public static let latestConversation = "Latest conversation"

    public init(
        id: UUID = UUID(),
        createdAt: Date = Date(),
        request: String? = nil,
        detectedQuestion: String,
        answer: String,
        details: String,
        performance: AssistPerformance? = nil
    ) {
        self.id = id
        // Whole milliseconds, so the value round-trips exactly through `LiveCueJSON` (CORE-A12).
        self.createdAt = LiveCueJSON.roundedToMilliseconds(createdAt)
        self.request = request
        self.detectedQuestion = detectedQuestion
        self.answer = answer
        self.details = details
        self.performance = performance
    }
}
