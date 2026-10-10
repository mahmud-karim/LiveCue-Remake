import Foundation
import LiveCueCore

/// `WebSocketTransport` over `URLSessionWebSocketTask`: one ephemeral session per connection, invalidated
/// on close. The upgrade request keeps an idle timeout of 60 s or more (protocol 13.1).
struct URLSessionWebSocketTransport: WebSocketTransport {
    init() {}

    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection {
        // STUB(WP-06)
        throw TransportFailure.other
    }
}
