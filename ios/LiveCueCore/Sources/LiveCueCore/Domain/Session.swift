import Foundation

/// The unit of history: one conversation with its transcript, answers, memory, notes and usage.
public struct Session: Codable, Equatable, Sendable, Identifiable {
    public let id: UUID
    public var title: String
    public var startedAt: Date
    public var endedAt: Date?
    public var segments: [TranscriptSegment]
    public var assistantTurns: [AssistantTurn]
    public var memory: SessionMemory
    public var notes: SessionNotes
    public var transcriptionUsage: TranscriptionUsage?

    public static let defaultTitle = "New conversation"

    public init(
        id: UUID = UUID(),
        title: String = Session.defaultTitle,
        startedAt: Date = Date(),
        endedAt: Date? = nil,
        segments: [TranscriptSegment] = [],
        assistantTurns: [AssistantTurn] = [],
        memory: SessionMemory = SessionMemory(),
        notes: SessionNotes = SessionNotes()
    ) {
        self.id = id
        self.title = title
        // Whole milliseconds, so the values round-trip exactly through `LiveCueJSON` (CORE-A12).
        self.startedAt = LiveCueJSON.roundedToMilliseconds(startedAt)
        self.endedAt = endedAt.map { LiveCueJSON.roundedToMilliseconds($0) }
        self.segments = segments
        self.assistantTurns = assistantTurns
        self.memory = memory
        self.notes = notes
        self.transcriptionUsage = nil
    }
}
