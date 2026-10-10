import Foundation
import LiveCueCore

/// Provider `meta`: audio goes straight from the phone to Meta.
///
/// The key is read from the secret store at every start and travels only inside the stream's first
/// message. Each start is a new billable stream; a disconnect pauses the session and nothing reconnects
/// by itself.
@MainActor final class MetaTranscriber: SpeechTranscriber {
    let provider: SpeechProvider = .meta
    private let channel: TranscriberEventChannel
    private let engine: StreamingTranscriberEngine
    private let secrets: any SecretStoring

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(audio: any AudioCapturing, transport: any WebSocketTransport, secrets: any SecretStoring, clock: any AppClock) {
        let channel = TranscriberEventChannel()
        self.channel = channel
        self.engine = StreamingTranscriberEngine(
            provider: .meta,
            audio: audio,
            transport: transport,
            clock: clock,
            channel: channel
        )
        self.secrets = secrets
    }

    func start(_ start: TranscriberStart) async throws {
        // Without a key the stream refuses to open and says so with the missing-key text; a key of the
        // wrong shape is refused the same way. Nothing is opened in either case.
        let key = secrets.read(.metaAPIKey) ?? ""
        try await engine.start(start, dialect: .meta(key: key))
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
