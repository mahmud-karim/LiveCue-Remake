import Foundation

/// Steps 1, 2, 4, 6 and 7 of protocol 13.7 without any I/O.
public struct RelaySpeechNegotiation: Sendable {
    public enum Step: Equatable, Sendable {
        case loading(elapsedSeconds: Int)
        /// Open the microphone now.
        case ready(handshakeMs: Double)
        case event(StreamTranscriptEvent, audioProcessedMs: Double?, hasText: Bool)
        case streamComplete(finalizationMs: Double?)
        case ignore
    }

    public enum Ending: Equatable, Sendable {
        /// endStream sent, then a clean close inside C-60: code 1000, or 1001 or no status code (1005)
        /// with no code of the relay as the reason.
        case completed
        case interrupted(RelayFailure)
    }

    private let model: SpeechProvider
    public private(set) var isReady: Bool
    public private(set) var endStreamSent: Bool

    public init(model: SpeechProvider) {
        self.model = model
        self.isReady = false
        self.endStreamSent = false
    }

    /// Throws `RelayFailure`: an `error` frame's code; L-SPEECH-NOT-CONFIRMED for a `ready` naming another
    /// provider or sample rate; L-UNEXPECTED-FRAME for `speechStart`, `transcript`, `speechComplete` or
    /// `streamComplete` before `ready`, and for text that is not a frame. `.unknown` is `Step.ignore` in
    /// every state.
    public mutating func receive(text: String) throws -> Step {
        guard let frame = RelaySpeechFrame.parse(text) else {
            throw RelayFailure.local(.unexpectedFrame, origin: .stream)
        }

        switch frame {
        case .unknown:
            return .ignore

        case .error(let code, let message):
            // The relay's text is kept only for a code this build does not know: that is its one use
            // (protocol 14), and a known code then equals `RelayFailure.wire(_:origin:)`.
            let isKnown = RelayWireCode(rawValue: code) != nil
            throw RelayFailure(kind: .wire(code), origin: .stream, relayText: isKnown ? nil : message)

        case .loading(_, let elapsedSeconds, _):
            // The relay stops sending these at `ready`; a late one changes nothing.
            return isReady ? .ignore : .loading(elapsedSeconds: elapsedSeconds)

        case .ready(let provider, let sampleRate, let handshakeMs):
            guard !isReady else { return .ignore }
            guard provider == model.rawValue, sampleRate == RelayConstants.speechSampleRate else {
                throw RelayFailure.local(.speechNotConfirmed, origin: .stream)
            }
            isReady = true
            return .ready(handshakeMs: handshakeMs)

        case .speechStart(let turnId, let audioProcessedMs):
            try requireReady()
            return .event(
                .speechStart(turn: turnId, processedMs: audioProcessedMs ?? 0),
                audioProcessedMs: audioProcessedMs,
                hasText: false
            )

        case .transcript(let turnId, let audioProcessedMs, let transcript):
            try requireReady()
            return .event(
                .transcript(turn: turnId, text: transcript, processedMs: audioProcessedMs ?? 0),
                audioProcessedMs: audioProcessedMs,
                hasText: transcript?.isEmpty == false
            )

        case .speechComplete(let turnId, let audioProcessedMs, let transcript):
            try requireReady()
            return .event(
                .speechComplete(turn: turnId, text: transcript, processedMs: audioProcessedMs ?? 0),
                audioProcessedMs: audioProcessedMs,
                hasText: transcript?.isEmpty == false
            )

        case .streamComplete(_, let finalizationMs):
            try requireReady()
            return .streamComplete(finalizationMs: finalizationMs)
        }
    }

    public mutating func markEndStreamSent() {
        endStreamSent = true
    }

    /// How the stream ended, given the close. `insideWindow` is false when C-60 passed first.
    public func ending(closeCode: Int, closeReason: String, insideWindow: Bool) -> Ending {
        guard insideWindow else {
            // Step 6: C-60 passed with no close in time.
            return .interrupted(RelayFailure.local(.streamDropped, origin: .stream))
        }
        if endStreamSent, closeCode == 1000 {
            return .completed
        }
        // "Going away" and "no status code" after `endStream` are the peer ending the stream too, unless
        // the relay named a code of its own as the reason.
        if endStreamSent, closeCode == 1001 || closeCode == 1005, RelayWireCode(rawValue: closeReason) == nil {
            return .completed
        }
        // Step 7: a close that no `error` frame explained. The reason text of a refusing relay is the code.
        if let code = RelayWireCode(rawValue: closeReason) {
            return .interrupted(RelayFailure.wire(code, origin: .stream))
        }
        return .interrupted(RelayFailure.local(.streamDropped, origin: .stream))
    }

    /// The ending when the socket dropped with no close frame.
    public func endingAfterDrop() -> Ending {
        .interrupted(RelayFailure.local(.streamDropped, origin: .stream))
    }

    /// 401, 404, 429, 502 to 504, other.
    public static func failure(forUpgradeStatus status: Int) -> RelayFailure {
        switch status {
        case 401:
            return RelayFailure.wire(.unauthorized, origin: .stream)
        case 404:
            return RelayFailure.wire(.notFound, origin: .stream)
        case 429:
            return RelayFailure.wire(.tooManyRequests, origin: .stream)
        case 502, 503, 504:
            return RelayFailure.local(.noRelay, origin: .stream)
        default:
            return RelayFailure.local(.http, origin: .stream, httpStatus: status)
        }
    }

    /// Rows 4 to 6 of protocol 13.6.
    public static func failure(for transport: TransportFailure) -> RelayFailure {
        switch transport {
        case .tls:
            return RelayFailure.local(.tls, origin: .stream)
        case .unreachable, .timedOut:
            return RelayFailure.local(.unreachable, origin: .stream)
        case .other, .cancelled:
            // Cancellation is row 1 and never reaches this table: the caller turns it into
            // `CancellationError` first. It is listed here only so the switch is complete.
            return RelayFailure.local(.network, origin: .stream)
        }
    }

    /// The four lab-derived frames mean nothing before a valid `ready` (step 2).
    private func requireReady() throws {
        guard isReady else {
            throw RelayFailure.local(.unexpectedFrame, origin: .stream)
        }
    }
}
