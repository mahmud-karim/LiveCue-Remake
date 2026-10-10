import Foundation

enum SecretKind: CaseIterable, Sendable {
    case metaAPIKey, openRouterAPIKey
}

struct SecretStoreFailure: Error, Equatable, Sendable {
    let status: Int32
}

/// API keys. They are stored in the Keychain only.
protocol SecretStoring: Sendable {
    func read(_ kind: SecretKind) -> String?
    /// Throws `SecretStoreFailure`; nothing is stored elsewhere on failure.
    func save(_ value: String, for kind: SecretKind) throws
    /// Throws `SecretStoreFailure`; absent is success.
    func remove(_ kind: SecretKind) throws
}
