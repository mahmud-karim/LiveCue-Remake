import Foundation
import LiveCueCore

/// Providers `nemotron` and `qwen3`: audio goes to the model on the paired PC.
///
/// The pairing is read from the pairing store at every start. The relay may answer `loading` for a
/// while; the microphone opens only at its valid `ready`.
@MainActor final class RelayTranscriber: SpeechTranscriber {
    let provider: SpeechProvider
    private let channel: TranscriberEventChannel
    private let engine: StreamingTranscriberEngine
    private let pairingStore: any PairingStoring

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
        let channel = TranscriberEventChannel()
        self.provider = model
        self.channel = channel
        self.engine = StreamingTranscriberEngine(
            provider: model,
            audio: audio,
            transport: transport,
            clock: clock,
            channel: channel
        )
        self.pairingStore = pairingStore
    }

    func start(_ start: TranscriberStart) async throws {
        guard let pairing = pairingStore.load() else {
            // Nothing is paired, so there is no PC to connect to.
            throw TranscriberFailure(.stream, message: RelayMessages.text(for: RelayLocalCondition.notPaired))
        }
        try await engine.start(start, dialect: .relay(pairing: pairing, model: provider))
    }

    func finalize() async throws {
        try await engine.finalize()
    }

    func transcribePending() async throws -> TimeInterval {
        0
    }

    func shutdown() async {
        await engine.shutdown()
    }
}
