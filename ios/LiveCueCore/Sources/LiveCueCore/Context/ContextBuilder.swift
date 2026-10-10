import Foundation

/// Builds the answer and summary requests from a session (protocol 13.4 step 1).
public enum ContextBuilder {
    /// Step 1 of protocol 13.4. `assistant` is left unset and `requestId` is fresh.
    /// Transcript: segments from two before the first one not covered by the memory marker, joined by "\n",
    /// tail of C-12. Partial: trimmed, tail of C-14, nil when empty. Instruction: trimmed, head of 4,000
    /// characters then head of 8,000 bytes, nil when empty. Memory: summary tail of C-16; each list keeps
    /// its last 16 items, each cut to its head of 256 bytes; the marker is passed through.
    public static func assistRequest(session: Session, partial: String?, instruction: String?) -> AssistRequest {
        let window = session.segments[windowStart(of: session)...]
        let transcript = window.map(\.text).joined(separator: "\n")

        var cutPartial: String?
        if let partial {
            cutPartial = nonEmpty(WireBudget.tail(trimmed(partial), maxBytes: RelayConstants.partialTranscriptBytes))
        }

        var cutInstruction: String?
        if let instruction {
            let byCharacters = WireBudget.head(trimmed(instruction), maxCharacters: RelayConstants.instructionCharacters)
            cutInstruction = nonEmpty(WireBudget.head(byCharacters, maxBytes: RelayConstants.instructionBytes))
        }

        return AssistRequest(
            sessionId: session.id,
            instruction: cutInstruction,
            memory: budgeted(session.memory),
            transcript: WireBudget.tail(transcript, maxBytes: RelayConstants.assistTranscriptBytes),
            partialTranscript: cutPartial
        )
    }

    /// False when the transcript is empty and there is no partial ("No speech was recognized yet.").
    public static func hasContent(_ request: AssistRequest) -> Bool {
        if !request.transcript.isEmpty {
            return true
        }
        guard let partial = request.partialTranscript else { return false }
        return !partial.isEmpty
    }

    /// Every segment joined by "\n", tail of C-13, `transcriptTruncated` true exactly when cut; memory cut
    /// as above. Returns nil for a session without segments.
    public static func summaryRequest(session: Session, assistant: AssistantConfiguration) -> SummaryRequest? {
        guard !session.segments.isEmpty else { return nil }
        let whole = session.segments.map(\.text).joined(separator: "\n")
        let transcript = WireBudget.tail(whole, maxBytes: RelayConstants.summaryTranscriptBytes)
        let wasCut = transcript.utf8.count < whole.utf8.count
        return SummaryRequest(
            sessionId: session.id,
            assistant: assistant,
            memory: budgeted(session.memory),
            transcript: transcript,
            transcriptTruncated: wasCut ? true : nil
        )
    }

    /// Same text, new `requestId`, the given assistant. The caller fits it again.
    public static func retry(_ previous: AssistRequest, assistant: AssistantConfiguration) -> AssistRequest {
        var request = previous
        request.requestId = UUID()
        request.assistant = assistant
        return request
    }

    // MARK: - Step 1 pieces

    /// The index of the first segment of the window: two before the first segment that the memory marker
    /// does not cover. Without a marker, or with one that names no segment, the window is every segment.
    private static func windowStart(of session: Session) -> Int {
        guard
            let marker = session.memory.throughSegmentId,
            let covered = session.segments.firstIndex(where: { $0.id == marker })
        else {
            return 0
        }
        let firstNew = covered + 1
        return max(0, firstNew - 2)
    }

    /// The memory inside its budgets (C-16 and C-17). The marker is passed through.
    private static func budgeted(_ memory: SessionMemory) -> SessionMemory {
        SessionMemory(
            summary: WireBudget.tail(memory.summary, maxBytes: RelayConstants.memorySummaryBytes),
            facts: budgeted(memory.facts),
            openQuestions: budgeted(memory.openQuestions),
            throughSegmentId: memory.throughSegmentId
        )
    }

    private static func budgeted(_ items: [String]) -> [String] {
        items.suffix(RelayConstants.memoryListItems).map { item in
            WireBudget.head(item, maxBytes: RelayConstants.memoryListItemBytes)
        }
    }

    private static func trimmed(_ text: String) -> String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func nonEmpty(_ text: String) -> String? {
        text.isEmpty ? nil : text
    }
}
