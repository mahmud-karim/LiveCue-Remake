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
    /// read as absent. `ready` and `error` with a missing member parse as `.unknown`. `loading` with a
    /// missing member is still `loading`, with 0 or "" in its place.
    public static func parse(_ text: String) -> RelaySpeechFrame? {
        guard let members = JSONValue.parse(text)?.objectValue,
              let type = members["type"]?.stringValue
        else { return nil }

        // Read through the strict accessors: a boolean is never a number, a number with a fraction is
        // never a turn, and a member the frame does not list is never looked at (P2-218).
        let turnId = members["turnId"]?.intValue
        let audioProcessedMs = members["audioProcessedMs"]?.numberValue
        let transcript = members["transcript"]?.stringValue

        switch type {
        case "loading":
            // The frame only keeps the wait going, so a missing member must not end the wait (A-14).
            return .loading(
                provider: members["provider"]?.stringValue ?? "",
                elapsedSeconds: members["elapsedSeconds"]?.intValue ?? 0,
                message: members["message"]?.stringValue ?? ""
            )
        case "ready":
            guard let provider = members["provider"]?.stringValue,
                  let sampleRate = members["sampleRate"]?.intValue,
                  let handshakeMs = members["handshakeMs"]?.numberValue
            else { return .unknown }
            return .ready(provider: provider, sampleRate: sampleRate, handshakeMs: handshakeMs)
        case "speechStart":
            return .speechStart(turnId: turnId, audioProcessedMs: audioProcessedMs)
        case "transcript":
            return .transcript(turnId: turnId, audioProcessedMs: audioProcessedMs, transcript: transcript)
        case "speechComplete":
            return .speechComplete(turnId: turnId, audioProcessedMs: audioProcessedMs, transcript: transcript)
        case "streamComplete":
            return .streamComplete(
                audioProcessedMs: audioProcessedMs,
                finalizationMs: members["finalizationMs"]?.numberValue
            )
        case "error":
            guard let code = members["code"]?.stringValue,
                  let message = members["message"]?.stringValue
            else { return .unknown }
            return .error(code: code, message: message)
        default:
            return .unknown
        }
    }
}
