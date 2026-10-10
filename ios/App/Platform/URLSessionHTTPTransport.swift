import Foundation
import LiveCueCore

/// `HTTPTransport` over `URLSession`: ephemeral configuration, no cookie storage, no cache, a task delegate
/// that answers every redirect with nil. `stream` uses `URLSession.bytes(for:)` and yields a chunk at
/// every line feed and at every 4,096 bytes.
struct URLSessionHTTPTransport: HTTPTransport {
    private let resourceTimeout: TimeInterval?

    init(resourceTimeout: TimeInterval? = nil) {
        self.resourceTimeout = resourceTimeout
    }

    func send(_ request: HTTPRequest) async throws -> (HTTPResponseHead, Data) {
        // STUB(WP-06)
        throw TransportFailure.other
    }

    func stream(_ request: HTTPRequest) async throws -> (HTTPResponseHead, AsyncThrowingStream<Data, Error>) {
        // STUB(WP-06)
        throw TransportFailure.other
    }
}
