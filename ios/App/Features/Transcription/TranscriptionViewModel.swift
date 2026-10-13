import Foundation
import LiveCueCore
import Observation

/// Transcription (screens 7): the provider list and what the selected provider needs.
@MainActor @Observable final class TranscriptionViewModel {
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

    /// Catalogue order.
    var providers: [SpeechProvider] {
        SpeechProvider.allCases
    }

    var selected: SpeechProvider {
        speech.provider
    }

    /// Why the provider cannot be changed right now, or nil when it can.
    var lockCaption: String? {
        readiness.providerLockReason?.caption
    }

    /// The display name: panel 2's title.
    var selectedTitle: String {
        speech.provider.displayName
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

    /// Stays on this screen. The owner refuses the change during a session and while it is locked.
    func select(_ provider: SpeechProvider) {
        speech.select(provider)
    }

    /// The PC model label of a PC-local provider is kept fresh while this screen is visible.
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
