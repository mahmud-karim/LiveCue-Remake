import Foundation

/// Byte budgets and the fitting step of protocol 13.4.
///
/// "Character" is a Swift `Character`. Sizes are UTF-8 bytes of the raw string.
public enum WireBudget {
    /// Longest prefix of whole characters within the limit.
    public static func head(_ text: String, maxCharacters: Int) -> String {
        // STUB(WP-02)
        text
    }

    /// Longest prefix of whole characters within the limit.
    public static func head(_ text: String, maxBytes: Int) -> String {
        // STUB(WP-02)
        text
    }

    /// Longest suffix of whole characters within the limit.
    public static func tail(_ text: String, maxBytes: Int) -> String {
        // STUB(WP-02)
        text
    }

    /// The body exactly as the relay client sends it.
    public static func encodedBody(_ request: AssistRequest) throws -> Data {
        // STUB(WP-02)
        try LiveCueJSON.encode(request)
    }

    /// The body exactly as the relay client sends it.
    public static func encodedBody(_ request: SummaryRequest) throws -> Data {
        // STUB(WP-02)
        try LiveCueJSON.encode(request)
    }

    /// Steps 2 and 3: returns the request unchanged when its encoded body is at most C-11 bytes; otherwise
    /// shrinks one member at a time in the order transcript, memory.summary, partialTranscript,
    /// memory.facts and memory.openQuestions (oldest items first), instruction. The result always fits.
    public static func fitted(_ request: AssistRequest) throws -> AssistRequest {
        // STUB(WP-02)
        request
    }

    /// As above. Sets `transcriptTruncated` on a summary whose transcript it cut.
    public static func fitted(_ request: SummaryRequest) throws -> SummaryRequest {
        // STUB(WP-02)
        request
    }
}
