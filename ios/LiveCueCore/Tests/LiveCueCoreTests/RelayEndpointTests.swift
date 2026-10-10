import Foundation
import LiveCueCore
import XCTest

/// P2-200 for both endpoints and all ten routes, the canonical forms of P2-203, and every rejection of
/// the grammar in protocol 4.3.
final class RelayEndpointTests: XCTestCase {
    private let requestID = "6F9619FF-8B86-4011-B42D-00C04FC964FF"

    private var httpRoutes: [RelayRoute] {
        [
            .health, .pairVerify, .models, .localModel, .localModelStart, .localModelStop,
            .assist, .sessionSummary, .cancel(requestId: requestID)
        ]
    }

    // MARK: - URLs (P2-200)

    func testEveryRouteURLIsTheEndpointPlusTheRoute() throws {
        for text in ["https://pc.example.test:11443/", "https://pc.example.test:10000/livecue/"] {
            let endpoint = try XCTUnwrap(RelayEndpoint(parsing: text))
            XCTAssertEqual(endpoint.canonical, text)

            for route in httpRoutes + [.speech] {
                let url = try XCTUnwrap(endpoint.url(for: route), route.path)
                XCTAssertEqual(url.absoluteString, text + String(route.path.dropFirst()))
                XCTAssertNil(url.query, route.path)
                XCTAssertNil(url.fragment, route.path)
                XCTAssertEqual(url.scheme, "https")
            }
        }
    }

    func testRouteURLsSpelledOut() throws {
        let direct = try XCTUnwrap(RelayEndpoint(parsing: "https://pc.example.test:11443/"))
        XCTAssertEqual(direct.url(for: .health)?.absoluteString, "https://pc.example.test:11443/v1/health")
        XCTAssertEqual(direct.url(for: .pairVerify)?.absoluteString, "https://pc.example.test:11443/v1/pair/verify")
        XCTAssertEqual(
            direct.url(for: .cancel(requestId: requestID))?.absoluteString,
            "https://pc.example.test:11443/v1/requests/6F9619FF-8B86-4011-B42D-00C04FC964FF"
        )

        let mounted = try XCTUnwrap(RelayEndpoint(parsing: "https://pc.example.test:10000/livecue/"))
        XCTAssertEqual(mounted.url(for: .health)?.absoluteString, "https://pc.example.test:10000/livecue/v1/health")
        XCTAssertEqual(mounted.url(for: .localModelStart)?.absoluteString, "https://pc.example.test:10000/livecue/v1/local-model/start")
        XCTAssertEqual(mounted.url(for: .sessionSummary)?.absoluteString, "https://pc.example.test:10000/livecue/v1/session-summary")
    }

    func testSpeechURLIsTheSameWithTheSchemeWss() throws {
        let direct = try XCTUnwrap(RelayEndpoint(parsing: "https://pc.example.test:11443/"))
        XCTAssertEqual(direct.speechURL()?.absoluteString, "wss://pc.example.test:11443/v1/speech")

        let mounted = try XCTUnwrap(RelayEndpoint(parsing: "https://pc.example.test:10000/livecue/"))
        let url = try XCTUnwrap(mounted.speechURL())
        XCTAssertEqual(url.absoluteString, "wss://pc.example.test:10000/livecue/v1/speech")
        XCTAssertNil(url.query)
        XCTAssertNil(url.fragment)
        XCTAssertEqual(url.scheme, "wss")
    }

    // MARK: - Canonical form (P2-203)

    func testCanonicalForms() {
        let rows: [(text: String, canonical: String)] = [
            ("https://PC.Example.Test", "https://pc.example.test/"),
            ("https://pc.example.test", "https://pc.example.test/"),
            ("https://pc.example.test/", "https://pc.example.test/"),
            ("https://pc.example.test:10000/livecue", "https://pc.example.test:10000/livecue/"),
            ("https://pc.example.test:10000/livecue/", "https://pc.example.test:10000/livecue/"),
            ("https://pc.example.test:11443", "https://pc.example.test:11443/"),
            ("https://pc.example.test/A/b", "https://pc.example.test/A/b/"),
            ("https://pc.example.test/a/b/c/d", "https://pc.example.test/a/b/c/d/"),
            ("https://pc.example.test/a.b/c_d/e~f/g-h/", "https://pc.example.test/a.b/c_d/e~f/g-h/"),
            ("https://pc.example.test/.../", "https://pc.example.test/.../"),
            ("https://PC-1.Example.TEST:1/", "https://pc-1.example.test:1/"),
            ("https://pc.example.test:65535/", "https://pc.example.test:65535/"),
            ("https://192.0.2.7:11443/", "https://192.0.2.7:11443/"),
            ("https://localhost/", "https://localhost/")
        ]
        for row in rows {
            XCTAssertEqual(RelayEndpoint(parsing: row.text)?.canonical, row.canonical, row.text)
        }
    }

    func testEqualEndpointsHaveEqualCanonicalForms() {
        XCTAssertEqual(RelayEndpoint(parsing: "https://PC.example.test"), RelayEndpoint(parsing: "https://pc.example.test/"))
        XCTAssertNotEqual(RelayEndpoint(parsing: "https://pc.example.test/a"), RelayEndpoint(parsing: "https://pc.example.test/A"))
    }

    // MARK: - Rejections (protocol 4.3)

    func testRejectsEverythingOutsideTheGrammar() {
        let rejected: [(text: String, reason: String)] = [
            ("", "empty"),
            ("https://", "nothing after the scheme"),
            ("https:///", "no host"),
            ("https:///livecue/", "no host before a path"),
            ("http://pc.example.test/", "plain http"),
            ("HTTPS://pc.example.test/", "upper-case scheme"),
            ("Https://pc.example.test/", "mixed-case scheme"),
            ("wss://pc.example.test/", "another scheme"),
            ("pc.example.test", "no scheme"),
            (" https://pc.example.test/", "leading space: nothing is trimmed"),
            ("https://pc.example.test/ ", "trailing space: nothing is trimmed"),
            ("https://user@pc.example.test/", "user name"),
            ("https://user:pass@pc.example.test/", "user name and password"),
            ("https://pc.example.test/?a=1", "query"),
            ("https://pc.example.test?a=1", "query without a path"),
            ("https://pc.example.test/#top", "fragment"),
            ("https://pc.example.test#top", "fragment without a path"),
            ("https://pc.example.test/a%20b/", "percent-encoding"),
            ("https://pc.example.test//", "empty segment"),
            ("https://pc.example.test/a//", "two trailing slashes"),
            ("https://pc.example.test/a//b", "empty segment between segments"),
            ("https://pc.example.test/./", "dot segment"),
            ("https://pc.example.test/../", "dot-dot segment"),
            ("https://pc.example.test/a/..", "dot-dot as the last segment"),
            ("https://pc.example.test/a/b/c/d/e", "five segments"),
            ("https://pc.example.test/a/b/c/d/e/", "five segments and a slash"),
            ("https://pc.example.test/" + String(repeating: "s", count: 65) + "/", "segment of 65 characters"),
            ("https://pc.example.test/a b/", "space in a segment"),
            ("https://pc.example.test/a:b/", "colon in a segment"),
            ("https://pc.example.test/a@b/", "at sign in a segment"),
            ("https://pc.example.test/caf\u{E9}/", "letter outside ASCII in a segment"),
            ("https://pc.example.test\\livecue", "backslash"),
            ("https://pc.example.test:0/", "port 0"),
            ("https://pc.example.test:65536/", "port 65536"),
            ("https://pc.example.test:0443/", "port with a leading zero"),
            ("https://pc.example.test:/", "empty port"),
            ("https://pc.example.test:", "empty port and no path"),
            ("https://pc.example.test:443:443/", "two ports"),
            ("https://pc.example.test:+443/", "signed port"),
            ("https://pc.example.test:4 43/", "space in the port"),
            ("https://pc.example.test:123456/", "port of six digits"),
            ("https://[::1]/", "IPv6 literal"),
            ("https://[2001:db8::1]:11443/", "IPv6 literal with a port"),
            ("https://pc_example.test/", "underscore in the host"),
            ("https://pc example.test/", "space in the host"),
            ("https://-pc.example.test/", "label starting with a hyphen"),
            ("https://pc-.example.test/", "label ending with a hyphen"),
            ("https://pc..example.test/", "empty label"),
            ("https://.pc.example.test/", "leading dot"),
            ("https://pc.example.test./", "trailing dot"),
            ("https://" + String(repeating: "a", count: 64) + ".test/", "label of 64 characters"),
            ("https://b\u{FC}cher.example.test/", "letter outside ASCII in the host")
        ]
        for row in rejected {
            XCTAssertNil(RelayEndpoint(parsing: row.text), row.reason)
        }
    }

    func testLengthLimitsOfHostLabelAndSegment() {
        let label63 = String(repeating: "a", count: 63)
        XCTAssertNotNil(RelayEndpoint(parsing: "https://\(label63).test/"))

        // 63 + 1 + 63 + 1 + 63 + 1 + 61 = 253 characters, the longest host.
        let host253 = [label63, label63, label63, String(repeating: "b", count: 61)].joined(separator: ".")
        XCTAssertEqual(host253.count, 253)
        XCTAssertNotNil(RelayEndpoint(parsing: "https://\(host253)/"))

        let host254 = [label63, label63, label63, String(repeating: "b", count: 62)].joined(separator: ".")
        XCTAssertEqual(host254.count, 254)
        XCTAssertNil(RelayEndpoint(parsing: "https://\(host254)/"))

        let segment64 = String(repeating: "s", count: 64)
        XCTAssertNotNil(RelayEndpoint(parsing: "https://pc.example.test/\(segment64)/"))
    }
}
