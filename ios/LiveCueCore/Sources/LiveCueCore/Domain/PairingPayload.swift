import Foundation

/// A verified pairing: where the PC is and the token that proves this phone may talk to it.
///
/// Always valid: it can only be made by `parse` or `manual`.
public struct PairingPayload: Equatable, Sendable {
    /// Canonical form (protocol 4.3).
    public let endpoint: String
    public let token: String
    /// The endpoint, parsed. Its `canonical` text equals `endpoint`.
    public let relayEndpoint: RelayEndpoint

    private init(relayEndpoint: RelayEndpoint, token: String) {
        self.endpoint = relayEndpoint.canonical
        self.token = token
        self.relayEndpoint = relayEndpoint
    }

    /// Rules V-1 to V-4. Throws `PairingPayloadError.invalid`. V-4 is a whole-text check done by a
    /// character loop (exactly 43 characters of the token alphabet), never a pattern ending in `$`: a
    /// token followed by a line feed, a carriage return or any other character is invalid.
    public static func parse(_ text: String) throws -> PairingPayload {
        guard text.utf8.count <= RelayConstants.pairingPayloadBytes,
              let value = JSONValue.parse(text),
              let endpointText = value["endpoint"]?.stringValue,
              let token = value["token"]?.stringValue
        else {
            throw PairingPayloadError.invalid
        }
        return try validated(endpoint: endpointText, token: token)
    }

    /// Manual entry of protocol 4.3: trims the address, lower-cases a leading "https://" written in any
    /// case, applies V-3 and V-4. The token is used exactly as typed and is NOT trimmed: one that ends in
    /// a line break or a space fails V-4.
    public static func manual(address: String, token: String) throws -> PairingPayload {
        let trimmed = address.trimmingCharacters(in: .whitespacesAndNewlines)
        let scheme = Array("https://".utf8)
        let bytes = Array(trimmed.utf8)

        var endpointText = trimmed
        if bytes.count >= scheme.count,
           zip(bytes, scheme).allSatisfy({ asciiLowercased($0) == $1 }) {
            endpointText = "https://" + String(decoding: bytes[scheme.count...], as: UTF8.self)
        }
        return try validated(endpoint: endpointText, token: token)
    }

    /// `{"endpoint":"…","token":"…"}`: members in this order, no whitespace. The stored and QR form.
    ///
    /// Both values are made only of characters that JSON writes as they are, so no escaping is needed.
    public var storedText: String {
        "{\"endpoint\":\"\(endpoint)\",\"token\":\"\(token)\"}"
    }

    /// V-3 and V-4. Nothing is trimmed from the token, here or by either caller.
    private static func validated(endpoint: String, token: String) throws -> PairingPayload {
        guard let relayEndpoint = RelayEndpoint(parsing: endpoint), isValidToken(token) else {
            throw PairingPayloadError.invalid
        }
        return PairingPayload(relayEndpoint: relayEndpoint, token: token)
    }

    /// C-02 as a whole-text check: the text is exactly 43 characters of `[A-Za-z0-9_-]` and nothing
    /// else. A loop over every byte, never a pattern: a token followed by a line feed, a carriage
    /// return, U+2028 or any other character is longer than 43 bytes or holds a byte outside the
    /// alphabet, and is invalid.
    private static func isValidToken(_ token: String) -> Bool {
        let bytes = Array(token.utf8)
        guard bytes.count == RelayConstants.tokenLength else { return false }
        for byte in bytes {
            switch byte {
            case UInt8(ascii: "A")...UInt8(ascii: "Z"),
                 UInt8(ascii: "a")...UInt8(ascii: "z"),
                 UInt8(ascii: "0")...UInt8(ascii: "9"),
                 UInt8(ascii: "_"),
                 UInt8(ascii: "-"):
                continue
            default:
                return false
            }
        }
        return true
    }

    private static func asciiLowercased(_ byte: UInt8) -> UInt8 {
        (UInt8(ascii: "A")...UInt8(ascii: "Z")).contains(byte) ? byte + 32 : byte
    }
}

public enum PairingPayloadError: LocalizedError, Equatable, Sendable {
    case invalid

    /// The `PAIRING_PAYLOAD_INVALID` text of protocol 14.2.
    public var errorDescription: String? {
        "Use the pairing QR from LiveCue Desktop. It must contain an HTTPS PC address and a valid pairing token."
    }
}
