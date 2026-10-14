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
    /// with "Prepare Voz first.", "Prepare Parakeet first." or "Prepare Fluid Speech Mini first.". Apple
    /// takes the system recogniser, which is there once its language asset is installed.
    func makeTranscriber(for provider: SpeechProvider) -> any SpeechTranscriber {
        switch provider {
        case .meta:
            return MetaTranscriber(audio: audio, transport: transport, secrets: secrets, clock: clock)
        case .nemotron, .qwen3:
            return RelayTranscriber(model: provider, audio: audio, transport: transport, pairingStore: pairingStore, clock: clock)
        case .voz:
            return VozTranscriber(engine: models.vozEngine, audio: audio, clock: clock, models: models)
        case .parakeet:
            return ParakeetTranscriber(provider: .parakeet, engine: models.parakeetEngine, audio: audio, clock: clock, models: models)
        case .apple:
            return AppleTranscriber(engine: models.systemSpeechEngine, audio: audio, clock: clock, models: models)
        case .fluid:
            // The same transcriber as Parakeet: its recogniser makes live text out of whole windows.
            return ParakeetTranscriber(provider: .fluid, engine: models.fluidEngine, audio: audio, clock: clock, models: models)
        }
    }
}
