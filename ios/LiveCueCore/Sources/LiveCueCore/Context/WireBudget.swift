import Foundation

/// Byte budgets and the fitting step of protocol 13.4.
///
/// "Character" is a Swift `Character`. Sizes are UTF-8 bytes of the raw string.
public enum WireBudget {
    /// Longest prefix of whole characters within the limit.
    public static func head(_ text: String, maxCharacters: Int) -> String {
        guard maxCharacters > 0 else { return "" }
        return String(text.prefix(maxCharacters))
    }

    /// Longest prefix of whole characters within the limit.
    public static func head(_ text: String, maxBytes: Int) -> String {
        guard maxBytes > 0 else { return "" }
        if text.utf8.count <= maxBytes {
            return text
        }
        var used = 0
        var end = text.startIndex
        while end < text.endIndex {
            let next = text.index(after: end)
            let size = text[end..<next].utf8.count
            if used + size > maxBytes {
                break
            }
            used += size
            end = next
        }
        return String(text[..<end])
    }

    /// Longest suffix of whole characters within the limit.
    public static func tail(_ text: String, maxBytes: Int) -> String {
        guard maxBytes > 0 else { return "" }
        if text.utf8.count <= maxBytes {
            return text
        }
        // Walks back from the end, so a short tail of a very long text costs only what it keeps.
        var used = 0
        var start = text.endIndex
        while start > text.startIndex {
            let previous = text.index(before: start)
            let size = text[previous..<start].utf8.count
            if used + size > maxBytes {
                break
            }
            used += size
            start = previous
        }
        return String(text[start...])
    }

    /// The body exactly as the relay client sends it.
    public static func encodedBody(_ request: AssistRequest) throws -> Data {
        try LiveCueJSON.encode(request)
    }

    /// The body exactly as the relay client sends it.
    public static func encodedBody(_ request: SummaryRequest) throws -> Data {
        try LiveCueJSON.encode(request)
    }

    /// Steps 2 and 3: returns the request unchanged when its encoded body is at most C-11 bytes; otherwise
    /// shrinks one member at a time in the order transcript, memory.summary, partialTranscript,
    /// memory.facts and memory.openQuestions (oldest items first), instruction. The result always fits.
    ///
    /// A member that is shrunk to nothing stays in the body as an empty string or an empty list, except
    /// `partialTranscript` and `instruction`, which are left out as step 1 leaves them out. When the body
    /// is too large even with all six members empty (which needs an assistant selection of more than
    /// 100,000 bytes) this throws `EncodingError.invalidValue`.
    public static func fitted(_ request: AssistRequest) throws -> AssistRequest {
        if try fits(request) {
            return request
        }
        var body = request

        let transcript = CharacterCuts(body.transcript)
        if try shrink(&body, count: transcript.count, keep: { $0.transcript = transcript.tail($1) }) {
            return body
        }
        let summary = CharacterCuts(body.memory.summary)
        if try shrink(&body, count: summary.count, keep: { $0.memory.summary = summary.tail($1) }) {
            return body
        }
        let partial = CharacterCuts(body.partialTranscript ?? "")
        if try shrink(&body, count: partial.count, keep: { $0.partialTranscript = $1 == 0 ? nil : partial.tail($1) }) {
            return body
        }
        let facts = body.memory.facts
        if try shrink(&body, count: facts.count, keep: { $0.memory.facts = Array(facts.suffix($1)) }) {
            return body
        }
        let openQuestions = body.memory.openQuestions
        if try shrink(&body, count: openQuestions.count, keep: { $0.memory.openQuestions = Array(openQuestions.suffix($1)) }) {
            return body
        }
        let instruction = CharacterCuts(body.instruction ?? "")
        if try shrink(&body, count: instruction.count, keep: { $0.instruction = $1 == 0 ? nil : instruction.head($1) }) {
            return body
        }
        throw cannotFit(request)
    }

    /// As above. Sets `transcriptTruncated` on a summary whose transcript it cut.
    public static func fitted(_ request: SummaryRequest) throws -> SummaryRequest {
        if try fits(request) {
            return request
        }
        var body = request

        let transcript = CharacterCuts(body.transcript)
        let cutTranscript: (inout SummaryRequest, Int) -> Void = { candidate, kept in
            // `shrink` only ever keeps less than the whole text, so every call here is a cut.
            candidate.transcript = transcript.tail(kept)
            candidate.transcriptTruncated = true
        }
        if try shrink(&body, count: transcript.count, keep: cutTranscript) {
            return body
        }
        let summary = CharacterCuts(body.memory.summary)
        if try shrink(&body, count: summary.count, keep: { $0.memory.summary = summary.tail($1) }) {
            return body
        }
        let facts = body.memory.facts
        if try shrink(&body, count: facts.count, keep: { $0.memory.facts = Array(facts.suffix($1)) }) {
            return body
        }
        let openQuestions = body.memory.openQuestions
        if try shrink(&body, count: openQuestions.count, keep: { $0.memory.openQuestions = Array(openQuestions.suffix($1)) }) {
            return body
        }
        throw cannotFit(request)
    }

    // MARK: - Fitting

    /// Step 2: the body as it would be sent is at most C-11 bytes.
    private static func fits<Body: Encodable>(_ body: Body) throws -> Bool {
        try LiveCueJSON.encode(body).count <= RelayConstants.maximumEncodedBodyBytes
    }

    /// Step 3 for one member of a body that does not fit.
    ///
    /// The member has `count` units (characters of a text, items of a list) and `keep` sets it to a given
    /// number of them. The member is cut to the largest number of units that makes the encoded body fit,
    /// found by binary search with the candidate body encoded each time. Returns true when the body fits
    /// afterwards. Returns false, with the member empty, when the body is too large even without it; the
    /// caller then moves to the next member.
    private static func shrink<Body: Encodable>(
        _ body: inout Body,
        count: Int,
        keep: (inout Body, Int) -> Void
    ) throws -> Bool {
        guard count > 0 else { return false }

        var emptied = body
        keep(&emptied, 0)
        guard try fits(emptied) else {
            body = emptied
            return false
        }

        // An encoded body never gets smaller when a member keeps more, so "fits" is true up to some
        // number of units and false after it. It is true for none of them and false for all of them.
        var fitting = 0
        var upper = count - 1
        while fitting < upper {
            let middle = fitting + (upper - fitting + 1) / 2
            var candidate = body
            keep(&candidate, middle)
            if try fits(candidate) {
                fitting = middle
            } else {
                upper = middle - 1
            }
        }
        keep(&body, fitting)
        return true
    }

    private static func cannotFit<Body>(_ request: Body) -> EncodingError {
        EncodingError.invalidValue(
            request,
            EncodingError.Context(
                codingPath: [],
                debugDescription: "The request body exceeds \(RelayConstants.maximumEncodedBodyBytes) bytes even with every text member empty."
            )
        )
    }
}

/// The character boundaries of one text, found once, so that each step of a binary search takes a tail or
/// a head by position instead of walking the text again.
private struct CharacterCuts {
    private let text: String
    /// The start of every character, then the end of the text.
    private let boundaries: [String.Index]

    init(_ text: String) {
        self.text = text
        var boundaries = Array(text.indices)
        boundaries.append(text.endIndex)
        self.boundaries = boundaries
    }

    /// The number of characters.
    var count: Int {
        boundaries.count - 1
    }

    /// The last `kept` characters. `kept` must be in `0...count`.
    func tail(_ kept: Int) -> String {
        String(text[boundaries[count - kept]...])
    }

    /// The first `kept` characters. `kept` must be in `0...count`.
    func head(_ kept: Int) -> String {
        String(text[..<boundaries[kept]])
    }
}
