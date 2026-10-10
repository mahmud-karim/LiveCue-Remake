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
        /// endStream sent, close code 1000, inside C-60.
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
        // STUB(WP-04)
        throw RelayFailure.local(.unexpectedFrame, origin: .stream)
    }

    public mutating func markEndStreamSent() {
        // STUB(WP-04)
        endStreamSent = true
    }

    /// How the stream ended, given the close. `insideWindow` is false when C-60 passed first.
    public func ending(closeCode: Int, closeReason: String, insideWindow: Bool) -> Ending {
        // STUB(WP-04)
        .interrupted(RelayFailure.local(.streamDropped, origin: .stream))
    }

    /// The ending when the socket dropped with no close frame.
    public func endingAfterDrop() -> Ending {
        // STUB(WP-04)
        .interrupted(RelayFailure.local(.streamDropped, origin: .stream))
    }

    /// 401, 404, 429, 502 to 504, other.
    public static func failure(forUpgradeStatus status: Int) -> RelayFailure {
        // STUB(WP-04)
        RelayFailure.local(.http, origin: .stream, httpStatus: status)
    }

    /// Rows 4 to 6 of protocol 13.6.
    public static func failure(for transport: TransportFailure) -> RelayFailure {
        // STUB(WP-04)
        RelayFailure.local(.network, origin: .stream)
    }
}
