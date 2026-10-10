import Foundation
import LiveCueCore

/// Shared by Meta and relay: owns one `SpeechStreamSession` per start, pumps capture into it and maps
/// its events to `TranscriberEvent`s (adding the stream offset to segment times).
@MainActor final class StreamingTranscriberEngine {
    init(
        provider: SpeechProvider,
        audio: any AudioCapturing,
        transport: any WebSocketTransport,
        clock: any AppClock,
        channel: TranscriberEventChannel
    ) {
        // STUB(WP-07)
    }

    func start(_ start: TranscriberStart, dialect: SpeechStreamDialect) async throws {
        // STUB(WP-07)
        throw TranscriberFailure(.stream, message: TranscriberStatusText.disconnected)
    }

    func finalize() async throws {
        // STUB(WP-07)
    }

    func shutdown() async {
        // STUB(WP-07)
    }
}
