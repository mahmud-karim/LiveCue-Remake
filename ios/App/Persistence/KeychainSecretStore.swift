import Foundation

/// API keys in the Keychain, under the accounts of `names`.
struct KeychainSecretStore: SecretStoring {
    private let keychain: KeychainStore
    private let names: StorageNames.Names

    init(keychain: KeychainStore = KeychainStore(), names: StorageNames.Names) {
        self.keychain = keychain
        self.names = names
    }

    func read(_ kind: SecretKind) -> String? {
        // STUB(WP-06)
        nil
    }

    func save(_ value: String, for kind: SecretKind) throws {
        // STUB(WP-06)
        // -4 is errSecUnimplemented.
        throw SecretStoreFailure(status: -4)
    }

    func remove(_ kind: SecretKind) throws {
        // STUB(WP-06)
        throw SecretStoreFailure(status: -4)
    }
}
