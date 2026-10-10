import Foundation
import LiveCueCore

extension AppDependencies {
    /// The real dependencies: system clock, Keychain, files, URL sessions, the microphone.
    static func live() -> AppDependencies {
        let clock = SystemClock()
        let secrets = KeychainSecretStore(names: StorageNames.live)
        let pairingStore = FilePairingStore(names: StorageNames.live)

        // Without a store on disk the app still runs: history then lasts until the app is closed.
        let sessions: any SessionStoring
        if let store = try? FileSessionStore.open() {
            sessions = store
        } else {
            sessions = InMemorySessionStore()
        }

        let relayClient = RelayClient(transport: URLSessionHTTPTransport(), timing: clock.coreTiming)
        let openRouterClient = OpenRouterClient(
            transport: URLSessionHTTPTransport(resourceTimeout: OpenRouterClient.assistTotalSeconds),
            timing: clock.coreTiming
        )
        let codex = CodexRelayAnswerService(gateway: relayClient, pairingStore: pairingStore)
        let openRouter = OpenRouterAnswerService(client: openRouterClient, secrets: secrets)
        let socketTransport = URLSessionWebSocketTransport()
        let audio = AudioCapture()

        return AppDependencies(
            clock: clock,
            permissions: SystemPermissions(),
            clipboard: SystemClipboard(),
            preferences: UserDefaultsPreferencesStore(),
            secrets: secrets,
            pairingStore: pairingStore,
            sessions: sessions,
            relayGateway: relayClient,
            answers: LiveAnswerServices(codex: codex, openRouter: openRouter),
            openRouterKeyVerifier: openRouter,
            metaKeyTester: LiveMetaKeyTester(transport: socketTransport, clock: clock),
            engineLoader: LiveOnDeviceEngineLoader(),
            makeTranscriberFactory: { models in
                LiveTranscriberFactory(
                    audio: audio,
                    transport: socketTransport,
                    secrets: secrets,
                    pairingStore: pairingStore,
                    clock: clock,
                    models: models
                )
            }
        )
    }
}
