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
        // STUB(WP-04)
        throw MetaSpeechError.connection
    }

    /// Accepts only a JSON object with no `type` member and a non-empty string `sessionId`; else `.authentication`.
    public static func acknowledge(_ firstMessage: String) throws {
        // STUB(WP-04)
        throw MetaSpeechError.authentication
    }

    /// The sanitiser: throws `.provider` for type "error"; returns an ignored event for any type outside
    /// the five allowed; copies only `turnId` and `audioProcessedMs` (numbers, never booleans), `final`
    /// (boolean) and `transcript` (key replaced by "[redacted]", then the first 32,000 characters).
    public static func event(_ message: String, key: String) throws -> MetaEvent {
        // STUB(WP-04)
        MetaEvent(type: "")
    }
}
