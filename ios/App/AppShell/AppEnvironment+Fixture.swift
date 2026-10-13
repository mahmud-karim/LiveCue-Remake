import Foundation
import LiveCueCore

extension AppDependencies {
    /// The dependencies of the test seam. Nothing here opens a socket, a URL session, the microphone or
    /// the camera. Sessions stay in memory; keys and the pairing use the test names.
    static func fixture(_ mode: TestMode) -> AppDependencies {
        let clock = SystemClock()

        let pairingStore: any PairingStoring
        if mode.pairingPersistenceTest {
            pairingStore = FilePairingStore(names: StorageNames.test)
        } else {
            pairingStore = FixturePairingStore()
        }

        return AppDependencies(
            clock: clock,
            permissions: FixturePermissions(),
            clipboard: SystemClipboard(),
            preferences: UserDefaultsPreferencesStore(),
            secrets: KeychainSecretStore(names: StorageNames.test),
            pairingStore: pairingStore,
            sessions: InMemorySessionStore(),
            relayGateway: FixtureRelayGateway(mode: mode, clock: clock),
            answers: FixtureAnswerServices(mode: mode, clock: clock),
            openRouterKeyVerifier: FixtureAnswerService(provider: .openrouter, mode: mode, clock: clock),
            metaKeyTester: FixtureMetaKeyTester(),
            engineLoader: FixtureEngineLoader(clock: clock),
            systemSpeech: FixtureSystemSpeech(),
            makeTranscriberFactory: { _ in
                FixtureTranscriberFactory(clock: clock)
            }
        )
    }
}
