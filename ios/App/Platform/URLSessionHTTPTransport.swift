import Foundation
import LiveCueCore

/// `HTTPTransport` over `URLSession`: ephemeral configuration, no cookie storage, no cache, a task delegate
/// that answers every redirect with nil. `stream` uses `URLSession.bytes(for:)` and yields a chunk at
/// every line feed and at every 4,096 bytes.
///
/// One transport keeps one session, so that requests to the same host share a connection. The session
/// is invalidated when the last copy of the transport goes away.
struct URLSessionHTTPTransport: HTTPTransport {
    private let holder: HTTPSessionHolder

    /// `resourceTimeout` limits the whole of one request, in seconds; nil keeps the system's limit.
    /// The idle timeout comes with every request (`HTTPRequest.timeout`).
    init(resourceTimeout: TimeInterval? = nil) {
        holder = HTTPSessionHolder(
            configuration: URLSessionHTTPTransport.makeConfiguration(resourceTimeout: resourceTimeout)
        )
    }

    /// For unit tests: the same transport with URL protocol classes placed in front of the system's, so
    /// that a test can answer a request without a network.
    init(resourceTimeout: TimeInterval?, protocolClasses: [AnyClass]) {
        let configuration = URLSessionHTTPTransport.makeConfiguration(resourceTimeout: resourceTimeout)
        configuration.protocolClasses = protocolClasses
        holder = HTTPSessionHolder(configuration: configuration)
    }

    func send(_ request: HTTPRequest) async throws -> (HTTPResponseHead, Data) {
        if Task.isCancelled {
            throw TransportFailure.cancelled
        }
        do {
            let (data, response) = try await holder.session.data(
                for: URLSessionHTTPTransport.urlRequest(for: request),
                delegate: holder.redirects
            )
            guard let head = URLSessionHTTPTransport.head(of: response) else {
                throw TransportFailure.other
            }
            return (head, data)
        } catch {
            throw URLErrorMapping.transportFailure(for: error)
        }
    }

    func stream(_ request: HTTPRequest) async throws -> (HTTPResponseHead, AsyncThrowingStream<Data, Error>) {
        if Task.isCancelled {
            throw TransportFailure.cancelled
        }

        let bytes: URLSession.AsyncBytes
        let head: HTTPResponseHead
        do {
            let (received, response) = try await holder.session.bytes(
                for: URLSessionHTTPTransport.urlRequest(for: request),
                delegate: holder.redirects
            )
            guard let parsed = URLSessionHTTPTransport.head(of: response) else {
                received.task.cancel()
                throw TransportFailure.other
            }
            bytes = received
            head = parsed
        } catch {
            throw URLErrorMapping.transportFailure(for: error)
        }

        // The body is read by a task of its own. When the reader stops, or its task is cancelled, the
        // stream ends, and that ends the reading task and the request.
        let dataTask = bytes.task
        let body = AsyncThrowingStream<Data, Error> { continuation in
            let reading = Task {
                var chunker = HTTPBodyChunker()
                do {
                    for try await byte in bytes {
                        if let chunk = chunker.append(byte) {
                            continuation.yield(chunk)
                        }
                    }
                    if let rest = chunker.finish() {
                        continuation.yield(rest)
                    }
                    continuation.finish()
                } catch {
                    // What arrived before the failure is still delivered, then the failure.
                    if let rest = chunker.finish() {
                        continuation.yield(rest)
                    }
                    continuation.finish(throwing: URLErrorMapping.transportFailure(for: error))
                }
            }
            continuation.onTermination = { _ in
                reading.cancel()
                dataTask.cancel()
            }
        }
        return (head, body)
    }

    // MARK: - Parts

    /// Ephemeral: nothing is written to disk. No response cache, no cookie storage, cookies never sent.
    static func makeConfiguration(resourceTimeout: TimeInterval?) -> URLSessionConfiguration {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.urlCache = nil
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.httpCookieStorage = nil
        configuration.httpShouldSetCookies = false
        configuration.httpCookieAcceptPolicy = .never
        if let resourceTimeout {
            configuration.timeoutIntervalForResource = resourceTimeout
        }
        return configuration
    }

    /// `HTTPRequest.timeout` becomes the request's idle timeout.
    static func urlRequest(for request: HTTPRequest) -> URLRequest {
        var urlRequest = URLRequest(
            url: request.url,
            cachePolicy: .reloadIgnoringLocalCacheData,
            timeoutInterval: request.timeout
        )
        urlRequest.httpMethod = request.method
        urlRequest.httpBody = request.body
        urlRequest.httpShouldHandleCookies = false
        for (name, value) in request.headers {
            urlRequest.setValue(value, forHTTPHeaderField: name)
        }
        return urlRequest
    }

    /// Status and headers, header names lower-cased. Nil when the response is not an HTTP response.
    static func head(of response: URLResponse) -> HTTPResponseHead? {
        guard let http = response as? HTTPURLResponse else { return nil }
        var headers: [String: String] = [:]
        for (field, value) in http.allHeaderFields {
            guard let name = field as? String else { continue }
            headers[name.lowercased()] = (value as? String) ?? String(describing: value)
        }
        return HTTPResponseHead(status: http.statusCode, headers: headers)
    }
}

/// Cuts a response body into chunks: one at every line feed and one at every 4,096 bytes, whichever
/// comes first, and one for what is left at the end of the body.
struct HTTPBodyChunker {
    static let maximumChunkBytes = 4_096

    private var pending: [UInt8] = []

    init() {
        pending.reserveCapacity(HTTPBodyChunker.maximumChunkBytes)
    }

    /// Takes the next byte of the body. Returns a chunk when this byte completed one.
    mutating func append(_ byte: UInt8) -> Data? {
        pending.append(byte)
        guard byte == 0x0A || pending.count >= HTTPBodyChunker.maximumChunkBytes else { return nil }
        return take()
    }

    /// The bytes after the last chunk, or nil when there are none.
    mutating func finish() -> Data? {
        pending.isEmpty ? nil : take()
    }

    private mutating func take() -> Data {
        let chunk = Data(pending)
        pending.removeAll(keepingCapacity: true)
        return chunk
    }
}

/// Answers every redirect with nil: the 3xx response is handed to the caller and never followed.
final class RedirectRefusingDelegate: NSObject, URLSessionTaskDelegate, Sendable {
    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping @Sendable (URLRequest?) -> Void
    ) {
        completionHandler(nil)
    }
}

/// The session of one transport. It is shared by the copies of the transport and invalidated with the
/// last of them; requests that are still running finish first.
private final class HTTPSessionHolder: Sendable {
    let redirects: RedirectRefusingDelegate
    let session: URLSession

    init(configuration: URLSessionConfiguration) {
        let redirects = RedirectRefusingDelegate()
        self.redirects = redirects
        // The same object is the session's delegate and each task's delegate.
        session = URLSession(configuration: configuration, delegate: redirects, delegateQueue: nil)
    }

    deinit {
        session.finishTasksAndInvalidate()
    }
}
