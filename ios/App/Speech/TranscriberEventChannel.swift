import Foundation

/// The event stream of a transcriber, with the count of events emitted so far.
@MainActor final class TranscriberEventChannel {
    let stream: AsyncStream<TranscriberEvent>
    private(set) var emittedCount: Int

    private let continuation: AsyncStream<TranscriberEvent>.Continuation

    init() {
        let (stream, continuation) = AsyncStream<TranscriberEvent>.makeStream(bufferingPolicy: .unbounded)
        self.stream = stream
        self.continuation = continuation
        self.emittedCount = 0
    }

    /// An event emitted after `finish()` is dropped and not counted, so the count never runs ahead of
    /// what a consumer can receive.
    func emit(_ kind: TranscriberEvent.Kind, generation: UUID) {
        let result = continuation.yield(TranscriberEvent(generation: generation, kind: kind))
        if case .enqueued = result {
            emittedCount += 1
        }
    }

    func finish() {
        continuation.finish()
    }
}
