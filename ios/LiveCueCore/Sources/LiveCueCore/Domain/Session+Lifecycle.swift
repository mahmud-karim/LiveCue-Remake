import Foundation

extension Session {
    /// The first 48 characters of the first segment's text, or "Conversation" without segments.
    public func finalTitle() -> String {
        // STUB(WP-02)
        "Conversation"
    }

    /// Launch recovery: end time = `lastSaved`, title by `finalTitle()`, every stream still "streaming"
    /// becomes "interrupted". A session that already has an end time is returned unchanged.
    public func closedAfterInterruption(at lastSaved: Date) -> Session {
        // STUB(WP-02)
        self
    }

    /// Every segment's text joined by single spaces (Speech benchmark).
    public var spaceJoinedTranscript: String {
        // STUB(WP-02)
        ""
    }
}
