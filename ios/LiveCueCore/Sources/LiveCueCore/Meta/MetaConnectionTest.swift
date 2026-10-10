import Foundation

/// The Meta key test: a connection without audio.
public enum MetaConnectionTest {
    /// Connect, handshake, acknowledgement, endStream, close; no audio; bounded by `testSeconds`.
    /// Throws `MetaSpeechError`, or `CancellationError`.
    public static func run(key: String, transport: any WebSocketTransport, timing: CoreTiming = .system) async throws {
        // STUB(WP-04)
        throw MetaSpeechError.connection
    }
}
