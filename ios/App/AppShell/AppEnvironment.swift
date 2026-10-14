import Foundation
import Observation

/// The container: the dependencies, every state owner and every view model, built once at launch.
/// There are no singletons and no global state besides it.
@MainActor @Observable final class AppEnvironment {
    let mode: LaunchMode
    let dependencies: AppDependencies

    // State owners, built in this order.
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

    // View models, built last.
    let home: HomeViewModel
    let live: LiveSessionViewModel
    let assistantLab: AssistantLabViewModel
    let settings: SettingsViewModel
    let transcription: TranscriptionViewModel
    let pcSpeechModel: PCSpeechModelViewModel
    let metaKey: MetaKeyViewModel
    let pairingScreen: PairingViewModel
    let modelLibrary: ModelLibraryViewModel
    let benchmark: BenchmarkViewModel
    let historyScreen: HistoryViewModel

    @ObservationIgnored private var didBootstrap = false

    /// The only way the app makes one. Applies the test-seam launch actions before any owner reads a store.
    static func make(_ launch: LaunchConfiguration) -> AppEnvironment {
        switch launch.mode {
        case .live:
            return AppEnvironment(mode: .live, dependencies: AppDependencies.live())
        case .fixture(let testMode):
            let dependencies = AppDependencies.fixture(testMode)
            TestSeamLaunchActions.apply(
                testMode,
                preferences: dependencies.preferences,
                secrets: dependencies.secrets,
                pairingStore: dependencies.pairingStore
            )
            return AppEnvironment(mode: launch.mode, dependencies: dependencies)
        }
    }

    init(mode: LaunchMode, dependencies: AppDependencies) {
        let app = AppModel()
        let activity = SessionActivity()
        let toasts = ToastCenter(clock: dependencies.clock)
        let speech = SpeechSettings(
            preferences: dependencies.preferences,
            secrets: dependencies.secrets,
            keyTester: dependencies.metaKeyTester,
            activity: activity
        )
        let assistant = AssistantSettings(
            preferences: dependencies.preferences,
            secrets: dependencies.secrets,
            pairingStore: dependencies.pairingStore,
            answers: dependencies.answers,
            keyVerifier: dependencies.openRouterKeyVerifier,
            activity: activity
        )
        let models = OnDeviceModelManager(
            loader: dependencies.engineLoader,
            activity: activity,
            clock: dependencies.clock,
            systemSpeech: dependencies.systemSpeech
        )
        let relay = RelayMonitor(
            gateway: dependencies.relayGateway,
            pairingStore: dependencies.pairingStore,
            clock: dependencies.clock,
            speech: speech,
            assistant: assistant,
            activity: activity
        )
        let pairing = PairingController(gateway: dependencies.relayGateway, store: dependencies.pairingStore, monitor: relay)
        let history = HistoryModel(store: dependencies.sessions)
        let readiness = SystemReadiness(speech: speech, assistant: assistant, relay: relay, models: models)
        let session = SessionController(
            SessionDependencies(
                transcribers: dependencies.makeTranscriberFactory(models),
                answers: dependencies.answers,
                sessions: dependencies.sessions,
                secrets: dependencies.secrets,
                pairingStore: dependencies.pairingStore,
                permissions: dependencies.permissions,
                clock: dependencies.clock,
                app: app,
                activity: activity,
                speech: speech,
                assistant: assistant,
                relay: relay,
                models: models,
                history: history
            )
        )

        self.mode = mode
        self.dependencies = dependencies
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

        self.home = HomeViewModel(
            session: session,
            speech: speech,
            assistant: assistant,
            relay: relay,
            models: models,
            readiness: readiness,
            history: history
        )
        self.live = LiveSessionViewModel(session: session, assistant: assistant, clipboard: dependencies.clipboard, toasts: toasts)
        self.assistantLab = AssistantLabViewModel(
            assistant: assistant,
            relay: relay,
            activity: activity,
            history: history,
            session: session
        )
        self.settings = SettingsViewModel(speech: speech, assistant: assistant, relay: relay, models: models, readiness: readiness)
        self.transcription = TranscriptionViewModel(
            speech: speech,
            assistant: assistant,
            relay: relay,
            models: models,
            readiness: readiness
        )
        self.pcSpeechModel = PCSpeechModelViewModel(speech: speech, relay: relay)
        self.metaKey = MetaKeyViewModel(speech: speech)
        self.pairingScreen = PairingViewModel(pairing: pairing, relay: relay, app: app)
        self.modelLibrary = ModelLibraryViewModel(speech: speech, models: models, app: app, clock: dependencies.clock)
        self.benchmark = BenchmarkViewModel(session: session, history: history)
        self.historyScreen = HistoryViewModel(history: history)

        // Wiring between owners. Each closure holds the owner it calls, not this container.
        speech.onProviderChanged = { [models, relay] provider in
            models.providerChanged(to: provider)
            relay.selectionChanged()
        }
        speech.changeIsBlocked = { [readiness] in
            readiness.providerLockReason != nil
        }
        speech.selectionIsUnavailable = { [readiness] provider in
            readiness.unavailableReason(for: provider) != nil
        }
        assistant.onProviderChanged = { [relay] in
            relay.selectionChanged()
        }
        history.willDelete = { [session] id in
            await session.cancelSummary(forSession: id)
        }

        // The provider the launch actions selected comes with its model loaded.
        if case .fixture(let testMode) = mode {
            if testMode.fluidReady {
                models.installPreloaded(variant: "fluid-mini", voz: nil, parakeet: nil, fluid: FixtureStreamingEngine())
            } else if testMode.onDeviceReady {
                models.installPreloaded(variant: "parakeet", voz: nil, parakeet: FixtureStreamingEngine())
            }
        }
    }

    /// Once, from the root view's task: close interrupted sessions, load History, start the relay monitor.
    /// With Apple speech selected, the system is then asked whether its language asset is installed, so
    /// that the provider is ready without a manual step. A second call does nothing.
    func bootstrap() async {
        guard !didBootstrap else { return }
        didBootstrap = true
        await dependencies.sessions.closeInterruptedSessions()
        await history.reload()
        relay.start()
        if speech.provider == .apple {
            await models.refreshSystemSpeech()
        }
    }

    /// The language asset may have been installed or removed while the app was away.
    func sceneBecameActive() {
        relay.appBecameActive()
        if didBootstrap, speech.provider == .apple {
            Task { [models] in
                await models.refreshSystemSpeech()
            }
        }
    }
}
