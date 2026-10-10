import Foundation

/// The phone's own text for every relay code and local condition (protocol 14).
public enum RelayMessages {
    /// Protocol 14.1 and 14.2. A known code or condition gives its phone text. An unknown code gives
    /// `relayText` when it is 1 to 300 characters, otherwise the L-HTTP text (origin http) or the
    /// L-STREAM-DROPPED text (origin stream). No result ever contains a token.
    public static func text(for failure: RelayFailure) -> String {
        // STUB(WP-03)
        ""
    }

    public static func text(for code: RelayWireCode) -> String {
        // STUB(WP-03)
        ""
    }

    public static func text(for condition: RelayLocalCondition, httpStatus: Int? = nil) -> String {
        // STUB(WP-03)
        ""
    }

    // MARK: - Protocol 14.3 compositions

    /// "Transcription could not start. " + message
    public static func startFailure(_ message: String) -> String {
        // STUB(WP-03)
        message
    }

    /// "Your pairing is saved. " + text for L-TLS, L-UNREACHABLE, L-NETWORK, L-TIMEOUT, L-NO-RELAY; the
    /// bare text otherwise.
    public static func savedPairingHealthFailure(_ failure: RelayFailure) -> String {
        // STUB(WP-03)
        text(for: failure)
    }

    /// "Could not load models. Restart the updated PC app and check the connection. " + text
    public static func catalogueFailure(_ failure: RelayFailure) -> String {
        // STUB(WP-03)
        text(for: failure)
    }

    /// "Could not check the PC model. Keep the updated LiveCue Desktop running. " + text
    public static func pcModelCheckFailure(_ failure: RelayFailure) -> String {
        // STUB(WP-03)
        text(for: failure)
    }
}
