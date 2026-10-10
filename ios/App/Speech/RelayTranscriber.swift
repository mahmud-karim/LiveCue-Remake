import Foundation
import LiveCueCore

/// Providers `nemotron` and `qwen3`: audio goes to the model on the paired PC.
@MainActor final class RelayTranscriber: SpeechTranscriber {
    let provider: SpeechProvider
    private let channel: TranscriberEventChannel

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(
        model: SpeechProvider,
        audio: any AudioCapturing,
        transport: any WebSocketTransport,
        pairingStore: any PairingStoring,
        clock: any AppClock
    ) {
        // STUB(WP-07)
        provider = model
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
