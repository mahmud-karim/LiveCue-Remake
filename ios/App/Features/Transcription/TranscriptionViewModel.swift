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
            isModelLoaded: models.isLoaded(for: provider),
            modelNote: models.systemSpeechNote
        )
    }

    /// S-01 / S-02 / S-03
    var explanation: String {
        speech.provider.explanation(answers: assistant.configuration.provider)
    }

    /// Why a provider cannot be chosen on this iPhone, or nil. Apple on iOS 18 to 25:
    /// "Requires iOS 26 or later".
    func unavailableReason(for provider: SpeechProvider) -> String? {
        readiness.unavailableReason(for: provider)
    }

    /// Stays on this screen. The owner refuses the change during a session, while it is locked, and for a
    /// provider this iPhone cannot run. Before Apple is chosen for the first time the system is asked
    /// whether it can run it, so an iPhone that cannot keeps its choice.
    func select(_ provider: SpeechProvider) {
        guard provider == .apple, speech.provider != .apple, models.systemSpeechAvailability == .unknown else {
            speech.select(provider)
            return
        }
        Task { [speech, models] in
            await models.refreshSystemSpeech(updatingStatus: false)
            speech.select(provider)
        }
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
