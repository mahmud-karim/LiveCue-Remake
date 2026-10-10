import Foundation

/// Token counts and cost of one answer, when the service reports them.
public struct AssistantUsage: Codable, Equatable, Sendable {
    public var generationId: String?
    public var promptTokens: Int?
    public var completionTokens: Int?
    public var costCredits: Double?

    public init(
        generationId: String? = nil,
        promptTokens: Int? = nil,
        completionTokens: Int? = nil,
        costCredits: Double? = nil
    ) {
        self.generationId = generationId
        self.promptTokens = promptTokens
        self.completionTokens = completionTokens
        self.costCredits = costCredits
    }
}

/// What actually ran an answer, and how long each part took.
public struct RelayExecution: Codable, Equatable, Sendable {
    public var model: String
    public var reasoningEffort: String
    public var codexMs: Double
    public var relayTotalMs: Double
    public var requestReadMs: Double
    public var relayOverheadMs: Double
    public var usage: AssistantUsage?

    public init(
        model: String,
        reasoningEffort: String,
        codexMs: Double,
        relayTotalMs: Double,
        requestReadMs: Double,
        relayOverheadMs: Double,
        usage: AssistantUsage? = nil
    ) {
        self.model = model
        self.reasoningEffort = reasoningEffort
        self.codexMs = codexMs
        self.relayTotalMs = relayTotalMs
        self.requestReadMs = requestReadMs
        self.relayOverheadMs = relayOverheadMs
        self.usage = usage
    }
}
