import Foundation
import LiveCueCore

/// Why the speech provider cannot be changed right now.
enum ProviderLockReason: Equatable, Sendable {
    case preparingModel
    case pcModelChanging

    var caption: String {
        switch self {
        case .preparingModel: return "Available when the model has finished preparing."
        case .pcModelChanging: return "Available when the PC model has finished changing."
        }
    }
}

/// Whether the PC is needed, whether speech and answers are ready, and the provider lock (spec 01
/// section 0.2). It stores nothing: reading its properties inside a view body is tracked through the
/// owners it reads.
@MainActor final class SystemReadiness {
    private let speech: SpeechSettings
    private let assistant: AssistantSettings
    private let relay: RelayMonitor
    private let models: OnDeviceModelManager

    init(speech: SpeechSettings, assistant: AssistantSettings, relay: RelayMonitor, models: OnDeviceModelManager) {
        self.speech = speech
        self.assistant = assistant
        self.relay = relay
        self.models = models
    }

    /// The speech provider is PC-local or the answer service is Codex.
    var needsPC: Bool {
        PCRequirement.needsPC(speech: speech.provider, answers: assistant.configuration.provider)
    }

    /// Meta: a key is saved. PC-local: the PC model can start a session. On-device: the model of the
    /// selected provider is loaded.
    var speechReady: Bool {
        let provider = speech.provider
        if provider.isCloud {
            return speech.hasMetaKey
        }
        if provider.isPCLocal {
            return relay.canStartSession(for: provider)
        }
        return models.isLoaded(for: provider)
    }

    /// Codex: the relay is online. OpenRouter: a key is saved and a model is chosen.
    var answersReady: Bool {
        switch assistant.configuration.provider {
        case .codex:
            return relay.isOnline
        case .openrouter:
            return assistant.hasOpenRouterKey && !assistant.configuration.model.isEmpty
        }
    }

    var systemReady: Bool {
        speechReady && answersReady
    }

    /// An on-device model is being prepared, or a PC model command is in flight or the PC reports
    /// starting or stopping. A running session is not a reason here: `SpeechSettings.select` refuses
    /// that by itself.
    var providerLockReason: ProviderLockReason? {
        if models.isPreparing {
            return .preparingModel
        }
        if relay.isPCModelCommandInFlight || relay.pcModelStatus?.isChanging == true {
            return .pcModelChanging
        }
        return nil
    }
}
