import Foundation
import LiveCueCore
@testable import LiveCueRemake

/// Builds every state owner on fakes, with the same wiring as `AppEnvironment.init`.
@MainActor final class TestOwners {
    // Fakes.
    let clock: ManualClock
    let preferences: FakePreferences
    let secrets: FakeSecrets
    let pairingStore: FakePairingStore
    let sessions: InMemorySessionStore
    let gateway: FakeRelayGateway
    let codex: FakeAnswerService
    let openRouter: FakeAnswerService
    let keyTester: FakeMetaKeyTester
    let transcribers: FakeTranscriberFactory
    let vozEngine: FakeBatchEngine
    let parakeetEngine: FakeStreamingEngine
    let clipboard: FakeClipboard

    // State owners.
    let app: AppModel
    let activity: SessionActivity
    let toasts: ToastCenter
    let speech: SpeechSettings
    let assistant: AssistantSettings
    let models: OnDeviceModelManager
    let relay: RelayMonitor
    let pairing: PairingController
    let history: HistoryModel
    let readiness: SystemReadiness
    let session: SessionController

    /// - Parameters:
    ///   - paired: the pairing store starts with the fixture pairing.
    ///   - provider: the stored speech provider.
    ///   - answers: the stored answer service, with its default selection.
    ///   - metaKey: the stored Meta key, or nil for none.
    ///   - microphoneGranted: what the microphone permission request answers.
    init(
        paired: Bool = true,
        provider: SpeechProvider = .meta,
        answers: AssistantProvider = .codex,
        metaKey: String? = "fixture-meta-key",
        microphoneGranted: Bool = true
    ) {
        let clock = ManualClock()
        let preferences = FakePreferences()
        preferences.speechProvider = provider
        preferences.assistantConfiguration = AssistantConfiguration.default(for: answers)

        var storedSecrets: [SecretKind: String] = [:]
        if let metaKey {
            storedSecrets[.metaAPIKey] = metaKey
        }
        let secrets = FakeSecrets(storedSecrets)
        let pairingStore = FakePairingStore(paired ? FixtureValues.pairing : nil)
        let sessions = InMemorySessionStore()
        let gateway = FakeRelayGateway()
        let codex = FakeAnswerService(provider: .codex)
        let openRouter = FakeAnswerService(provider: .openrouter)
        let answerServices = FakeAnswerServices(codex: codex, openRouter: openRouter)
        let keyTester = FakeMetaKeyTester()
        let transcribers = FakeTranscriberFactory()
        let vozEngine = FakeBatchEngine()
        let parakeetEngine = FakeStreamingEngine()
        let clipboard = FakeClipboard()

        let app = AppModel()
        let activity = SessionActivity()
        let toasts = ToastCenter(clock: clock)
        let speech = SpeechSettings(preferences: preferences, secrets: secrets, keyTester: keyTester, activity: activity)
        let assistant = AssistantSettings(
            preferences: preferences,
            secrets: secrets,
            pairingStore: pairingStore,
            answers: answerServices,
            keyVerifier: openRouter,
            activity: activity
        )
        let models = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: vozEngine, parakeet: parakeetEngine),
            activity: activity,
            clock: clock
        )
        let relay = RelayMonitor(
            gateway: gateway,
            pairingStore: pairingStore,
            clock: clock,
            speech: speech,
            assistant: assistant,
            activity: activity
        )
        let pairing = PairingController(gateway: gateway, store: pairingStore, monitor: relay)
        let history = HistoryModel(store: sessions)
        let readiness = SystemReadiness(speech: speech, assistant: assistant, relay: relay, models: models)
        let session = SessionController(
            SessionDependencies(
                transcribers: transcribers,
                answers: answerServices,
                sessions: sessions,
                secrets: secrets,
                pairingStore: pairingStore,
                permissions: FakePermissions(microphone: microphoneGranted),
                clock: clock,
                app: app,
                activity: activity,
                speech: speech,
                assistant: assistant,
                relay: relay,
                models: models,
                history: history
            )
        )

        // The wiring of `AppEnvironment.init`.
        speech.onProviderChanged = { [models, relay] provider in
            models.providerChanged(to: provider)
            relay.selectionChanged()
        }
        speech.changeIsBlocked = { [readiness] in
            readiness.providerLockReason != nil
        }
        assistant.onProviderChanged = { [relay] in
            relay.selectionChanged()
        }
        history.willDelete = { [session] id in
            await session.cancelSummary(forSession: id)
        }

        self.clock = clock
        self.preferences = preferences
        self.secrets = secrets
        self.pairingStore = pairingStore
        self.sessions = sessions
        self.gateway = gateway
        self.codex = codex
        self.openRouter = openRouter
        self.keyTester = keyTester
        self.transcribers = transcribers
        self.vozEngine = vozEngine
        self.parakeetEngine = parakeetEngine
        self.clipboard = clipboard
        self.app = app
        self.activity = activity
        self.toasts = toasts
        self.speech = speech
        self.assistant = assistant
        self.models = models
        self.relay = relay
        self.pairing = pairing
        self.history = history
        self.readiness = readiness
        self.session = session
    }
}
