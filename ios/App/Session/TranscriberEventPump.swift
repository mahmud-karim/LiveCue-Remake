import Foundation

/// Consumes one transcriber's events in order on the main actor.
///
/// The pump is made before the transcriber's first start, so the number of events it has handled and
/// the transcriber's `emittedEventCount` count the same events.
@MainActor final class TranscriberEventPump {
    private struct Waiter {
        let target: Int
        let continuation: CheckedContinuation<Void, Never>
    }

    private let transcriber: any SpeechTranscriber
    private let handler: @MainActor (TranscriberEvent) -> Void
    private var handledCount = 0
    private var isStopped = false
    private var waiters: [Waiter] = []
    private var task: Task<Void, Never>?

    init(transcriber: any SpeechTranscriber, handler: @escaping @MainActor (TranscriberEvent) -> Void) {
        self.transcriber = transcriber
        self.handler = handler
        let events = transcriber.events
        // No transcriber ever finishes its stream: the loop ends when `stop()` cancels this task.
        task = Task { [weak self] in
            for await event in events {
                guard let self else { return }
                if self.isStopped { return }
                self.receive(event)
            }
        }
    }

    /// Returns when every event emitted so far was handled.
    func waitUntilCaughtUp() async {
        let target = transcriber.emittedEventCount
        guard !isStopped, handledCount < target else { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiters.append(Waiter(target: target, continuation: continuation))
        }
    }

    /// Nothing is handled afterwards. Whoever waits is let go.
    func stop() {
        isStopped = true
        task?.cancel()
        task = nil
        let waiting = waiters
        waiters = []
        for waiter in waiting {
            waiter.continuation.resume()
        }
    }

    private func receive(_ event: TranscriberEvent) {
        handler(event)
        handledCount += 1

        var stillWaiting: [Waiter] = []
        var ready: [Waiter] = []
        for waiter in waiters {
            if waiter.target <= handledCount {
                ready.append(waiter)
            } else {
                stillWaiting.append(waiter)
            }
        }
        waiters = stillWaiting
        for waiter in ready {
            waiter.continuation.resume()
        }
    }
}
