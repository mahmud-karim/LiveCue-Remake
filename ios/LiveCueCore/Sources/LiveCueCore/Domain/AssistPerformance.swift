import Foundation

/// The timing record saved with each answer.
public struct AssistPerformance: Codable, Equatable, Sendable {
    public var configuration: AssistantConfiguration
    public var speechModel: String
    public var reusedText: Bool
    public var transcriptionMs: Double
    public var contextMs: Double
    public var roundTripMs: Double
    public var totalMs: Double
    public var lastLiveChunkMs: Double?
    public var transcriptCharacters: Int
    public var execution: RelayExecution?

    public init(
        configuration: AssistantConfiguration,
        speechModel: String,
        reusedText: Bool,
        transcriptionMs: Double,
        contextMs: Double,
        roundTripMs: Double,
        totalMs: Double,
        lastLiveChunkMs: Double? = nil,
        transcriptCharacters: Int,
        execution: RelayExecution? = nil
    ) {
        self.configuration = configuration
        self.speechModel = speechModel
        self.reusedText = reusedText
        self.transcriptionMs = transcriptionMs
        self.contextMs = contextMs
        self.roundTripMs = roundTripMs
        self.totalMs = totalMs
        self.lastLiveChunkMs = lastLiveChunkMs
        self.transcriptCharacters = transcriptCharacters
        self.execution = execution
    }

    /// max(0, roundTripMs - execution.relayTotalMs); nil without an execution record.
    public var transportEstimateMs: Double? {
        guard let execution else { return nil }
        return max(0, roundTripMs - execution.relayTotalMs)
    }
}
