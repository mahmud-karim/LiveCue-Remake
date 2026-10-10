import Foundation

/// The Meta key test: a connection without audio.
public enum MetaConnectionTest {
    /// Connect, handshake, acknowledgement, endStream, close; no audio; bounded by `testSeconds`.
    /// Throws `MetaSpeechError`, or `CancellationError`.
    public static func run(key: String, transport: any WebSocketTransport, timing: CoreTiming = .system) async throws {
        // A key of the wrong shape is refused before anything is opened.
        let handshake = try MetaRealtimeAPI.handshake(key: key)
        let probe = MetaConnectionProbe(transport: transport, handshake: handshake)

        // The exchange and its time limit are two tasks. Whichever ends first decides; the limit ends
        // the exchange by closing its socket, which does not depend on the transport noticing a
        // cancelled task.
        let exchange = Task { try await probe.exchange() }
        let limit = Task {
            do {
                try await timing.sleep(MetaRealtimeAPI.testSeconds)
            } catch {
                return
            }
            await probe.giveUp(timedOut: true)
            exchange.cancel()
        }

        let outcome: Result<Void, any Error>
        do {
            try await withTaskCancellationHandler {
                try await exchange.value
            } onCancel: {
                // Closing the socket ends a wait that the transport would not end for a cancelled task.
                exchange.cancel()
                Task { await probe.giveUp(timedOut: false) }
            }
            outcome = .success(())
        } catch {
            outcome = .failure(error)
        }
        limit.cancel()

        guard case .failure(let error) = outcome else { return }

        // Whatever went wrong, the socket does not outlive the test.
        await probe.giveUp(timedOut: false)

        if Task.isCancelled {
            throw CancellationError()
        }
        if await probe.timedOut {
            throw MetaSpeechError.connection
        }
        // Only the key's shape and the acknowledgement have texts of their own. Everything else,
        // a refused upgrade and a dropped socket included, is "could not connect".
        throw (error as? MetaSpeechError) ?? MetaSpeechError.connection
    }
}

/// The one socket of a key test. An actor so that the time limit can close the socket while the exchange
/// is waiting on it.
private actor MetaConnectionProbe {
    /// WebSocket close codes: the test finished, or the phone gave up.
    static let normalClosure = 1000
    static let goingAway = 1001

    private let transport: any WebSocketTransport
    private let handshake: String
    private var connection: (any WebSocketConnection)?
    private var stopped = false
    private(set) var timedOut = false

    init(transport: any WebSocketTransport, handshake: String) {
        self.transport = transport
        self.handshake = handshake
    }

    /// Opens, sends the handshake, requires the acknowledgement, sends `endStream` and closes.
    /// Sends no binary message.
    func exchange() async throws {
        let opened = try await transport.open(
            url: MetaRealtimeAPI.endpoint,
            headers: [:],
            maximumMessageSize: MetaRealtimeAPI.maximumMessageBytes
        )
        guard !stopped else {
            // The test ended while the socket was opening.
            await opened.close(code: MetaConnectionProbe.goingAway, reason: "")
            throw MetaSpeechError.connection
        }
        connection = opened

        do {
            try await opened.send(text: handshake)
            let first = try await opened.receive()
            guard case .text(let text) = first else {
                if case .closed = first {
                    // Closed before any answer: there is no acknowledgement to judge.
                    throw MetaSpeechError.connection
                }
                throw MetaSpeechError.authentication
            }
            try MetaRealtimeAPI.acknowledge(text)
            try await opened.send(text: MetaRealtimeAPI.endStreamMessage)
        } catch {
            await close(code: MetaConnectionProbe.goingAway)
            throw error
        }
        await close(code: MetaConnectionProbe.normalClosure)
    }

    /// Closes the socket if it is open and stops an exchange that has not opened it yet. Idempotent.
    func giveUp(timedOut: Bool) async {
        if timedOut, !stopped {
            self.timedOut = true
        }
        stopped = true
        await close(code: MetaConnectionProbe.goingAway)
    }

    private func close(code: Int) async {
        guard let open = connection else { return }
        connection = nil
        await open.close(code: code, reason: "")
    }
}
