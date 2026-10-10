import Foundation
import LiveCueCore

/// The pairing in a protected file, with a best-effort copy in the Keychain.
struct FilePairingStore: PairingStoring {
    private let keychain: KeychainStore
    private let names: StorageNames.Names
    private let directory: URL?

    /// `directory` nil means Application Support.
    init(keychain: KeychainStore = KeychainStore(), names: StorageNames.Names, directory: URL? = nil) {
        self.keychain = keychain
        self.names = names
        self.directory = directory
    }

    func load() -> PairingPayload? {
        // STUB(WP-06)
        nil
    }

    func save(_ pairing: PairingPayload) throws {
        // STUB(WP-06)
        throw CocoaError(.fileWriteUnknown)
    }

    func removeKeychainCopy() {
        // STUB(WP-06)
    }
}
