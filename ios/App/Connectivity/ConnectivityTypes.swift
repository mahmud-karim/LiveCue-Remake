import Foundation

enum SavedPCState: Equatable, Sendable {
    case connected, reconnecting, needsAttention
}

/// A message line on the screen that started an action.
struct InlineMessage: Equatable, Sendable {
    let text: String
    let isError: Bool

    init(_ text: String, isError: Bool = false) {
        self.text = text
        self.isError = isError
    }
}

/// Added by the review: pairing with another host while a PC is saved needs a confirmation
/// (architecture 5.9). Both hosts are lower case, as `RelayEndpoint.host` gives them.
struct PairingReplacement: Equatable, Sendable {
    let currentHost: String
    let newHost: String

    /// `newHost` ends in ".ts.net".
    var newHostIsTailscaleName: Bool {
        newHost.hasSuffix(".ts.net")
    }
}
