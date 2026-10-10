import Foundation
import LiveCueCore

protocol PairingStoring: Sendable {
    /// File first (refreshing the Keychain copy from it), then the Keychain copy. Text that does not
    /// pass `PairingPayload.parse` is treated as absent.
    func load() -> PairingPayload?
    /// File first, atomically, protected until first user authentication, excluded from backup; it must
    /// succeed. The Keychain copy is best effort.
    func save(_ pairing: PairingPayload) throws
    /// Test seam only.
    func removeKeychainCopy()
}
