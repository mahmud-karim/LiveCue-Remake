import Foundation
import LiveCueCore

/// `WebSocketTransport` over `URLSessionWebSocketTask`: one ephemeral session per connection, invalidated
/// on close. The upgrade request keeps an idle timeout of 60 s or more (protocol 13.1).
///
/// `open` resumes the task and waits for `urlSession(_:webSocketTask:didOpenWithProtocol:)`. If the task
/// completes first, an HTTP response on the task means `WebSocketUpgradeRejected(status:)`, anything else
/// a `TransportFailure`. A redirect is never followed: it is refused, and the 3xx is the rejection.
struct URLSessionWebSocketTransport: WebSocketTransport {
    /// The idle timeout of the upgrade request, in seconds.
    static let idleTimeoutSeconds: TimeInterval = 60

    init() {}

    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection {
        if Task.isCancelled {
            throw TransportFailure.cancelled
        }
        // The system raises an exception for any other scheme, so it never gets one.
        guard let scheme = url.scheme?.lowercased(), scheme == "wss" || scheme == "ws" else {
            throw TransportFailure.other
        }

        let state = WebSocketSessionDelegate()
        let session = URLSession(
            configuration: URLSessionWebSocketTransport.makeConfiguration(),
            delegate: state,
            delegateQueue: nil
        )
        let task = session.webSocketTask(with: URLSessionWebSocketTransport.upgradeRequest(url: url, headers: headers))
        task.maximumMessageSize = maximumMessageSize
        // From here on the socket ends the task and the session when its last reference goes away,
        // which is what happens on every failing path below.
        let socket = WebSocketSocket(session: session, task: task, state: state)

        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, any Error>) in
                state.awaitOpening(continuation)
                task.resume()
            }
        } onCancel: {
            state.finishOpening(.failure(TransportFailure.cancelled))
            task.cancel()
        }
        return URLSessionWebSocketConnection(socket: socket)
    }

    // MARK: - Parts

    /// The same private session as the HTTP transport, with the idle timeout of the speech socket.
    static func makeConfiguration() -> URLSessionConfiguration {
        let configuration = URLSessionHTTPTransport.makeConfiguration(resourceTimeout: nil)
        configuration.timeoutIntervalForRequest = idleTimeoutSeconds
        return configuration
    }

    /// No `Origin`, no subprotocol, no cookies: only the headers the caller gives.
    static func upgradeRequest(url: URL, headers: [String: String]) -> URLRequest {
        var request = URLRequest(
            url: url,
            cachePolicy: .reloadIgnoringLocalCacheData,
            timeoutInterval: idleTimeoutSeconds
        )
        request.httpShouldHandleCookies = false
        for (name, value) in headers {
            request.setValue(value, forHTTPHeaderField: name)
        }
        return request
    }
}

/// One open connection.
///
/// `receive()` returns `.closed(code:reason:)` once when the task reports a close frame of the peer; a
/// failure without one is thrown as `TransportFailure`. After that message, and after `close`, sending
/// and receiving throw `TransportFailure.other`. A `receive()` whose task is cancelled throws
/// `.cancelled` at once and loses nothing: the message it was waiting for goes to the next `receive()`.
private struct URLSessionWebSocketConnection: WebSocketConnection {
    let socket: WebSocketSocket

    func send(text: String) async throws {
        try await send(.string(text))
    }

    func send(data: Data) async throws {
        try await send(.data(data))
    }

    func receive() async throws -> WebSocketMessage {
        let state = socket.state
        let task = socket.task
        let receiver = state.makeReceiverID()

        let arrival = try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<WebSocketArrival, any Error>) in
                state.awaitArrival(receiver: receiver, continuation: continuation, from: task)
            }
        } onCancel: {
            state.cancelReceiver(receiver)
        }

        switch arrival {
        case .message(let message):
            return message
        case .failed(let failure):
            // The task stopped receiving. With a close frame of the peer that is the close; without
            // one the connection dropped.
            if let closed = state.claimClose(reportedBy: task) {
                return closed
            }
            // The delegate hears of a close frame on the session's queue. Let a call that is already
            // on its way arrive before deciding that there was none.
            await settleDelegateQueue()
            if let closed = state.claimClose(reportedBy: task) {
                return closed
            }
            throw failure
        }
    }

    func close(code: Int, reason: String) async {
        guard socket.state.markClosedByClient() else { return }
        let closeCode = URLSessionWebSocketTask.CloseCode(rawValue: code) ?? .normalClosure
        // A close frame carries at most 123 bytes of reason.
        let reasonData = Data(reason.utf8.prefix(123))
        socket.task.cancel(with: closeCode, reason: reasonData.isEmpty ? nil : reasonData)
    }

    private func send(_ message: URLSessionWebSocketTask.Message) async throws {
        guard !socket.state.hasEnded else {
            throw TransportFailure.other
        }
        do {
            try await socket.task.send(message)
        } catch {
            throw URLErrorMapping.transportFailure(for: error)
        }
    }

    private func settleDelegateQueue() async {
        let queue = socket.session.delegateQueue
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            queue.addOperation {
                continuation.resume()
            }
        }
    }
}

/// What one receive call of the system ended with.
private enum WebSocketArrival: Sendable {
    case message(WebSocketMessage)
    case failed(TransportFailure)
}

/// The session, its one task and the state they share. When the last reference goes away the task is
/// cancelled and the session invalidated, so a connection that was dropped without `close` leaks nothing.
private final class WebSocketSocket: Sendable {
    let session: URLSession
    let task: URLSessionWebSocketTask
    let state: WebSocketSessionDelegate

    init(session: URLSession, task: URLSessionWebSocketTask, state: WebSocketSessionDelegate) {
        self.session = session
        self.task = task
        self.state = state
    }

    deinit {
        task.cancel()
        state.invalidate(session)
    }
}

/// The delegate of the one-connection session, and the state the connection shares with it. The session
/// calls on its own queue and the connection from any task, so every member is read and written under
/// the lock, and every continuation is taken out under the lock before it is resumed: none is resumed
/// twice and none is left waiting.
private final class WebSocketSessionDelegate: NSObject, URLSessionWebSocketDelegate, @unchecked Sendable {
    private let lock = NSLock()

    // Opening.
    private var openOutcome: Result<Void, any Error>?
    private var openWaiter: CheckedContinuation<Void, any Error>?

    // Receiving. At most one receive call of the system is outstanding. What it delivers while nobody
    // waits is kept for the next `receive()`.
    private var lastReceiverID = 0
    private var receiver: (id: Int, continuation: CheckedContinuation<WebSocketArrival, any Error>)?
    private var systemReceiveOutstanding = false
    private var arrivals: [WebSocketArrival] = []

    // Closing.
    private var peerClose: (code: Int, reason: String)?
    private var closeDelivered = false
    private var closedByClient = false
    private var invalidated = false

    // MARK: Opening

    /// Parks the continuation of `open` until the upgrade succeeds, fails or is cancelled. Resumes it at
    /// once when that already happened.
    func awaitOpening(_ continuation: CheckedContinuation<Void, any Error>) {
        let decided = lock.withLock { () -> Result<Void, any Error>? in
            if let known = openOutcome {
                return known
            }
            if Task.isCancelled {
                let cancelled: Result<Void, any Error> = .failure(TransportFailure.cancelled)
                openOutcome = cancelled
                return cancelled
            }
            openWaiter = continuation
            return nil
        }
        if let decided {
            continuation.resume(with: decided)
        }
    }

    /// The first outcome wins; later ones are ignored.
    func finishOpening(_ outcome: Result<Void, any Error>) {
        let waiter = lock.withLock { () -> CheckedContinuation<Void, any Error>? in
            guard openOutcome == nil else { return nil }
            openOutcome = outcome
            let waiting = openWaiter
            openWaiter = nil
            return waiting
        }
        waiter?.resume(with: outcome)
    }

    // MARK: Receiving

    func makeReceiverID() -> Int {
        lock.withLock { () -> Int in
            lastReceiverID += 1
            return lastReceiverID
        }
    }

    /// Hands the continuation the next arrival: one that is already kept, or the one the system
    /// delivers next. Called on the task that waits, so `Task.isCancelled` is that task's.
    func awaitArrival(
        receiver id: Int,
        continuation: CheckedContinuation<WebSocketArrival, any Error>,
        from task: URLSessionWebSocketTask
    ) {
        var startsSystemReceive = false
        let immediate = lock.withLock { () -> Result<WebSocketArrival, any Error>? in
            if Task.isCancelled {
                return .failure(TransportFailure.cancelled)
            }
            if closeDelivered || closedByClient {
                return .failure(TransportFailure.other)
            }
            if !arrivals.isEmpty {
                return .success(arrivals.removeFirst())
            }
            if receiver != nil {
                // One receiver at a time.
                return .failure(TransportFailure.other)
            }
            receiver = (id: id, continuation: continuation)
            if !systemReceiveOutstanding {
                systemReceiveOutstanding = true
                startsSystemReceive = true
            }
            return nil
        }

        if let immediate {
            continuation.resume(with: immediate)
            return
        }
        if startsSystemReceive {
            task.receive { result in
                self.systemReceiveFinished(result)
            }
        }
    }

    /// The task of the waiting `receive()` was cancelled. The system's receive call stays outstanding.
    func cancelReceiver(_ id: Int) {
        let waiting = lock.withLock { () -> CheckedContinuation<WebSocketArrival, any Error>? in
            guard let current = receiver, current.id == id else { return nil }
            receiver = nil
            return current.continuation
        }
        waiting?.resume(throwing: TransportFailure.cancelled)
    }

    private func systemReceiveFinished(_ result: Result<URLSessionWebSocketTask.Message, any Error>) {
        let arrival: WebSocketArrival
        switch result {
        case .success(let message):
            switch message {
            case .string(let text):
                arrival = .message(.text(text))
            case .data(let data):
                arrival = .message(.data(data))
            @unknown default:
                arrival = .failed(.other)
            }
        case .failure(let error):
            arrival = .failed(URLErrorMapping.transportFailure(for: error))
        }

        let waiting = lock.withLock { () -> CheckedContinuation<WebSocketArrival, any Error>? in
            systemReceiveOutstanding = false
            guard let current = receiver else {
                arrivals.append(arrival)
                return nil
            }
            receiver = nil
            return current.continuation
        }
        waiting?.resume(returning: arrival)
    }

    // MARK: Closing

    /// True after the close of the peer was delivered and after the client closed.
    var hasEnded: Bool {
        lock.withLock { closeDelivered || closedByClient }
    }

    /// True the first time. A `receive()` that is waiting then fails, as every later one does.
    func markClosedByClient() -> Bool {
        var isFirst = false
        let waiting = lock.withLock { () -> CheckedContinuation<WebSocketArrival, any Error>? in
            guard !closedByClient else { return nil }
            closedByClient = true
            isFirst = true
            let current = receiver
            receiver = nil
            return current?.continuation
        }
        waiting?.resume(throwing: TransportFailure.other)
        return isFirst
    }

    /// The close of the peer as a message, once. The delegate's record is used when it is there; the
    /// task's own close code covers the case in which the delegate has not been called yet. Code 1006
    /// is the system's word for "no close frame", so it is not a close.
    func claimClose(reportedBy task: URLSessionWebSocketTask) -> WebSocketMessage? {
        let taskCode = task.closeCode
        let taskReason = WebSocketSessionDelegate.text(task.closeReason)
        return lock.withLock { () -> WebSocketMessage? in
            guard !closeDelivered, !closedByClient else { return nil }
            if let recorded = peerClose {
                closeDelivered = true
                return .closed(code: recorded.code, reason: recorded.reason)
            }
            guard taskCode != .invalid, taskCode != .abnormalClosure else { return nil }
            closeDelivered = true
            return .closed(code: taskCode.rawValue, reason: taskReason)
        }
    }

    /// Invalidates the session once. Its task is finished or is being cancelled by then.
    func invalidate(_ session: URLSession) {
        let isFirst = lock.withLock { () -> Bool in
            guard !invalidated else { return false }
            invalidated = true
            return true
        }
        if isFirst {
            session.finishTasksAndInvalidate()
        }
    }

    private static func text(_ reason: Data?) -> String {
        guard let reason, let text = String(data: reason, encoding: .utf8) else { return "" }
        return text
    }

    // MARK: URLSessionWebSocketDelegate

    func urlSession(
        _ session: URLSession,
        webSocketTask: URLSessionWebSocketTask,
        didOpenWithProtocol negotiatedProtocol: String?
    ) {
        finishOpening(.success(()))
    }

    /// The peer sent a close frame.
    func urlSession(
        _ session: URLSession,
        webSocketTask: URLSessionWebSocketTask,
        didCloseWith closeCode: URLSessionWebSocketTask.CloseCode,
        reason: Data?
    ) {
        let text = WebSocketSessionDelegate.text(reason)
        lock.withLock { () -> Void in
            guard peerClose == nil else { return }
            peerClose = (code: closeCode.rawValue, reason: text)
        }
    }

    // MARK: URLSessionTaskDelegate

    /// The task is over. Before the socket opened, that is a failed upgrade.
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: (any Error)?) {
        let failure: any Error
        if let response = task.response as? HTTPURLResponse, response.statusCode != 101 {
            failure = WebSocketUpgradeRejected(status: response.statusCode)
        } else if let error {
            failure = URLErrorMapping.transportFailure(for: error)
        } else {
            failure = TransportFailure.other
        }
        finishOpening(.failure(failure))
        invalidate(session)
    }

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
