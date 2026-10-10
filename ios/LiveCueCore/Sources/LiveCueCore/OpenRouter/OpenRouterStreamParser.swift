import Foundation

// Internal; reached by tests with `@testable import LiveCueCore`.

struct OpenRouterStreamResult: Equatable, Sendable {
    var id: String?
    var model: String?
    var text: String
    var usage: AssistantUsage?
}

/// The SSE parser of a streamed answer, and the whole-body form (spec 02 sections 4.5 and 4.6).
///
/// An answer exists only when the stream reached its terminator and a finish reason of "stop" was seen.
/// A stream that broke off, a refusal, a tool call and a cut-off answer are never a result; the text
/// gathered so far stays readable for a preview.
struct OpenRouterStreamParser: Sendable {
    /// The data of one event may be at most 128 KiB.
    static let maximumEventBytes = 131_072
    /// The answer may be at most 1 MiB of UTF-8.
    static let maximumAnswerBytes = 1_048_576

    /// Readable after a failure, for the preview.
    private(set) var text: String
    private(set) var done: Bool

    /// The `data:` lines of the event being assembled, and their size.
    private var pendingData: [String]
    private var pendingBytes: Int
    private var latestID: String?
    private var latestModel: String?
    private var finishReason: String?
    private var latestUsage: AssistantUsage?

    init() {
        text = ""
        done = false
        pendingData = []
        pendingBytes = 0
        latestID = nil
        latestModel = nil
        finishReason = nil
        latestUsage = nil
    }

    /// One line without its terminator. True when the answer text grew. Throws `OpenRouterError`.
    mutating func consumeLine(_ line: String) throws -> Bool {
        if done {
            return false
        }

        if line.isEmpty {
            // A blank line dispatches the event assembled so far.
            if pendingData.isEmpty {
                return false
            }
            let event = pendingData.joined(separator: "\n")
            pendingData = []
            pendingBytes = 0
            // Exactly this text and nothing around it (CORE-A13).
            if event == "[DONE]" {
                done = true
                return false
            }
            return try consumeEvent(JSONValue.parse(event), messageKey: "delta")
        }

        // Bytes, not characters: a field name is ASCII, and what follows it must not decide the match.
        let field = Array("data:".utf8)
        let bytes = line.utf8
        guard bytes.starts(with: field) else {
            // A comment (":"), another field ("event:", "id:", "retry:") or anything unknown.
            return false
        }
        var value = bytes.dropFirst(field.count)
        if value.first == UInt8(ascii: " ") {
            value = value.dropFirst()
        }
        pendingBytes += value.count
        if pendingBytes > OpenRouterStreamParser.maximumEventBytes {
            throw OpenRouterError.invalidResponse
        }
        pendingData.append(String(decoding: value, as: UTF8.self))
        return false
    }

    /// Succeeds only when done is set, the finish reason is "stop" and the trimmed text is not empty.
    func result() throws -> OpenRouterStreamResult {
        guard done,
              finishReason == "stop",
              !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        else {
            throw OpenRouterError.invalidResponse
        }
        var usage = latestUsage
        usage?.generationId = latestID
        return OpenRouterStreamResult(id: latestID, model: latestModel, text: text, usage: usage)
    }

    /// The whole-body form: one envelope read as one event, with the terminator taken as seen.
    static func completeJSON(_ data: Data) throws -> OpenRouterStreamResult {
        var parser = OpenRouterStreamParser()
        _ = try parser.consumeEvent(JSONValue.parse(data), messageKey: "message")
        parser.done = true
        return try parser.result()
    }

    // MARK: - One completion event (spec 02 section 4.6)

    /// `messageKey` is "delta" for an SSE event and "message" for the whole-body envelope.
    /// Returns whether the event added text.
    private mutating func consumeEvent(_ event: JSONValue?, messageKey: String) throws -> Bool {
        guard let members = event?.objectValue else {
            throw OpenRouterError.invalidResponse
        }

        // 1. An `error` member of any value, `null` included. The provider's message is discarded.
        if members["error"] != nil {
            throw OpenRouterError.unavailable
        }

        // 2. Later values replace earlier ones.
        if let id = members["id"]?.stringValue {
            latestID = id
        }
        if let model = members["model"]?.stringValue {
            latestModel = model
        }

        // 3. Usage replaces what was stored.
        if let usage = members["usage"], usage.objectValue != nil {
            latestUsage = AssistantUsage(
                promptTokens: usage["prompt_tokens"]?.intValue,
                completionTokens: usage["completion_tokens"]?.intValue,
                costCredits: OpenRouterStreamParser.cost(usage["cost"])
            )
        }

        // 4. The first choice of index 0. A choice without an integer index counts as index 0.
        //    None: the event added no text (a usage-only frame has `"choices": []`).
        let choices = members["choices"]?.arrayValue ?? []
        guard let choice = choices.first(where: { $0.objectValue != nil && ($0["index"]?.intValue ?? 0) == 0 }) else {
            return false
        }

        // 5. Before any content is taken: text that comes with "length" or with a refusal is not kept.
        if let reason = choice["finish_reason"]?.stringValue {
            if reason == "length" {
                throw OpenRouterError.truncated
            }
            if reason != "stop" {
                throw OpenRouterError.invalidResponse
            }
            finishReason = reason
        }

        // 6 and 7. Reasoning, tool calls, refusals and blocks that are not text are never rendered.
        let content = OpenRouterStreamParser.content(choice[messageKey]?["content"])

        // 8.
        if text.utf8.count + content.utf8.count > OpenRouterStreamParser.maximumAnswerBytes {
            throw OpenRouterError.invalidResponse
        }
        text += content

        // 9.
        return !content.isEmpty
    }

    /// A string as it is; an array of blocks as the joined `text` of its "text" blocks; otherwise "".
    private static func content(_ value: JSONValue?) -> String {
        if let string = value?.stringValue {
            return string
        }
        guard let blocks = value?.arrayValue else {
            return ""
        }
        var joined = ""
        for block in blocks where block["type"]?.stringValue == "text" {
            joined += block["text"]?.stringValue ?? ""
        }
        return joined
    }

    /// A number or a numeric string that is finite and not negative; anything else is absent.
    private static func cost(_ value: JSONValue?) -> Double? {
        let candidate: Double?
        if let direct = value?.numberValue {
            candidate = direct
        } else if let string = value?.stringValue {
            candidate = Double(string)
        } else {
            candidate = nil
        }
        guard let number = candidate, number.isFinite, number >= 0 else {
            return nil
        }
        return number
    }
}
