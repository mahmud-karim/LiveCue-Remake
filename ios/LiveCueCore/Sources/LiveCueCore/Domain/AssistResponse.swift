import Foundation

/// A finished answer.
public struct AssistResponse: Codable, Equatable, Sendable {
    public var execution: RelayExecution?
    public var detectedQuestion: String
    public var answer: String
    public var details: String
    public var memory: SessionMemory

    public init(detectedQuestion: String, answer: String, details: String, memory: SessionMemory) {
        self.execution = nil
        self.detectedQuestion = detectedQuestion
        self.answer = answer
        self.details = details
        self.memory = memory
    }
}

/// A finished end-of-session summary.
public struct SummaryResponse: Codable, Equatable, Sendable {
    public var title: String
    public var summary: String
    public var keyPoints: [String]
    public var actionItems: [String]

    public init(title: String, summary: String, keyPoints: [String], actionItems: [String]) {
        self.title = title
        self.summary = summary
        self.keyPoints = keyPoints
        self.actionItems = actionItems
    }
}
