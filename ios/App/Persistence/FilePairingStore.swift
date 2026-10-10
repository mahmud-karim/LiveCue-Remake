import Foundation
import LiveCueCore

/// The pairing: the Keychain item is the authority, and a protected file is the fallback (architecture
/// 4.10).
///
/// Other apps in the same LiveContainer can read and replace the file, so a file must never be able to
/// send the app to another server: it is read only when the Keychain gives no pairing, and the Keychain
/// is never written from it. The file exists because Keychain access inside LiveContainer can fail.
struct FilePairingStore: PairingStoring {
    /// The file is replaced in one step and protected until the first unlock after a restart.
    static let fileWritingOptions: Data.WritingOptions = [.atomic, .completeFileProtectionUntilFirstUserAuthentication]

    private let keychain: PairingKeychainAccess
    private let names: StorageNames.Names
    private let directory: URL?

    /// `directory` nil means Application Support.
    init(keychain: KeychainStore = KeychainStore(), names: StorageNames.Names, directory: URL? = nil) {
        self.init(
            access: PairingKeychainAccess(keychain: keychain, account: names.pairingAccount),
            names: names,
            directory: directory
        )
    }

    /// For unit tests: the same store over Keychain calls that a test can make fail.
    init(access: PairingKeychainAccess, names: StorageNames.Names, directory: URL?) {
        self.keychain = access
        self.names = names
        self.directory = directory
    }

    func load() -> PairingPayload? {
        if let pairing = keychainPairing() {
            // The authority. A file that is missing or holds anything else is rewritten from it.
            if readFile() != pairing.storedText {
                try? writeFile(pairing.storedText)
            }
            return pairing
        }
        // No item, an item that cannot be read, or text that is not a pairing: the fallback. What it
        // holds is used as it is and never copied into the Keychain.
        return filePairing()
    }

    func save(_ pairing: PairingPayload) throws {
        let text = pairing.storedText

        let refusal: any Error
        do {
            try keychain.write(text)
            // The Keychain holds it. The fallback copy is best effort; `load()` rewrites it otherwise.
            try? writeFile(text)
            return
        } catch {
            refusal = error
        }

        // The Keychain refused, so the file must hold the pairing.
        try writeFile(text)
        // An older item would win at the next load.
        try? keychain.delete()
        if let held = keychainPairing(), held != pairing {
            // It could be neither replaced nor deleted, and it is still the authority: the file goes
            // back to what the Keychain holds, and the save failed.
            try? writeFile(held.storedText)
            throw refusal
        }
    }

    func removeKeychainCopy() {
        try? keychain.delete()
    }

    // MARK: - Reading

    /// The Keychain item as a pairing. Nil without an item, when it cannot be read, and for text that
    /// does not pass `PairingPayload.parse`.
    private func keychainPairing() -> PairingPayload? {
        guard let text = keychain.read() else { return nil }
        return try? PairingPayload.parse(text)
    }

    private func filePairing() -> PairingPayload? {
        guard let text = readFile() else { return nil }
        return try? PairingPayload.parse(text)
    }

    // MARK: - The file

    private func fileURL() throws -> URL {
        let folder: URL
        if let directory {
            folder = directory
        } else {
            folder = try FileManager.default.url(
                for: .applicationSupportDirectory,
                in: .userDomainMask,
                appropriateFor: nil,
                create: true
            )
        }
        return folder.appendingPathComponent(names.pairingFileName, isDirectory: false)
    }

    /// The file's text. Nil without a file, and for a file larger than a pairing can be: another app
    /// can put anything there, and nothing that large is read into memory.
    private func readFile() -> String? {
        guard let url = try? fileURL(),
              let size = (try? url.resourceValues(forKeys: [.fileSizeKey]))?.fileSize,
              size <= RelayConstants.pairingPayloadBytes,
              let data = try? Data(contentsOf: url)
        else { return nil }
        return String(data: data, encoding: .utf8)
    }

    /// Atomically, protected until first user authentication, excluded from backup. The exclusion is set
    /// after every write, because an atomic write puts a new file in the old one's place.
    private func writeFile(_ text: String) throws {
        let url = try fileURL()
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(text.utf8).write(to: url, options: FilePairingStore.fileWritingOptions)
        BackupExclusion.exclude(url)
    }
}

/// The three Keychain calls the pairing store makes, on the one item that holds the pairing.
struct PairingKeychainAccess: Sendable {
    /// The item's text. Nil when there is no item and when it cannot be read.
    var read: @Sendable () -> String?
    /// Adds the item or replaces it.
    var write: @Sendable (String) throws -> Void
    /// Removes the item. An absent item is not an error.
    var delete: @Sendable () throws -> Void

    init(
        read: @escaping @Sendable () -> String?,
        write: @escaping @Sendable (String) throws -> Void,
        delete: @escaping @Sendable () throws -> Void
    ) {
        self.read = read
        self.write = write
        self.delete = delete
    }

    /// The calls of a real Keychain item.
    init(keychain: KeychainStore, account: String) {
        self.init(
            read: { keychain.get(account: account) },
            write: { text in try keychain.set(text, account: account) },
            delete: { try keychain.delete(account: account) }
        )
    }
}
