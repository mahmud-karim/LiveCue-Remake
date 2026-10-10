import Foundation

/// A frame the relay sends on the speech socket (protocol 10.3).
public enum RelaySpeechFrame: Equatable, Sendable {
    case loading(provider: String, elapsedSeconds: Int, message: String)
    case ready(provider: String, sampleRate: Int, handshakeMs: Double)
    case speechStart(turnId: Int?, audioProcessedMs: Double?)
    case transcript(turnId: Int?, audioProcessedMs: Double?, transcript: String?)
    case speechComplete(turnId: Int?, audioProcessedMs: Double?, transcript: String?)
    case streamComplete(audioProcessedMs: Double?, finalizationMs: Double?)
    case error(code: String, message: String)
    /// A JSON object of another type: ignored.
    case unknown

    /// nil when the text is not a JSON object with a string `type`. Members of the wrong JSON type are
    /// read as absent. `ready` and `error` with a missing member parse as `.unknown`.
    public static func parse(_ text: String) -> RelaySpeechFrame? {
        // STUB(WP-04)
        nil
    }
}
