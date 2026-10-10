import Foundation
import LiveCueCore

/// Provider `meta`: audio goes straight from the phone to Meta.
@MainActor final class MetaTranscriber: SpeechTranscriber {
    let provider: SpeechProvider = .meta
    private let channel: TranscriberEventChannel

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(audio: any AudioCapturing, transport: any WebSocketTransport, secrets: any SecretStoring, clock: any AppClock) {
        // STUB(WP-07)
        channel = TranscriberEventChannel()
    }

    func start(_ start: TranscriberStart) async throws {
        // STUB(WP-07)
        throw TranscriberFailure(.stream, message: TranscriberStatusText.disconnected)
    }

    func finalize() async throws {
        // STUB(WP-07)
    }

    func transcribePending() async throws -> TimeInterval {
        // STUB(WP-07)
        0
    }

    func shutdown() async {
        // STUB(WP-07)
    }
}
