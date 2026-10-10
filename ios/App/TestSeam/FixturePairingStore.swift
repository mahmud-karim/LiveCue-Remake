import Foundation
import LiveCueCore

/// "Always paired": the fixture pairing, whatever was saved. Saving and removing do nothing.
struct FixturePairingStore: PairingStoring {
    init() {}

    func load() -> PairingPayload? {
        FixtureValues.pairing
    }

    func save(_ pairing: PairingPayload) throws {}

    func removeKeychainCopy() {}
}
