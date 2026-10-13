import Foundation
import LiveCueCore
import Observation

/// Settings (screens 6, with the About panel).
///
/// It stores nothing of its own: every value is read from the state owners, so a change there is seen
/// here.
@MainActor @Observable final class SettingsViewModel {
    private let speech: SpeechSettings
    private let assistant: AssistantSettings
    private let relay: RelayMonitor
    private let models: OnDeviceModelManager
    private let readiness: SystemReadiness
    /// True between `appeared()` and `disappeared()`, so the two calls on the monitor stay in pairs.
    @ObservationIgnored private var isObservingPCModel = false

    init(
        speech: SpeechSettings,
        assistant: AssistantSettings,
        relay: RelayMonitor,
        models: OnDeviceModelManager,
        readiness: SystemReadiness
    ) {
        self.speech = speech
        self.assistant = assistant
        self.relay = relay
        self.models = models
        self.readiness = readiness
    }

    var providerName: String {
        speech.provider.displayName
    }

    /// `readiness.providerLockReason?.caption`
    var providerLockCaption: String? {
        readiness.providerLockReason?.caption
    }

    var setup: ProviderSetupState {
        let provider = speech.provider
        return ProviderSetupState(
            provider: provider,
            hasMetaKey: speech.hasMetaKey,
            pcModelLabel: relay.homeLabel(for: provider),
            isModelLoaded: models.isLoaded(for: provider)
        )
    }

    /// S-01 / S-02 / S-03
    var explanation: String {
        speech.provider.explanation(answers: assistant.configuration.provider)
    }

    /// The model id, or what to do when OpenRouter has none yet.
    var assistantSubtitle: String {
        let model = assistant.configuration.model
        return model.isEmpty ? "Choose an OpenRouter model" : model
    }

    /// `assistant.instruction`
    var instruction: String {
        get {
            assistant.instruction
        }
        set {
            assistant.instruction = newValue
        }
    }

    var pairingTitle: String {
        relay.isPaired ? "PC connection" : "Pair Windows PC"
    }

    /// "Saved · connected" / "Saved · reconnecting"
    var pairingSubtitle: String? {
        guard relay.isPaired else { return nil }
        return relay.isOnline ? "Saved · connected" : "Saved · reconnecting"
    }

    /// `AppVersion.marketing`
    var versionText: String {
        AppVersion.marketing
    }

    /// `relay.beginPCModelObservation()`
    func appeared() {
        guard !isObservingPCModel else { return }
        isObservingPCModel = true
        relay.beginPCModelObservation()
    }

    func disappeared() {
        guard isObservingPCModel else { return }
        isObservingPCModel = false
        relay.endPCModelObservation()
    }
}
