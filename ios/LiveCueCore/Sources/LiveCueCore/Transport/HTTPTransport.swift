import Foundation

/// One HTTP request, as the core hands it to a transport.
public struct HTTPRequest: Sendable {
    public var method: String
    public var url: URL
    public var headers: [String: String]
    public var body: Data?
    /// Idle timeout in seconds. Total time limits are enforced by the client that sends the request.
    public var timeout: TimeInterval

    public init(method: String, url: URL, headers: [String: String] = [:], body: Data? = nil, timeout: TimeInterval) {
        self.method = method
        self.url = url
        self.headers = headers
        self.body = body
        self.timeout = timeout
    }
}

/// Status and headers of a response.
public struct HTTPResponseHead: Sendable {
    public var status: Int
    /// Header names are lower-cased by the transport.
    public var headers: [String: String]

    public init(status: Int, headers: [String: String] = [:]) {
        self.status = status
        self.headers = headers
    }
}

/// Contract for every implementation: ephemeral session, no cookies, no cache, redirects never followed
/// (a 3xx is returned as a response). Throws only `TransportFailure`. Task cancellation surfaces as
/// `TransportFailure.cancelled`.
public protocol HTTPTransport: Sendable {
    func send(_ request: HTTPRequest) async throws -> (HTTPResponseHead, Data)
    /// The stream yields body chunks of any size, in order, and finishes at end of body.
    func stream(_ request: HTTPRequest) async throws -> (HTTPResponseHead, AsyncThrowingStream<Data, Error>)
}

/// Monotonic time and sleeping, replaceable in tests.
public struct CoreTiming: Sendable {
    public var uptime: @Sendable () -> TimeInterval
    public var sleep: @Sendable (TimeInterval) async throws -> Void

    public init(
        uptime: @escaping @Sendable () -> TimeInterval,
        sleep: @escaping @Sendable (TimeInterval) async throws -> Void
    ) {
        self.uptime = uptime
        self.sleep = sleep
    }

    /// `ProcessInfo.processInfo.systemUptime` and `Task.sleep(nanoseconds:)`.
    public static let system = CoreTiming(
        uptime: { ProcessInfo.processInfo.systemUptime },
        sleep: { seconds in
            // Clamped to a day, so that no value can overflow the nanosecond count.
            let clamped = min(max(0, seconds), 86_400)
            try await Task.sleep(nanoseconds: UInt64(clamped * 1_000_000_000))
        }
    )
}
