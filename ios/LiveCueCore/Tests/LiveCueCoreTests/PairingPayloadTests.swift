import Foundation
import LiveCueCore
import XCTest

/// AC-CORE-004 to 008 with a 43-character token, P2-202, the manual entry of P2-203, and the stored form.
final class PairingPayloadTests: XCTestCase {
    private let token = CoreFixtures.token

    private func payload(endpoint: String, token: String? = nil) -> String {
        "{\"endpoint\":\"\(endpoint)\",\"token\":\"\(token ?? self.token)\"}"
    }

    private func assertRejected(_ text: String, _ reason: String, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertThrowsError(try PairingPayload.parse(text), reason, file: file, line: line) { error in
            XCTAssertEqual(error as? PairingPayloadError, .invalid, reason, file: file, line: line)
        }
    }

    // MARK: - Accepted

    func testAcceptsThePayloadOfTheSpec() throws {
        // AC-CORE-004
        let pairing = try PairingPayload.parse(payload(endpoint: "https://pc.example.test/"))
        XCTAssertEqual(pairing.endpoint, "https://pc.example.test/")
        XCTAssertEqual(pairing.token, token)
        XCTAssertEqual(pairing.relayEndpoint.canonical, "https://pc.example.test/")
    }

    func testAcceptsBothEndpointsOfP2_200() throws {
        for endpoint in ["https://pc.example.test:11443/", "https://pc.example.test:10000/livecue/"] {
            let pairing = try PairingPayload.parse(payload(endpoint: endpoint))
            XCTAssertEqual(pairing.endpoint, endpoint)
        }
    }

    func testIgnoresUnknownMembersAndWhitespaceBetweenThem() throws {
        let text = """
        { "version": 9, "endpoint": "https://pc.example.test/", "name": "desk", "token": "\(token)", "extra": {"a": [1, true, null]} }
        """
        let pairing = try PairingPayload.parse(text)
        XCTAssertEqual(pairing.endpoint, "https://pc.example.test/")
        XCTAssertEqual(pairing.token, token)
    }

    func testStoresTheCanonicalEndpoint() throws {
        let pairing = try PairingPayload.parse(payload(endpoint: "https://PC.Example.Test:10000/livecue"))
        XCTAssertEqual(pairing.endpoint, "https://pc.example.test:10000/livecue/")
    }

    func testAcceptsEveryCharacterOfTheTokenShape() throws {
        // Built from the synthetic token, one character changed at a time, so that no other token-shaped
        // text is written in this repository.
        for character in ["a", "m", "z", "B", "Z", "0", "5", "9", "_", "-"] {
            let token = String(repeating: "A", count: 42) + character
            XCTAssertEqual(token.count, RelayConstants.tokenLength)
            let pairing = try PairingPayload.parse(payload(endpoint: "https://pc.example.test/", token: token))
            XCTAssertEqual(pairing.token, token, character)

            let leading = character + String(repeating: "A", count: 42)
            XCTAssertEqual(try PairingPayload.parse(payload(endpoint: "https://pc.example.test/", token: leading)).token, leading)
        }
    }

    // MARK: - Rejected (P2-202)

    func testRejectsEndpointsOutsideTheGrammar() {
        let rejected: [(endpoint: String, reason: String)] = [
            ("http://pc.example.test/", "plain http (AC-CORE-005)"),
            ("HTTPS://pc.example.test/", "upper-case scheme inside a payload"),
            ("https://user:pass@pc.example.test/", "user name and password (AC-CORE-006)"),
            ("https://user@pc.example.test/", "user name"),
            ("https://pc.example.test/?a=1", "query"),
            ("https://pc.example.test/livecue?a=1", "query after a segment"),
            ("https://pc.example.test/#top", "fragment"),
            ("https://pc.example.test/live%20cue/", "percent-encoding"),
            ("https://pc.example.test//", "empty segment"),
            ("https://pc.example.test/a//b/", "empty segment between segments"),
            ("https://pc.example.test/../", "dot-dot segment"),
            ("https://pc.example.test/a/../b/", "dot-dot segment between segments"),
            ("https://pc.example.test/./", "dot segment"),
            ("https://pc.example.test/a/b/c/d/e/", "five segments"),
            ("https://pc.example.test:0/", "port 0"),
            ("https://pc.example.test:65536/", "port 65536"),
            ("https://pc.example.test:0443/", "port with a leading zero"),
            ("https://pc.example.test:/", "empty port"),
            ("https://pc.example.test:44a/", "port with a letter"),
            ("https:///", "no host"),
            ("https://", "nothing after the scheme"),
            ("https://[::1]/", "IPv6 literal"),
            ("https://pc_example.test/", "underscore in the host"),
            ("https://-pc.example.test/", "label starting with a hyphen"),
            ("https://pc-.example.test/", "label ending with a hyphen"),
            ("https://pc..example.test/", "empty label"),
            ("https://pc.example.test./", "trailing dot"),
            ("https://pc.example.test/caf\u{E9}/", "character outside the segment set"),
            (" https://pc.example.test/", "leading space inside a payload"),
            ("wss://pc.example.test/", "another scheme")
        ]
        for row in rejected {
            assertRejected(payload(endpoint: row.endpoint), row.reason)
        }
    }

    func testRejectsTokensOutsideTheShape() {
        let endpoint = "https://pc.example.test/"
        assertRejected(payload(endpoint: endpoint, token: String(repeating: "A", count: 42)), "42 characters")
        assertRejected(payload(endpoint: endpoint, token: String(repeating: "A", count: 44)), "44 characters")
        assertRejected(payload(endpoint: endpoint, token: String(repeating: "A", count: 42) + "+"), "a plus sign")
        assertRejected(payload(endpoint: endpoint, token: String(repeating: "A", count: 42) + " "), "a space")
        assertRejected(payload(endpoint: endpoint, token: String(repeating: "A", count: 42) + "/"), "a slash")
        assertRejected(payload(endpoint: endpoint, token: String(repeating: "A", count: 42) + "="), "padding")
        assertRejected(payload(endpoint: endpoint, token: String(repeating: "\u{E9}", count: 43)), "43 letters outside ASCII")
        assertRejected(payload(endpoint: endpoint, token: "short"), "a short token (AC-CORE-008)")
        assertRejected(payload(endpoint: endpoint, token: ""), "an empty token")
    }

    func testRejectsATokenFollowedByALineBreak() {
        // ST-13: V-4 is a whole-text check. The escapes are JSON escapes, so the decoded token holds the
        // character itself next to 43 valid ones. One row carries U+2028 unescaped, which JSON allows.
        let endpoint = "https://pc.example.test/"
        assertRejected(payload(endpoint: endpoint, token: token + "\\n"), "a line feed after the token")
        assertRejected(payload(endpoint: endpoint, token: token + "\\r"), "a carriage return after the token")
        assertRejected(payload(endpoint: endpoint, token: token + "\\r\\n"), "a carriage return and line feed after the token")
        assertRejected(payload(endpoint: endpoint, token: token + "\\u2028"), "an escaped U+2028 after the token")
        assertRejected(payload(endpoint: endpoint, token: token + "\u{2028}"), "U+2028 after the token")
        assertRejected(payload(endpoint: endpoint, token: "\\n" + token), "a line feed before the token")
    }

    func testRejectsTextThatIsNotAPairingObject() {
        assertRejected("not a pairing QR", "plain text (AC-CORE-007)")
        assertRejected("", "empty text")
        assertRejected("[\"https://pc.example.test/\",\"\(token)\"]", "an array")
        assertRejected("\"https://pc.example.test/\"", "a string")
        assertRejected("{\"endpoint\":\"https://pc.example.test/\"}", "no token")
        assertRejected("{\"token\":\"\(token)\"}", "no endpoint")
        assertRejected("{\"endpoint\":7,\"token\":\"\(token)\"}", "an endpoint that is not a string")
        assertRejected("{\"endpoint\":\"https://pc.example.test/\",\"token\":null}", "a token that is not a string")
        assertRejected("{\"endpoint\":\"https://pc.example.test/\",\"token\":\"\(token)\"", "an unfinished object")
    }

    func testRejectsTextLongerThanTheLimit() throws {
        // V-1: exactly C-18 bytes is accepted, one more is not. The padding is an unknown member.
        let head = "{\"endpoint\":\"https://pc.example.test/\",\"token\":\"\(token)\",\"pad\":\""
        let tail = "\"}"
        let room = RelayConstants.pairingPayloadBytes - head.utf8.count - tail.utf8.count

        let atLimit = head + String(repeating: "x", count: room) + tail
        XCTAssertEqual(atLimit.utf8.count, RelayConstants.pairingPayloadBytes)
        XCTAssertNoThrow(try PairingPayload.parse(atLimit))

        let overLimit = head + String(repeating: "x", count: room + 1) + tail
        XCTAssertEqual(overLimit.utf8.count, RelayConstants.pairingPayloadBytes + 1)
        assertRejected(overLimit, "one byte over C-18")
    }

    func testTheErrorTextIsTheOneOfProtocol14() {
        XCTAssertEqual(
            PairingPayloadError.invalid.errorDescription,
            "Use the pairing QR from LiveCue Desktop. It must contain an HTTPS PC address and a valid pairing token."
        )
    }

    // MARK: - Manual entry (P2-203)

    func testManualEntryTrimsAndLowerCasesTheScheme() throws {
        let pairing = try PairingPayload.manual(address: " HTTPS://pc.example.test:11443 ", token: token)
        XCTAssertEqual(pairing.endpoint, "https://pc.example.test:11443/")
        XCTAssertEqual(pairing.token, token)

        let mixed = try PairingPayload.manual(address: "\n\tHttps://PC.Example.Test/LiveCue\n", token: token)
        XCTAssertEqual(mixed.endpoint, "https://pc.example.test/LiveCue/")
    }

    func testManualEntryStillAppliesTheGrammarAndTheTokenShape() {
        let invalid: [(address: String, token: String, reason: String)] = [
            ("pc.example.test", token, "no scheme"),
            ("http://pc.example.test/", token, "plain http"),
            ("https://pc.example.test/?a=1", token, "query"),
            ("https://pc example.test/", token, "space inside the address"),
            ("", token, "empty address"),
            ("https://pc.example.test/", " " + String(token.dropFirst()), "token with a space: it is not trimmed"),
            ("https://pc.example.test/", String(token.dropLast()), "short token"),
            ("https://pc.example.test/", token + " ", "token followed by a space: it is not trimmed"),
            ("https://pc.example.test/", token + "\n", "token followed by a line feed (ST-13)"),
            ("https://pc.example.test/", token + "\r", "token followed by a carriage return (ST-13)"),
            ("https://pc.example.test/", token + "\r\n", "token followed by a carriage return and line feed (ST-13)"),
            ("https://pc.example.test/", token + "\u{2028}", "token followed by U+2028 (ST-13)"),
            ("https://pc.example.test/", "\n" + token, "token after a line feed")
        ]
        for row in invalid {
            XCTAssertThrowsError(try PairingPayload.manual(address: row.address, token: row.token), row.reason) { error in
                XCTAssertEqual(error as? PairingPayloadError, .invalid, row.reason)
            }
        }
    }

    // MARK: - Stored form

    func testStoredTextHasTheMembersInOrderAndNoWhitespace() throws {
        let pairing = try PairingPayload.manual(address: "https://pc.example.test:11443/", token: token)
        XCTAssertEqual(
            pairing.storedText,
            "{\"endpoint\":\"https://pc.example.test:11443/\",\"token\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}"
        )
    }

    func testStoredTextRoundTripsThroughParse() throws {
        for endpoint in ["https://pc.example.test/", "https://pc.example.test:10000/livecue/", "https://a.b-c.test:1/x/y_z/~w/1.2/"] {
            let pairing = try PairingPayload.manual(address: endpoint, token: token)
            XCTAssertEqual(try PairingPayload.parse(pairing.storedText), pairing)
        }
    }
}
