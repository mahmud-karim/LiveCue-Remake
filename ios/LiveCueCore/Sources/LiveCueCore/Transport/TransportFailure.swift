import Foundation

/// The only error a transport throws, apart from `WebSocketUpgradeRejected`.
public enum TransportFailure: Error, Equatable, Sendable {
    case cancelled, timedOut, tls, unreachable, other
}

/// The server answered a WebSocket upgrade with an HTTP status other than 101.
public struct WebSocketUpgradeRejected: Error, Equatable, Sendable {
    public var status: Int

    public init(status: Int) {
        self.status = status
    }
}
