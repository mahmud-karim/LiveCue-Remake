import Foundation

/// One streaming speech connection, Meta or relay. One actor carries a whole stream, so the microphone
/// rule, framing, usage and the completed rule are tested on Linux with a fake socket.
public actor SpeechStreamSession {
    /// Finishes when the stream has ended, after the last event.
    public nonisolated let events: AsyncStream<SpeechStreamEvent>

    private let continuation: AsyncStream<SpeechStreamEvent>.Continuation
    private let dialect: SpeechStreamDialect
    private let transport: any WebSocketTransport
    private let timing: CoreTiming

    public init(dialect: SpeechStreamDialect, transport: any WebSocketTransport, timing: CoreTiming = .system) {
        let (events, continuation) = AsyncStream<SpeechStreamEvent>.makeStream()
        self.events = events
        self.continuation = continuation
        self.dialect = dialect
        self.transport = transport
        self.timing = timing
    }

    /// Opens the socket and returns when the provider is ready for audio: the Meta acknowledgement
    /// (limit 30 s) or the relay's valid `ready` (limit C-59). Emits `.status` while waiting, then
    /// `.status(.connected)` and `.usage(.begin)`. For the relay dialect, when C-59 passes first the socket
    /// is closed and the failure is `.relay(.local(.timeout, origin: .stream))`. Throws
    /// `SpeechStreamFailure` or `CancellationError`; after a throw the socket is closed and `events` has
    /// finished.
    public func start() async throws {
        // STUB(WP-04)
        continuation.finish()
        switch dialect {
        case .meta:
            throw SpeechStreamFailure(.meta(.connection))
        case .relay:
            throw SpeechStreamFailure(.relay(RelayFailure.local(.network, origin: .stream)))
        }
    }

    /// PCM16 mono at the dialect's rate, any length. Frames it and sends it. Ignored before `start`
    /// returned and after `finish` began. A send error ends the stream with `.failed(.sendFailed)`.
    public func send(audio: Data) async {
        // STUB(WP-04)
    }

    /// Flushes the remainder as one shorter frame, sends `endStream` once, then waits up to 15 s for the
    /// close. Returns after `events` has finished. `completed` only for close code 1000 inside the limit.
    public func finish() async -> SpeechStreamEnding {
        // STUB(WP-04)
        continuation.finish()
        return .interrupted
    }

    /// Closes now. Idempotent. Marks the usage stream interrupted if it was open.
    public func abort() async {
        // STUB(WP-04)
        continuation.finish()
    }
}
