import Foundation

/// The health reply of a relay that is online.
public struct RelayHealth: Equatable, Sendable {
    public var relayVersion: String

    public init(relayVersion: String) {
        self.relayVersion = relayVersion
    }
}
