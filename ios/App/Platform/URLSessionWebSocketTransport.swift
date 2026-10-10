import Foundation
import LiveCueCore

/// `WebSocketTransport` over `URLSessionWebSocketTask`: one ephemeral session per connection, invalidated
/// on close.
struct URLSessionWebSocketTransport: WebSocketTransport {
    init() {}

    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection {
        // STUB(WP-06)
        throw TransportFailure.other
    }
}
