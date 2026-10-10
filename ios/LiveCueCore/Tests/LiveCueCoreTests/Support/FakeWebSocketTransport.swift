import Foundation
import LiveCueCore

/// A scripted WebSocket that records what was sent.
///
/// One transport stands for one connection. Messages given to `deliver` wait in a queue until `receive()`
/// asks for them. After a `.closed` message was received, and after the client called `close`, every
/// `receive()` throws `TransportFailure.other`; a `receive()` whose task is cancelled throws `.cancelled`.
actor FakeWebSocketTransport: WebSocketTransport {
    enum OpenResult: Sendable {
        case connection
        case rejected(status: Int)
        case failure(TransportFailure)
        /// Suspends until the task is cancelled, then throws `.cancelled`.
        case never
    }

    private enum Condition {
        case opened
        case texts(Int)
        case frames(Int)
        case closes(Int)
        case receiving
    }

    private let openResult: OpenResult
    private var openCount = 0
    private var inbound: [Result<WebSocketMessage, TransportFailure>] = []
    private var receivers: [Int: CheckedContinuation<WebSocketMessage, any Error>] = [:]
    private var nextReceiverID = 0
    private var sendsFail = false
    private var ended = false
    private var waiters: [(condition: Condition, continuation: CheckedContinuation<Void, Never>)] = []

    /// The URL of the last `open`, or nil before the first.
    private(set) var openedURL: URL?
    private(set) var openedHeaders: [String: String] = [:]
    private(set) var openedMaximumMessageSize: Int?
    /// Text messages sent by the client, in order.
    private(set) var sentTexts: [String] = []
    /// Binary messages sent by the client, in order.
    private(set) var sentFrames: [Data] = []
    /// The code of every `close` call, in order.
    private(set) var closeCalls: [Int] = []

    init(open: OpenResult = .connection) {
        openResult = open
    }

    // MARK: - Scripting

    /// The next `receive()` returns it.
    func deliver(_ message: WebSocketMessage) {
        push(.success(message))
    }

    /// The next `receive()` throws `TransportFailure.other`.
    func drop() {
        push(.failure(.other))
    }

    /// Later sends throw `TransportFailure.other`.
    func failSends() {
        sendsFail = true
    }

    // MARK: - Waiting

    func waitForOpen() async {
        await wait(for: .opened)
    }

    /// Returns when at least `count` text messages were sent.
    func waitForSentTexts(count: Int) async {
        await wait(for: .texts(count))
    }

    /// Returns when at least `count` binary messages were sent.
    func waitForSentFrames(count: Int) async {
        await wait(for: .frames(count))
    }

    /// Returns when `close` was called at least `count` times.
    func waitForCloseCalls(count: Int = 1) async {
        await wait(for: .closes(count))
    }

    /// Returns when a `receive()` is waiting with nothing left in the queue.
    func waitForReceive() async {
        await wait(for: .receiving)
    }

    // MARK: - WebSocketTransport

    func open(url: URL, headers: [String: String], maximumMessageSize: Int) async throws -> any WebSocketConnection {
        openedURL = url
        openedHeaders = headers
        openedMaximumMessageSize = maximumMessageSize
        openCount += 1
        wake()

        switch openResult {
        case .connection:
            return FakeWebSocketConnection(transport: self)
        case .rejected(let status):
            throw WebSocketUpgradeRejected(status: status)
        case .failure(let failure):
            throw failure
        case .never:
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 3_600_000_000_000)
            }
            throw TransportFailure.cancelled
        }
    }

    // MARK: - The connection's side

    fileprivate func connectionSend(text: String) throws {
        guard !sendsFail, !ended else { throw TransportFailure.other }
        sentTexts.append(text)
        wake()
    }

    fileprivate func connectionSend(data: Data) throws {
        guard !sendsFail, !ended else { throw TransportFailure.other }
        sentFrames.append(data)
        wake()
    }

    fileprivate func connectionReceive() async throws -> WebSocketMessage {
        if !inbound.isEmpty {
            return try take(inbound.removeFirst())
        }
        guard !ended else { throw TransportFailure.other }

        nextReceiverID += 1
        let id = nextReceiverID
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<WebSocketMessage, any Error>) in
                if Task.isCancelled {
                    continuation.resume(throwing: TransportFailure.cancelled)
                } else {
                    receivers[id] = continuation
                    wake()
                }
            }
        } onCancel: {
            Task { await self.cancelReceiver(id) }
        }
    }

    fileprivate func connectionClose(code: Int) {
        closeCalls.append(code)
        ended = true
        let waiting = receivers
        receivers.removeAll()
        for continuation in waiting.values {
            continuation.resume(throwing: TransportFailure.other)
        }
        wake()
    }

    // MARK: - Internals

    private func push(_ item: Result<WebSocketMessage, TransportFailure>) {
        guard let oldest = receivers.keys.min(), let continuation = receivers.removeValue(forKey: oldest) else {
            inbound.append(item)
            return
        }
        do {
            continuation.resume(returning: try take(item))
        } catch {
            continuation.resume(throwing: error)
        }
    }

    /// Unwraps a queued item and remembers that a close frame ends the connection.
    private func take(_ item: Result<WebSocketMessage, TransportFailure>) throws -> WebSocketMessage {
        let message = try item.get()
        if case .closed = message {
            ended = true
        }
        return message
    }

    private func cancelReceiver(_ id: Int) {
        receivers.removeValue(forKey: id)?.resume(throwing: TransportFailure.cancelled)
    }

    private func isMet(_ condition: Condition) -> Bool {
        switch condition {
        case .opened: return openCount > 0
        case .texts(let count): return sentTexts.count >= count
        case .frames(let count): return sentFrames.count >= count
        case .closes(let count): return closeCalls.count >= count
        case .receiving: return !receivers.isEmpty
        }
    }

    private func wait(for condition: Condition) async {
        if isMet(condition) { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiters.append((condition: condition, continuation: continuation))
        }
    }

    private func wake() {
        var stillWaiting: [(condition: Condition, continuation: CheckedContinuation<Void, Never>)] = []
        var ready: [CheckedContinuation<Void, Never>] = []
        for waiter in waiters {
            if isMet(waiter.condition) {
                ready.append(waiter.continuation)
            } else {
                stillWaiting.append(waiter)
            }
        }
        waiters = stillWaiting
        for continuation in ready {
            continuation.resume()
        }
    }
}

/// The connection handed out by `FakeWebSocketTransport.open`. Everything goes back to the transport.
private struct FakeWebSocketConnection: WebSocketConnection {
    let transport: FakeWebSocketTransport

    func send(text: String) async throws {
        try await transport.connectionSend(text: text)
    }

    func send(data: Data) async throws {
        try await transport.connectionSend(data: data)
    }

    func receive() async throws -> WebSocketMessage {
        try await transport.connectionReceive()
    }

    func close(code: Int, reason: String) async {
        await transport.connectionClose(code: code)
    }
}
