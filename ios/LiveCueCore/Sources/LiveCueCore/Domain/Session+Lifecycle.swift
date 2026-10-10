import Foundation

extension Session {
    /// The first 48 characters of the first segment's text, or "Conversation" without segments.
    public func finalTitle() -> String {
        guard let first = segments.first else { return "Conversation" }
        return String(first.text.prefix(48))
    }

    /// Launch recovery: end time = `lastSaved`, title by `finalTitle()`, every stream still "streaming"
    /// becomes "interrupted". A session that already has an end time is returned unchanged.
    public func closedAfterInterruption(at lastSaved: Date) -> Session {
        guard endedAt == nil else { return self }
        var closed = self
        // Whole milliseconds, as the initialiser does, so the closed session round-trips through `LiveCueJSON`.
        closed.endedAt = LiveCueJSON.roundedToMilliseconds(lastSaved)
        closed.title = finalTitle()
        closed.transcriptionUsage?.interruptOpenStreams()
        return closed
    }

    /// Every segment's text joined by single spaces (Speech benchmark).
    public var spaceJoinedTranscript: String {
        segments.map(\.text).joined(separator: " ")
    }
}
