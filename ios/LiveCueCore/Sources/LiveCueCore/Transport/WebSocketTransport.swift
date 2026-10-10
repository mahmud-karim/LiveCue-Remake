import Foundation

public enum WebSocketMessage: Equatable, Sendable {
    case text(String)
    case data(Data)
    /// The peer closed, or the close handshake finished. Delivered once; `receive()` throws afterwards.
    case closed(code: Int, reason: String)
}

public protocol WebSocketConnection: Sendable {
    /// Throws `TransportFailure`.
    func send(text: String) async throws
    /// Throws `TransportFailure`.
    func send(data: Data) async throws
    /// Throws `TransportFailure` when the connection dropped without a close frame.
    func receive() async throws -> WebSocketMessage
    /// Idempotent; never throws.
    func close(code: Int, reason: String) async
}

public protocol WebSocketTransport: Sendable {
    /// Returns once the upgrade succeeded. Throws `WebSocketUpgradeRejected` for an HTTP answer other than
    /// 101, `TransportFailure` otherwise. No `Origin`, no subprotocol, no compression.
    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection
}
