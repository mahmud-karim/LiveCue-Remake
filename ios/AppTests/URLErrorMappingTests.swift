import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Each system error code maps to its transport failure.
final class URLErrorMappingTests: XCTestCase {
    @MainActor
    func testCancellationMapsToCancelled() async {
        XCTAssertEqual(URLErrorMapping.transportFailure(for: URLError(.cancelled)), .cancelled)
        XCTAssertEqual(URLErrorMapping.transportFailure(for: CancellationError()), .cancelled)
    }

    @MainActor
    func testATimeOutMapsToTimedOut() async {
        XCTAssertEqual(URLErrorMapping.transportFailure(for: URLError(.timedOut)), .timedOut)
    }

    @MainActor
    func testSecureConnectionAndEveryCertificateCodeMapToTLS() async {
        let codes: [URLError.Code] = [
            .secureConnectionFailed,
            .serverCertificateHasBadDate,
            .serverCertificateUntrusted,
            .serverCertificateHasUnknownRoot,
            .serverCertificateNotYetValid,
            .clientCertificateRejected,
            .clientCertificateRequired
        ]
        for code in codes {
            XCTAssertEqual(URLErrorMapping.transportFailure(for: URLError(code)), .tls, "URL error \(code.rawValue)")
        }
    }

    @MainActor
    func testConnectionProblemsMapToUnreachable() async {
        let codes: [URLError.Code] = [
            .notConnectedToInternet,
            .networkConnectionLost,
            .cannotConnectToHost,
            .cannotFindHost,
            .dnsLookupFailed
        ]
        for code in codes {
            XCTAssertEqual(URLErrorMapping.transportFailure(for: URLError(code)), .unreachable, "URL error \(code.rawValue)")
        }
    }

    @MainActor
    func testEveryOtherURLErrorMapsToOther() async {
        let codes: [URLError.Code] = [
            .unknown,
            .badURL,
            .unsupportedURL,
            .badServerResponse,
            .httpTooManyRedirects,
            .redirectToNonExistentLocation,
            .resourceUnavailable,
            .userCancelledAuthentication,
            .userAuthenticationRequired,
            .zeroByteResource,
            .cannotDecodeRawData,
            .cannotDecodeContentData,
            .cannotParseResponse,
            .appTransportSecurityRequiresSecureConnection,
            .dataLengthExceedsMaximum,
            .internationalRoamingOff,
            .callIsActive,
            .dataNotAllowed,
            .cannotLoadFromNetwork
        ]
        for code in codes {
            XCTAssertEqual(URLErrorMapping.transportFailure(for: URLError(code)), .other, "URL error \(code.rawValue)")
        }
    }

    /// A delegate receives the same errors as `NSError` values of the URL error domain.
    @MainActor
    func testTheSameCodesMapWhenTheyArriveAsNSError() async {
        let expectations: [(Int, TransportFailure)] = [
            (NSURLErrorCancelled, .cancelled),
            (NSURLErrorTimedOut, .timedOut),
            (NSURLErrorSecureConnectionFailed, .tls),
            (NSURLErrorServerCertificateUntrusted, .tls),
            (NSURLErrorClientCertificateRequired, .tls),
            (NSURLErrorNotConnectedToInternet, .unreachable),
            (NSURLErrorNetworkConnectionLost, .unreachable),
            (NSURLErrorCannotConnectToHost, .unreachable),
            (NSURLErrorCannotFindHost, .unreachable),
            (NSURLErrorDNSLookupFailed, .unreachable),
            (NSURLErrorBadServerResponse, .other)
        ]
        for (code, expected) in expectations {
            let error = NSError(domain: NSURLErrorDomain, code: code)
            XCTAssertEqual(URLErrorMapping.transportFailure(for: error), expected, "URL error \(code)")
        }
    }

    @MainActor
    func testErrorsOfOtherDomainsMapToOther() async {
        // The same number as "timed out" and as "cancelled", in other domains.
        XCTAssertEqual(URLErrorMapping.transportFailure(for: NSError(domain: NSPOSIXErrorDomain, code: NSURLErrorTimedOut)), .other)
        XCTAssertEqual(URLErrorMapping.transportFailure(for: NSError(domain: NSCocoaErrorDomain, code: NSURLErrorCancelled)), .other)
        // "Socket is not connected", which is what a closed socket reports.
        XCTAssertEqual(URLErrorMapping.transportFailure(for: NSError(domain: NSPOSIXErrorDomain, code: 57)), .other)
        XCTAssertEqual(URLErrorMapping.transportFailure(for: CocoaError(.fileNoSuchFile)), .other)
        XCTAssertEqual(URLErrorMapping.transportFailure(for: WebSocketUpgradeRejected(status: 401)), .other)
    }

    @MainActor
    func testATransportFailureKeepsItsMeaning() async {
        let failures: [TransportFailure] = [.cancelled, .timedOut, .tls, .unreachable, .other]
        for failure in failures {
            XCTAssertEqual(URLErrorMapping.transportFailure(for: failure), failure)
        }
    }
}

/// The parts of the two URL session transports that can be proven without a network: the private
/// session, the request, the response head, the chunk rule, the refused redirect, and whole requests
/// answered by a URL protocol inside the test.
final class URLSessionTransportTests: XCTestCase {
    private let url = URL(string: "https://pc.example.test:11443/v1/health")!

    private func stubbedTransport() -> URLSessionHTTPTransport {
        URLSessionHTTPTransport(resourceTimeout: nil, protocolClasses: [StubURLProtocol.self])
    }

    private func makeRequest(_ path: String, headers: [String: String] = [:]) -> HTTPRequest {
        HTTPRequest(
            method: "GET",
            url: URL(string: "https://pc.example.test:11443/\(path)")!,
            headers: headers,
            timeout: 5
        )
    }

    // MARK: - Parts

    @MainActor
    func testTheSessionIsEphemeralWithoutCacheOrCookies() async {
        let configuration = URLSessionHTTPTransport.makeConfiguration(resourceTimeout: nil)
        XCTAssertNil(configuration.urlCache)
        XCTAssertNil(configuration.httpCookieStorage)
        XCTAssertFalse(configuration.httpShouldSetCookies)
        XCTAssertEqual(configuration.httpCookieAcceptPolicy, .never)
        XCTAssertEqual(configuration.requestCachePolicy, .reloadIgnoringLocalCacheData)

        let limited = URLSessionHTTPTransport.makeConfiguration(resourceTimeout: 120)
        XCTAssertEqual(limited.timeoutIntervalForResource, 120)
    }

    @MainActor
    func testTheRequestCarriesMethodHeadersBodyAndIdleTimeout() async {
        let request = HTTPRequest(
            method: "POST",
            url: url,
            headers: ["Authorization": "Bearer \(FixtureValues.pairingToken)", "X-LiveCue-Protocol": "2"],
            body: Data("{}".utf8),
            timeout: 25
        )
        let urlRequest = URLSessionHTTPTransport.urlRequest(for: request)

        XCTAssertEqual(urlRequest.url, url)
        XCTAssertEqual(urlRequest.httpMethod, "POST")
        XCTAssertEqual(urlRequest.httpBody, Data("{}".utf8))
        XCTAssertEqual(urlRequest.timeoutInterval, 25)
        XCTAssertEqual(urlRequest.value(forHTTPHeaderField: "Authorization"), "Bearer \(FixtureValues.pairingToken)")
        XCTAssertEqual(urlRequest.value(forHTTPHeaderField: "X-LiveCue-Protocol"), "2")
        XCTAssertNil(urlRequest.value(forHTTPHeaderField: "Origin"))
        XCTAssertNil(urlRequest.value(forHTTPHeaderField: "Cookie"))
        XCTAssertFalse(urlRequest.httpShouldHandleCookies)
        XCTAssertEqual(urlRequest.cachePolicy, .reloadIgnoringLocalCacheData)
    }

    @MainActor
    func testTheResponseHeadHasLowerCasedHeaderNames() async throws {
        let response = try XCTUnwrap(
            HTTPURLResponse(
                url: url,
                statusCode: 429,
                httpVersion: "HTTP/1.1",
                headerFields: ["Content-Type": "application/json", "Retry-After": "3"]
            )
        )
        let head = try XCTUnwrap(URLSessionHTTPTransport.head(of: response))
        XCTAssertEqual(head.status, 429)
        XCTAssertEqual(head.headers, ["content-type": "application/json", "retry-after": "3"])

        let plain = URLResponse(url: url, mimeType: nil, expectedContentLength: 0, textEncodingName: nil)
        XCTAssertNil(URLSessionHTTPTransport.head(of: plain), "Only an HTTP response has a head.")
    }

    @MainActor
    func testTheBodyIsCutAtEveryLineFeedAndAtEvery4096Bytes() async {
        var body = Data("a\nbb\n".utf8)
        body.append(Data(repeating: 0x78, count: 5_000))
        body.append(Data("\ntail".utf8))

        var chunker = HTTPBodyChunker()
        var chunks: [Data] = []
        for byte in body {
            if let chunk = chunker.append(byte) {
                chunks.append(chunk)
            }
        }
        XCTAssertEqual(chunks.map(\.count), [2, 3, 4_096, 905])
        XCTAssertEqual(chunks.first, Data("a\n".utf8))
        XCTAssertEqual(chunks.last?.last, 0x0A)

        XCTAssertEqual(chunker.finish(), Data("tail".utf8))
        XCTAssertNil(chunker.finish(), "Nothing is left after the last chunk.")
        XCTAssertEqual(HTTPBodyChunker.maximumChunkBytes, 4_096)
    }

    @MainActor
    func testARedirectIsAnsweredWithNil() async throws {
        let delegate = RedirectRefusingDelegate()
        // The task is never resumed, so nothing is sent.
        let task = URLSession.shared.dataTask(with: url)
        let response = try XCTUnwrap(
            HTTPURLResponse(
                url: url,
                statusCode: 302,
                httpVersion: "HTTP/1.1",
                headerFields: ["Location": "https://elsewhere.example.test/"]
            )
        )
        let followed = URLRequest(url: try XCTUnwrap(URL(string: "https://elsewhere.example.test/")))

        let answer = await withCheckedContinuation { (continuation: CheckedContinuation<URLRequest?, Never>) in
            delegate.urlSession(
                URLSession.shared,
                task: task,
                willPerformHTTPRedirection: response,
                newRequest: followed,
                completionHandler: { request in continuation.resume(returning: request) }
            )
        }
        task.cancel()
        XCTAssertNil(answer, "A redirect is never followed.")

        // The URL session finds the method by its Objective-C name. A method that only looked like the
        // delegate's would never be asked, and every redirect would be followed.
        let asked = #selector(
            URLSessionTaskDelegate.urlSession(_:task:willPerformHTTPRedirection:newRequest:completionHandler:)
        )
        XCTAssertTrue(delegate.responds(to: asked))
    }

    @MainActor
    func testTheSpeechUpgradeRequestKeepsAnIdleTimeoutOfAMinuteOrMore() async throws {
        let socketURL = try XCTUnwrap(URL(string: "wss://pc.example.test:11443/v1/speech"))
        let request = URLSessionWebSocketTransport.upgradeRequest(
            url: socketURL,
            headers: ["Authorization": "Bearer \(FixtureValues.pairingToken)", "X-LiveCue-Speech-Model": "nemotron"]
        )
        XCTAssertGreaterThanOrEqual(URLSessionWebSocketTransport.idleTimeoutSeconds, 60)
        XCTAssertGreaterThanOrEqual(request.timeoutInterval, 60)
        XCTAssertEqual(request.url, socketURL)
        XCTAssertEqual(request.value(forHTTPHeaderField: "X-LiveCue-Speech-Model"), "nemotron")
        XCTAssertNil(request.value(forHTTPHeaderField: "Origin"))
        XCTAssertNil(request.value(forHTTPHeaderField: "Sec-WebSocket-Protocol"))
        XCTAssertFalse(request.httpShouldHandleCookies)

        let configuration = URLSessionWebSocketTransport.makeConfiguration()
        XCTAssertGreaterThanOrEqual(configuration.timeoutIntervalForRequest, 60)
        XCTAssertNil(configuration.urlCache)
        XCTAssertNil(configuration.httpCookieStorage)
        XCTAssertFalse(configuration.httpShouldSetCookies)
    }

    @MainActor
    func testASocketIsNotOpenedForAnAddressThatIsNotAWebSocket() async {
        do {
            _ = try await URLSessionWebSocketTransport().open(url: url, headers: [:], maximumMessageSize: 131_072)
            XCTFail("An https address is not a WebSocket address.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .other)
        }
    }

    // MARK: - Whole requests, answered inside the test

    @MainActor
    func testSendReturnsStatusHeadersAndBody() async throws {
        let (head, body) = try await stubbedTransport().send(makeRequest("reply", headers: ["X-LiveCue-Protocol": "2"]))

        XCTAssertEqual(head.status, 201)
        XCTAssertEqual(head.headers["x-mixed-case"], "Kept As Sent")
        XCTAssertEqual(head.headers["x-echo-method"], "GET")
        XCTAssertEqual(head.headers["x-echo-protocol"], "2", "The request's headers reach the server.")
        XCTAssertEqual(head.headers["x-echo-cookie"], "none", "No cookie is sent.")
        XCTAssertEqual(body, Data("hello".utf8))
    }

    @MainActor
    func testAnErrorStatusIsAResponseNotAFailure() async throws {
        let (head, body) = try await stubbedTransport().send(makeRequest("status/503"))
        XCTAssertEqual(head.status, 503)
        XCTAssertEqual(body, Data("status".utf8))
    }

    @MainActor
    func testStreamYieldsTheBodyInOrderCutAtLineFeedsAnd4096Bytes() async throws {
        let (head, stream) = try await stubbedTransport().stream(makeRequest("lines"))
        XCTAssertEqual(head.status, 200)

        var chunks: [Data] = []
        for try await chunk in stream {
            chunks.append(chunk)
        }
        XCTAssertEqual(chunks.map(\.count), [6, 7, 4_096, 905, 4])
        XCTAssertEqual(chunks.first, Data("data:\n".utf8))
        XCTAssertEqual(chunks.last, Data("tail".utf8))
        var joined = Data()
        for chunk in chunks {
            joined.append(chunk)
        }
        XCTAssertEqual(joined, StubURLProtocol.linesBody, "Nothing is lost and nothing changes place.")
    }

    @MainActor
    func testATransportErrorIsThrownAsItsTransportFailure() async {
        let expectations: [(Int, TransportFailure)] = [
            (NSURLErrorTimedOut, .timedOut),
            (NSURLErrorSecureConnectionFailed, .tls),
            (NSURLErrorCannotFindHost, .unreachable),
            (NSURLErrorBadServerResponse, .other)
        ]
        let transport = stubbedTransport()
        for (code, expected) in expectations {
            do {
                _ = try await transport.send(makeRequest("fail/\(code)"))
                XCTFail("URL error \(code) is thrown.")
            } catch {
                XCTAssertEqual(error as? TransportFailure, expected, "send, URL error \(code)")
            }
            do {
                _ = try await transport.stream(makeRequest("fail/\(code)"))
                XCTFail("URL error \(code) is thrown.")
            } catch {
                XCTAssertEqual(error as? TransportFailure, expected, "stream, URL error \(code)")
            }
        }
    }

    @MainActor
    func testACancelledTaskSurfacesAsCancelled() async {
        let transport = stubbedTransport()
        let pending = makeRequest("never")

        // The request gets time to reach the URL session, where it waits for an answer that never
        // comes. Cancelled there or before it started, it ends the same way.
        let sending = Task { try await transport.send(pending) }
        for _ in 0..<20 {
            await Task.yield()
        }
        sending.cancel()
        do {
            _ = try await sending.value
            XCTFail("A cancelled request throws.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .cancelled)
        }

        let streaming = Task { try await transport.stream(pending).0 }
        for _ in 0..<20 {
            await Task.yield()
        }
        streaming.cancel()
        do {
            _ = try await streaming.value
            XCTFail("A cancelled request throws.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .cancelled)
        }
    }
}

/// Answers every request of a stubbed transport from the request's own path, so that no test touches a
/// network and no state is shared between tests.
///
/// | Path | Answer |
/// |---|---|
/// | `reply` | 201, the body "hello", and headers that echo what was sent |
/// | `status/<n>` | status `n` with the body "status" |
/// | `lines` | 200 with `linesBody`, delivered in pieces of 1,000 bytes |
/// | `fail/<code>` | the URL error with that code |
/// | anything else | no answer at all |
private final class StubURLProtocol: URLProtocol {
    /// Two lines, 5,000 bytes without a line feed, a line feed, and four bytes after the last one.
    static let linesBody: Data = {
        var body = Data("data:\nevent:\n".utf8)
        body.append(Data(repeating: 0x78, count: 5_000))
        body.append(Data("\ntail".utf8))
        return body
    }()

    override class func canInit(with request: URLRequest) -> Bool {
        true
    }

    override class func canonicalRequest(for request: URLRequest) -> URLRequest {
        request
    }

    override func startLoading() {
        guard let url = request.url else {
            client?.urlProtocol(self, didFailWithError: URLError(.badURL))
            return
        }
        let parts = url.path.split(separator: "/").map(String.init)

        switch parts.first {
        case "reply":
            respond(
                to: url,
                status: 201,
                headers: [
                    "Content-Type": "text/plain",
                    "X-Mixed-Case": "Kept As Sent",
                    "X-Echo-Method": request.httpMethod ?? "",
                    "X-Echo-Protocol": request.value(forHTTPHeaderField: "X-LiveCue-Protocol") ?? "none",
                    "X-Echo-Cookie": request.value(forHTTPHeaderField: "Cookie") ?? "none"
                ],
                pieces: [Data("hello".utf8)]
            )
        case "status":
            respond(to: url, status: Int(parts.last ?? "") ?? 500, headers: [:], pieces: [Data("status".utf8)])
        case "lines":
            var pieces: [Data] = []
            var rest = StubURLProtocol.linesBody
            while !rest.isEmpty {
                pieces.append(Data(rest.prefix(1_000)))
                rest = Data(rest.dropFirst(1_000))
            }
            respond(to: url, status: 200, headers: ["Content-Type": "text/event-stream"], pieces: pieces)
        case "fail":
            let code = Int(parts.last ?? "") ?? NSURLErrorUnknown
            client?.urlProtocol(self, didFailWithError: NSError(domain: NSURLErrorDomain, code: code))
        default:
            // No answer: the request waits until it is cancelled.
            break
        }
    }

    override func stopLoading() {}

    private func respond(to url: URL, status: Int, headers: [String: String], pieces: [Data]) {
        guard let response = HTTPURLResponse(url: url, statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers) else {
            client?.urlProtocol(self, didFailWithError: URLError(.badServerResponse))
            return
        }
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        for piece in pieces {
            client?.urlProtocol(self, didLoad: piece)
        }
        client?.urlProtocolDidFinishLoading(self)
    }
}
