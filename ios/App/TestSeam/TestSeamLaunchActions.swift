import Foundation
import LiveCueCore

/// What the test seam does to the stores at launch, before any owner reads them.
@MainActor enum TestSeamLaunchActions {
    /// In this order: provider = meta (parakeet with onDeviceReady; fluid with fluidReady, which comes
    /// first when both are given); reset the answer configuration unless openRouterPersistenceTest; save
    /// the placeholder Meta key unless metaKeyPersistence; remove it when metaNoKey; remove the OpenRouter
    /// key when openRouterUIReset; save the fixture pairing when savePairingFixture; remove the Keychain
    /// copy when dropTestKeychain.
    static func apply(
        _ mode: TestMode,
        preferences: any PreferencesStoring,
        secrets: any SecretStoring,
        pairingStore: any PairingStoring
    ) {
        if mode.fluidReady {
            preferences.speechProvider = .fluid
        } else {
            preferences.speechProvider = mode.onDeviceReady ? .parakeet : .meta
        }

        if !mode.openRouterPersistenceTest {
            preferences.resetAssistantConfiguration()
        }

        // A store that refuses does not stop the launch: the test that needs the value fails on it.
        if !mode.metaKeyPersistence {
            try? secrets.save(FixtureValues.metaKey, for: .metaAPIKey)
        }
        if mode.metaNoKey {
            try? secrets.remove(.metaAPIKey)
        }
        if mode.openRouterUIReset {
            try? secrets.remove(.openRouterAPIKey)
        }

        if mode.savePairingFixture {
            try? pairingStore.save(FixtureValues.pairing)
        }
        if mode.dropTestKeychain {
            pairingStore.removeKeychainCopy()
        }
    }
}
