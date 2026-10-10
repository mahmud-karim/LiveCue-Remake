import Foundation

/// API keys in the Keychain, under the accounts of `names`.
///
/// A key is stored nowhere else. When the Keychain refuses, the call throws and nothing was saved.
struct KeychainSecretStore: SecretStoring {
    private let keychain: KeychainStore
    private let names: StorageNames.Names

    init(keychain: KeychainStore = KeychainStore(), names: StorageNames.Names) {
        self.keychain = keychain
        self.names = names
    }

    func read(_ kind: SecretKind) -> String? {
        keychain.get(account: account(for: kind))
    }

    func save(_ value: String, for kind: SecretKind) throws {
        do {
            try keychain.set(value, account: account(for: kind))
        } catch let failure as KeychainFailure {
            throw SecretStoreFailure(status: failure.status)
        }
    }

    func remove(_ kind: SecretKind) throws {
        do {
            try keychain.delete(account: account(for: kind))
        } catch let failure as KeychainFailure {
            throw SecretStoreFailure(status: failure.status)
        }
    }

    private func account(for kind: SecretKind) -> String {
        switch kind {
        case .metaAPIKey: return names.metaKeyAccount
        case .openRouterAPIKey: return names.openRouterKeyAccount
        }
    }
}
