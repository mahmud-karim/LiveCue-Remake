import Foundation

/// Builds the answer and summary requests from a session (protocol 13.4 step 1).
public enum ContextBuilder {
    /// Step 1 of protocol 13.4. `assistant` is left unset and `requestId` is fresh.
    /// Transcript: segments from two before the first one not covered by the memory marker, joined by "\n",
    /// tail of C-12. Partial: trimmed, tail of C-14, nil when empty. Instruction: trimmed, head of 4,000
    /// characters then head of 8,000 bytes, nil when empty. Memory: summary tail of C-16; each list keeps
    /// its last 16 items, each cut to its head of 256 bytes; the marker is passed through.
    public static func assistRequest(session: Session, partial: String?, instruction: String?) -> AssistRequest {
        // STUB(WP-02)
        AssistRequest(sessionId: session.id, memory: session.memory, transcript: "")
    }

    /// False when the transcript is empty and there is no partial ("No speech was recognized yet.").
    public static func hasContent(_ request: AssistRequest) -> Bool {
        // STUB(WP-02)
        false
    }

    /// Every segment joined by "\n", tail of C-13, `transcriptTruncated` true exactly when cut; memory cut
    /// as above. Returns nil for a session without segments.
    public static func summaryRequest(session: Session, assistant: AssistantConfiguration) -> SummaryRequest? {
        // STUB(WP-02)
        nil
    }

    /// Same text, new `requestId`, the given assistant. The caller fits it again.
    public static func retry(_ previous: AssistRequest, assistant: AssistantConfiguration) -> AssistRequest {
        // STUB(WP-02)
        previous
    }
}
