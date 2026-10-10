import Foundation
import LiveCueCore

/// Builds the real transcriber for a provider.
@MainActor final class LiveTranscriberFactory: SpeechTranscriberFactory {
    private let audio: any AudioCapturing
    private let transport: any WebSocketTransport
    private let secrets: any SecretStoring
    private let pairingStore: any PairingStoring
    private let clock: any AppClock
    private let models: OnDeviceModelManager

    init(
        audio: any AudioCapturing,
        transport: any WebSocketTransport,
        secrets: any SecretStoring,
        pairingStore: any PairingStoring,
        clock: any AppClock,
        models: OnDeviceModelManager
    ) {
        self.audio = audio
        self.transport = transport
        self.secrets = secrets
        self.pairingStore = pairingStore
        self.clock = clock
        self.models = models
    }

    /// An on-device transcriber takes the recogniser that is loaded now. Without one its start fails
    /// with "Prepare Voz first." or "Prepare Parakeet first.".
    func makeTranscriber(for provider: SpeechProvider) -> any SpeechTranscriber {
        switch provider {
        case .meta:
            return MetaTranscriber(audio: audio, transport: transport, secrets: secrets, clock: clock)
        case .nemotron, .qwen3:
            return RelayTranscriber(model: provider, audio: audio, transport: transport, pairingStore: pairingStore, clock: clock)
        case .voz:
            return VozTranscriber(engine: models.vozEngine, audio: audio, clock: clock, models: models)
        case .parakeet:
            return ParakeetTranscriber(engine: models.parakeetEngine, audio: audio, clock: clock, models: models)
        }
    }
}
