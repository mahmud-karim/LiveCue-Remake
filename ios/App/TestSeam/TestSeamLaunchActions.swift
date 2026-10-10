import Foundation
import LiveCueCore

/// What the test seam does to the stores at launch, before any owner reads them.
@MainActor enum TestSeamLaunchActions {
    /// In this order: provider = meta (parakeet with onDeviceReady); reset the answer configuration unless
    /// openRouterPersistenceTest; save the placeholder Meta key unless metaKeyPersistence; remove it when
    /// metaNoKey; remove the OpenRouter key when openRouterUIReset; save the fixture pairing when
    /// savePairingFixture; remove the Keychain copy when dropTestKeychain.
    static func apply(
        _ mode: TestMode,
        preferences: any PreferencesStoring,
        secrets: any SecretStoring,
        pairingStore: any PairingStoring
    ) {
        // STUB(WP-06)
    }
}
