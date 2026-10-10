import Foundation
@testable import LiveCueRemake

/// A microphone a test feeds by hand. Its streams are unbounded: a test that wants an overflow sends
/// the `.overflow` event itself.
@MainActor final class FakeAudioCapture: AudioCapturing {
    private var chunkContinuation: AsyncStream<AudioChunk>.Continuation?
    private var eventContinuation: AsyncStream<AudioCaptureEvent>.Continuation?

    /// Set it to make `start` throw.
    var startFailure: AudioCaptureFailure?
    private(set) var isRunning = false
    /// The request of every successful start, in order.
    private(set) var requests: [AudioCaptureRequest] = []
    /// The `releaseSession` argument of each stop.
    private(set) var stops: [Bool] = []

    init() {}

    func start(_ request: AudioCaptureRequest) throws -> AudioCaptureStreams {
        if let startFailure {
            throw startFailure
        }
        requests.append(request)

        let (chunks, chunkContinuation) = AsyncStream<AudioChunk>.makeStream()
        let (events, eventContinuation) = AsyncStream<AudioCaptureEvent>.makeStream()
        self.chunkContinuation = chunkContinuation
        self.eventContinuation = eventContinuation
        isRunning = true
        return AudioCaptureStreams(chunks: chunks, events: events)
    }

    func stop(releaseSession: Bool) {
        stops.append(releaseSession)
        chunkContinuation?.finish()
        eventContinuation?.finish()
        chunkContinuation = nil
        eventContinuation = nil
        isRunning = false
    }

    /// Delivers a converted buffer to the running capture. Does nothing while stopped.
    func feed(_ chunk: AudioChunk) {
        chunkContinuation?.yield(chunk)
    }

    /// Delivers a capture event to the running capture. Does nothing while stopped.
    func send(_ event: AudioCaptureEvent) {
        eventContinuation?.yield(event)
    }
}
