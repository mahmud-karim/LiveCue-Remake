import Foundation

/// The advertised endpoint of a relay, in canonical form (protocol 4.3).
public struct RelayEndpoint: Equatable, Sendable {
    /// "https://" + lower-case host + optional ":port" + path ending in exactly one "/".
    public let canonical: String

    /// The grammar of protocol 4.3, parsed by hand. No trimming and no case change of the scheme.
    ///
    ///     endpoint = "https://" host [ ":" port ] [ path ]
    ///     host     = label *( "." label )            ; total length 1..253
    ///     label    = 1..63 of [A-Za-z0-9-], not starting or ending with "-"
    ///     port     = decimal 1..65535, no leading zero
    ///     path     = "/" [ segment *( "/" segment ) [ "/" ] ]     ; at most 4 segments
    ///     segment  = 1..64 of [A-Za-z0-9._~-], and not "." and not ".."
    public init?(parsing text: String) {
        let bytes = Array(text.utf8)
        let scheme = Array(RelayEndpoint.scheme.utf8)
        guard bytes.count > scheme.count, bytes.starts(with: scheme) else { return nil }
        let rest = bytes[scheme.count...]

        let pathStart = rest.firstIndex(of: Byte.slash) ?? rest.endIndex
        let authority = rest[rest.startIndex..<pathStart]
        let path = rest[pathStart...]

        let hostBytes: ArraySlice<UInt8>
        var portSuffix = ""
        if let colon = authority.firstIndex(of: Byte.colon) {
            hostBytes = authority[authority.startIndex..<colon]
            guard let port = RelayEndpoint.validPort(authority[authority.index(after: colon)...]) else { return nil }
            portSuffix = ":" + port
        } else {
            hostBytes = authority
        }

        guard let host = RelayEndpoint.canonicalHost(hostBytes),
              let canonicalPath = RelayEndpoint.canonicalPath(path)
        else { return nil }

        canonical = RelayEndpoint.scheme + host + portSuffix + canonicalPath
    }

    /// `canonical` + the route path without its leading slash. Text concatenation only.
    public func url(for route: RelayRoute) -> URL? {
        URL(string: canonical + String(route.path.dropFirst()))
    }

    /// The speech URL: as above with the scheme "wss".
    public func speechURL() -> URL? {
        let withoutScheme = String(canonical.dropFirst(RelayEndpoint.scheme.count))
        return URL(string: "wss://" + withoutScheme + String(RelayRoute.speech.path.dropFirst()))
    }

    // MARK: - Grammar

    private static let scheme = "https://"

    private enum Byte {
        static let slash = UInt8(ascii: "/")
        static let colon = UInt8(ascii: ":")
        static let dot = UInt8(ascii: ".")
        static let hyphen = UInt8(ascii: "-")
        static let zero = UInt8(ascii: "0")

        static func isDigit(_ byte: UInt8) -> Bool {
            (UInt8(ascii: "0")...UInt8(ascii: "9")).contains(byte)
        }

        static func isLetter(_ byte: UInt8) -> Bool {
            (UInt8(ascii: "A")...UInt8(ascii: "Z")).contains(byte) || (UInt8(ascii: "a")...UInt8(ascii: "z")).contains(byte)
        }

        static func isLabelByte(_ byte: UInt8) -> Bool {
            isLetter(byte) || isDigit(byte) || byte == hyphen
        }

        static func isSegmentByte(_ byte: UInt8) -> Bool {
            isLabelByte(byte) || byte == dot || byte == UInt8(ascii: "_") || byte == UInt8(ascii: "~")
        }

        static func lowercased(_ byte: UInt8) -> UInt8 {
            (UInt8(ascii: "A")...UInt8(ascii: "Z")).contains(byte) ? byte + 32 : byte
        }
    }

    /// The host in lower case, or nil when it does not match the grammar.
    private static func canonicalHost(_ bytes: ArraySlice<UInt8>) -> String? {
        guard (1...253).contains(bytes.count) else { return nil }
        let labels = bytes.split(separator: Byte.dot, omittingEmptySubsequences: false)
        for label in labels {
            guard (1...63).contains(label.count),
                  label.allSatisfy(Byte.isLabelByte),
                  label.first != Byte.hyphen,
                  label.last != Byte.hyphen
            else { return nil }
        }
        return String(decoding: bytes.map(Byte.lowercased), as: UTF8.self)
    }

    /// The port as written, or nil when it is not a decimal from 1 to 65535 without a leading zero.
    private static func validPort(_ bytes: ArraySlice<UInt8>) -> String? {
        guard (1...5).contains(bytes.count),
              bytes.allSatisfy(Byte.isDigit),
              bytes.first != Byte.zero
        else { return nil }
        let text = String(decoding: bytes, as: UTF8.self)
        guard let value = Int(text), (1...65_535).contains(value) else { return nil }
        return text
    }

    /// The path with exactly one trailing slash, or nil when it does not match the grammar.
    /// An absent path becomes "/".
    private static func canonicalPath(_ bytes: ArraySlice<UInt8>) -> String? {
        // `bytes` is empty or starts with the slash that ended the authority.
        var body = bytes.dropFirst()
        guard !body.isEmpty else { return "/" }
        if body.last == Byte.slash {
            body = body.dropLast()
        }

        // One trailing slash is gone; any slash pair left is an empty segment.
        let segments = body.split(separator: Byte.slash, omittingEmptySubsequences: false)
        guard (1...4).contains(segments.count) else { return nil }
        for segment in segments {
            guard (1...64).contains(segment.count), segment.allSatisfy(Byte.isSegmentByte) else { return nil }
            if segment.allSatisfy({ $0 == Byte.dot }), segment.count <= 2 { return nil }
        }
        let joined = segments.map { String(decoding: $0, as: UTF8.self) }.joined(separator: "/")
        return "/" + joined + "/"
    }
}
