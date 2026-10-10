import Foundation

/// The body of an answer request. `assistant` is set just before sending.
public struct AssistRequest: Codable, Equatable, Sendable {
    public var assistant: AssistantConfiguration?
    public var requestId: UUID
    public var sessionId: UUID
    public var instruction: String?
    public var memory: SessionMemory
    public var transcript: String
    public var partialTranscript: String?

    public init(
        requestId: UUID = UUID(),
        sessionId: UUID,
        instruction: String? = nil,
        memory: SessionMemory,
        transcript: String,
        partialTranscript: String? = nil
    ) {
        self.assistant = nil
        self.requestId = requestId
        self.sessionId = sessionId
        self.instruction = instruction
        self.memory = memory
        self.transcript = transcript
        self.partialTranscript = partialTranscript
    }
}

/// The body of an end-of-session summary request.
public struct SummaryRequest: Codable, Equatable, Sendable {
    public var requestId: UUID
    public var sessionId: UUID
    public var assistant: AssistantConfiguration
    public var memory: SessionMemory
    public var transcript: String
    /// `true` when the transcript was cut, otherwise absent. Never `false`.
    public var transcriptTruncated: Bool?

    public init(
        requestId: UUID = UUID(),
        sessionId: UUID,
        assistant: AssistantConfiguration,
        memory: SessionMemory,
        transcript: String,
        transcriptTruncated: Bool? = nil
    ) {
        self.requestId = requestId
        self.sessionId = sessionId
        self.assistant = assistant
        self.memory = memory
        self.transcript = transcript
        self.transcriptTruncated = transcriptTruncated
    }
}
