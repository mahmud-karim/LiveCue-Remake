import Foundation
import LiveCueCore

protocol PairingStoring: Sendable {
    /// The Keychain item is the authority; the file is a fallback (architecture 4.10). Keychain item
    /// first: when it parses, that is the pairing, and a file that is missing or holds anything else is
    /// rewritten from it. The file is read only when the Keychain has no item, cannot be read, or holds
    /// text that does not parse. The Keychain is never written from the file. Text that does not pass
    /// `PairingPayload.parse` is treated as absent.
    func load() -> PairingPayload?
    /// Keychain item first. When that write succeeds, the file is rewritten best effort (atomically,
    /// protected until first user authentication, excluded from backup). When it fails, the file must
    /// be written, and an older Keychain item is then deleted so that it cannot win at the next load;
    /// if such an item can be neither replaced nor deleted, the file is put back to what the Keychain
    /// holds and the call throws. After a `save` that returned, `load()` returns that pairing.
    func save(_ pairing: PairingPayload) throws
    /// Test seam only.
    func removeKeychainCopy()
}
