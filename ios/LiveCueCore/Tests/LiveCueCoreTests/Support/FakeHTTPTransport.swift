import Foundation
import LiveCueCore

/// A scripted HTTP transport that records every request.
///
/// Replies are used in the order they were queued. When the queue is empty the default reply is used,
/// and without a default the request fails with `TransportFailure.other`.
actor FakeHTTPTransport: HTTPTransport {
    enum Reply: Sendable {
        case response(status: Int, headers: [String: String], body: Data)
        case failure(TransportFailure)
        /// Suspends until the task is cancelled, then throws `.cancelled`.
        case never
    }

    /// How a chunked body ends.
    enum StreamEnding: Sendable {
        /// The body ends normally.
        case finish
        /// The body breaks off with this failure after the last chunk.
        case fail(TransportFailure)
        /// The body never ends: the reader waits until its task is cancelled.
        case stall
    }

    private enum Scripted: Sendable {
        case reply(Reply)
        case chunks(status: Int, headers: [String: String], chunks: [Data], ending: StreamEnding)
    }

    private var queue: [Scripted]
    private var defaultReply: Reply?
    private var waiters: [(count: Int, continuation: CheckedContinuation<Void, Never>)] = []

    /// Every request received so far, in order.
    private(set) var requests: [HTTPRequest] = []

    init(replies: [Reply] = []) {
        queue = replies.map { Scripted.reply($0) }
    }

    func enqueue(_ reply: Reply) {
        queue.append(.reply(reply))
    }

    /// Used when the queue is empty.
    func setDefault(_ reply: Reply) {
        defaultReply = reply
    }

    /// A reply whose body `stream` delivers as these chunks. `send` returns them joined.
    func enqueueChunks(status: Int, headers: [String: String], chunks: [Data], ending: StreamEnding = .finish) {
        queue.append(.chunks(status: status, headers: headers, chunks: chunks, ending: ending))
    }

    /// Returns when at least `count` requests were received.
    func waitForRequests(count: Int) async {
        if requests.count >= count { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiters.append((count: count, continuation: continuation))
        }
    }

    // MARK: - HTTPTransport

    func send(_ request: HTTPRequest) async throws -> (HTTPResponseHead, Data) {
        switch record(request) {
        case .reply(let reply):
            return try await resolve(reply)
        case .chunks(let status, let headers, let chunks, _):
            var body = Data()
            for chunk in chunks {
                body.append(chunk)
            }
            return (HTTPResponseHead(status: status, headers: headers), body)
        }
    }

    /// Delivers a plain reply's body as one chunk, or the chunks given to `enqueueChunks`.
    func stream(_ request: HTTPRequest) async throws -> (HTTPResponseHead, AsyncThrowingStream<Data, Error>) {
        let head: HTTPResponseHead
        let chunks: [Data]
        let ending: StreamEnding

        switch record(request) {
        case .reply(let reply):
            let (replyHead, body) = try await resolve(reply)
            head = replyHead
            chunks = body.isEmpty ? [] : [body]
            ending = .finish
        case .chunks(let status, let headers, let scriptedChunks, let scriptedEnding):
            head = HTTPResponseHead(status: status, headers: headers)
            chunks = scriptedChunks
            ending = scriptedEnding
        }

        let (stream, continuation) = AsyncThrowingStream<Data, Error>.makeStream()
        for chunk in chunks {
            continuation.yield(chunk)
        }
        switch ending {
        case .finish: continuation.finish()
        case .fail(let failure): continuation.finish(throwing: failure)
        case .stall: break
        }
        return (head, stream)
    }

    // MARK: - Scripting

    private func record(_ request: HTTPRequest) -> Scripted {
        requests.append(request)
        let received = requests.count
        let ready = waiters.filter { $0.count <= received }
        waiters.removeAll { $0.count <= received }
        for waiter in ready {
            waiter.continuation.resume()
        }

        if !queue.isEmpty {
            return queue.removeFirst()
        }
        return .reply(defaultReply ?? .failure(.other))
    }

    private func resolve(_ reply: Reply) async throws -> (HTTPResponseHead, Data) {
        switch reply {
        case .response(let status, let headers, let body):
            return (HTTPResponseHead(status: status, headers: headers), body)
        case .failure(let failure):
            throw failure
        case .never:
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 3_600_000_000_000)
            }
            throw TransportFailure.cancelled
        }
    }
}
