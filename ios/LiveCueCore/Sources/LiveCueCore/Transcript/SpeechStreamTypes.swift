import Foundation

/// Which streaming speech service a session talks to.
public enum SpeechStreamDialect: Equatable, Sendable {
    case meta(key: String)
    case relay(pairing: PairingPayload, model: SpeechProvider)
}

public enum SpeechStreamStatus: Equatable, Sendable {
    case connecting
    /// Relay only.
    case loading(elapsedSeconds: Int)
    case connected(setupSeconds: Double)
    /// From stream start.
    case firstWords(seconds: Double)
    case finalized(seconds: Double)
}

public struct SpeechStreamFailure: Error, Equatable, Sendable {
    public enum Source: Equatable, Sendable {
        case relay(RelayFailure)
        case meta(MetaSpeechError)
        /// "Audio connection failed. Resume to reconnect."
        case sendFailed
    }

    public var source: Source

    public init(_ source: Source) {
        self.source = source
    }

    /// The user text for the source.
    public var message: String {
        switch source {
        case .relay(let failure):
            return RelayMessages.text(for: failure)
        case .meta(let error):
            return error.errorDescription ?? ""
        case .sendFailed:
            return "Audio connection failed. Resume to reconnect."
        }
    }
}

public enum SpeechStreamEvent: Equatable, Sendable {
    case status(SpeechStreamStatus)
    case partial(String)
    /// Times relative to this stream.
    case segment(TranscriptSegment)
    case usage(UsageUpdate)
    /// Only after a successful start; the socket is already closed.
    case failed(SpeechStreamFailure)
}

public enum SpeechStreamEnding: Equatable, Sendable {
    case completed, interrupted
}
