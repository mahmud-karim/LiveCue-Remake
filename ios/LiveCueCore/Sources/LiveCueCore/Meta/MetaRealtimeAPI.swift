import Foundation

/// The fixed endpoint, the handshake, the acknowledgement check and the event sanitiser of Meta's
/// realtime transcription (spec 02 section 5).
public enum MetaRealtimeAPI {
    /// wss://api.meta.ai/v1/asr/realtime
    public static let endpoint: URL = URL(string: "wss://api.meta.ai/v1/asr/realtime") ?? URL(fileURLWithPath: "/")
    public static let sampleRate = 24_000
    public static let frameBytes = 3_840
    public static let connectSeconds: TimeInterval = 30
    public static let testSeconds: TimeInterval = 15
    public static let finalizeSeconds: TimeInterval = 15
    public static let maximumMessageBytes = 131_072
    public static let endStreamMessage = "{\"type\":\"endStream\"}"

    /// The six-member first message. Throws `.missingKey` for "" and `.malformedKey` for whitespace.
    public static func handshake(key: String) throws -> String {
        guard !key.isEmpty else { throw MetaSpeechError.missingKey }
        guard !key.contains(where: { $0.isWhitespace || $0.isNewline }) else { throw MetaSpeechError.malformedKey }

        // The key travels only here: never in the URL and never in a header (spec 02 section 5.1).
        let message = JSONValue.object([
            "authorization": .object(["accessToken": .string("Bearer " + key)]),
            "model": .string("muse-voice-transcribe-1.0"),
            "audioEncoding": .string("PCM_24KHZ"),
            "mode": .string("ENDPOINTING"),
            "partialMode": .string("CUMULATIVE"),
            "emitAudioProgress": .bool(true)
        ])
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        guard let data = try? encoder.encode(message) else { throw MetaSpeechError.connection }
        return String(decoding: data, as: UTF8.self)
    }

    /// Accepts only a JSON object with no `type` member and a non-empty string `sessionId`; else `.authentication`.
    public static func acknowledge(_ firstMessage: String) throws {
        // A `type` member of any value, `null` included, makes the message something other than the
        // acknowledgement (CORE-A9: the contract is kept exactly). Nothing of the message is surfaced.
        guard let members = JSONValue.parse(firstMessage)?.objectValue,
              members["type"] == nil,
              let sessionId = members["sessionId"]?.stringValue,
              !sessionId.isEmpty
        else { throw MetaSpeechError.authentication }
    }

    /// The sanitiser: throws `.provider` for type "error"; returns an ignored event for any type outside
    /// the five allowed; copies only `turnId` and `audioProcessedMs` (numbers, never booleans), `final`
    /// (boolean) and `transcript` (key replaced by "[redacted]", then the first 32,000 characters).
    public static func event(_ message: String, key: String) throws -> MetaEvent {
        guard let members = JSONValue.parse(message)?.objectValue,
              let type = members["type"]?.stringValue
        else { return MetaEvent(type: "") }

        // Nothing of an error event is surfaced: the caller shows the fixed provider text.
        if type == "error" { throw MetaSpeechError.provider }
        guard allowedEventTypes.contains(type) else { return MetaEvent(type: "") }

        // Built from this allowlist: `authorization`, `metadata` and every other member are dropped.
        var transcript = members["transcript"]?.stringValue
        if let text = transcript {
            // Redaction is skipped for an empty key: there is nothing to look for.
            let redacted = key.isEmpty ? text : text.replacingOccurrences(of: key, with: redactionMark)
            transcript = String(redacted.prefix(maximumTranscriptCharacters))
        }
        return MetaEvent(
            type: type,
            turnId: members["turnId"]?.intValue,
            audioProcessedMs: members["audioProcessedMs"]?.numberValue,
            isFinal: members["final"]?.boolValue,
            transcript: transcript
        )
    }

    // MARK: - Internals

    private static let allowedEventTypes: Set<String> = [
        "speechStart", "speechEnd", "speechComplete", "transcript", "audioProgress"
    ]
    private static let redactionMark = "[redacted]"
    private static let maximumTranscriptCharacters = 32_000
}
