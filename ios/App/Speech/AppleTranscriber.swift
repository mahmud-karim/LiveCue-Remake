import Foundation
import LiveCueCore

/// Provider `apple` (D-28): live captions from the recogniser built into iOS 26.
///
/// Every captured buffer goes to the recogniser. A tentative result is the text on screen; a final
/// result is committed as a segment at once, with the time the recogniser gives it, so the transcript
/// grows while people speak and nothing waits for Pause. Pause and Stop ask the recogniser to finalise
/// what it still holds; those last results are committed before the call returns.
///
/// A failure (the recogniser stops, a full hand-off queue, an interruption, a lost route) stops the
/// capture, keeps the audio session and emits `failed` once. The session controller then calls
/// `finalize`, which collects what the recogniser can still finalise.
@MainActor final class AppleTranscriber: SpeechTranscriber {
    let provider: SpeechProvider = .apple
    private let channel: TranscriberEventChannel
    private let engine: (any SystemSpeechRecognizing)?
    private let audio: any AudioCapturing
    private let clock: any AppClock
    private let models: OnDeviceModelManager

    /// One start of the transcriber.
    @MainActor private final class Run {
        enum State {
            /// The recogniser is being started; the microphone is not open yet.
            case starting
            case listening
            /// `failed` was emitted; the capture is down and `finalize` has not run yet.
            case failed
            case finalizing
            /// Over. Nothing is emitted for this run any more.
            case closed
        }

        let generation: UUID
        /// Where this start lies in the session, in seconds: added to the recogniser's times.
        let offset: Double
        var state = State.starting
        /// The recogniser threw: nothing more is given to it or asked of it.
        var engineFailed = false
        /// The recogniser did not finish in time and was ended.
        var finalizeTimedOut = false
        var resultTask: Task<Void, Never>?
        var chunkTask: Task<Void, Never>?
        var eventTask: Task<Void, Never>?

        init(generation: UUID, offset: Double) {
            self.generation = generation
            self.offset = offset
        }
    }

    private var run: Run?

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    /// `engine` is nil while the language asset is not installed.
    init(engine: (any SystemSpeechRecognizing)?, audio: any AudioCapturing, clock: any AppClock, models: OnDeviceModelManager) {
        self.channel = TranscriberEventChannel()
        self.engine = engine
        self.audio = audio
        self.clock = clock
        self.models = models
    }

    // MARK: - SpeechTranscriber

    func start(_ start: TranscriberStart) async throws {
        guard let engine else {
            throw TranscriberFailureText.notPrepared(.apple)
        }
        // One capture at a time. A run that is still there ends without a word.
        if let earlier = self.run {
            close(earlier)
            audio.stop(releaseSession: false)
        }

        let run = Run(generation: start.generation, offset: start.streamOffsetSeconds)
        self.run = run

        let results: AsyncThrowingStream<SystemSpeechResult, any Error>
        do {
            results = try await engine.begin()
        } catch {
            // The system's own text is not shown during a session (R-08).
            close(run)
            throw TranscriberFailureText.systemSpeechStart
        }

        guard run.state == .starting else {
            // Shut down, or started again, while the recogniser was starting. A later start has
            // already replaced this recognition; after a shutdown it is ended here.
            if self.run == nil {
                await engine.cancel()
            }
            throw TranscriberFailureText.processingFailed
        }

        let streams: AudioCaptureStreams
        do {
            // On-device providers open the microphone at once.
            streams = try audio.start(
                AudioCaptureRequest(
                    target: .float32(sampleRate: AppleTranscriber.sampleRate),
                    queueLimit: AppleTranscriber.queueLimit
                )
            )
        } catch {
            close(run)
            await engine.cancel()
            throw TranscriberFailureText.noMicrophone
        }

        run.state = .listening
        setStatus(TranscriberStatusText.listening, run)
        run.resultTask = Task { await self.show(results, run) }
        run.chunkTask = Task { await self.process(streams.chunks, with: engine, run) }
        run.eventTask = Task { await self.watch(streams.events, run) }
    }

    func finalize() async throws {
        guard let engine, let run, run.state == .listening || run.state == .failed else { return }
        if run.state == .listening {
            // The audio session stays active across a Pause (IOS-B6).
            audio.stop(releaseSession: false)
        }
        run.state = .finalizing

        // The capture has stopped: the queue ends once its buffers have been handed over.
        await run.chunkTask?.value
        await run.eventTask?.value

        var failure: TranscriberFailure?
        if run.engineFailed {
            // Said when it happened. Nothing more is asked of the recogniser.
            await engine.cancel()
        } else {
            // A recogniser that does not answer must not hold Pause or Stop for ever.
            let watchdog = Task { [clock] in
                do {
                    try await clock.sleep(seconds: AppleTranscriber.finalizeLimitSeconds)
                } catch {
                    return
                }
                // Only while this finalisation is still waiting: the recogniser may serve a later start.
                guard run.state == .finalizing else { return }
                run.finalizeTimedOut = true
                await engine.cancel()
            }
            do {
                try await engine.finish()
            } catch {
                failure = TranscriberFailureText.processingFailed
                await engine.cancel()
            }
            // The last results are committed as they arrive; then their stream ends.
            await run.resultTask?.value
            watchdog.cancel()
            if run.engineFailed || run.finalizeTimedOut {
                failure = TranscriberFailureText.processingFailed
            }
        }
        await run.resultTask?.value

        emit(.partial(""), run)
        setStatus(TranscriberStatusText.paused, run)
        close(run)

        if let failure {
            throw failure
        }
    }

    func transcribePending() async throws -> TimeInterval {
        0
    }

    func shutdown() async {
        if let run {
            close(run)
        }
        audio.stop(releaseSession: true)
        if let engine {
            models.reportStatus(TranscriberStatusText.ready)
            await engine.cancel()
        }
    }

    // MARK: - The recogniser and the capture

    private func show(_ results: AsyncThrowingStream<SystemSpeechResult, any Error>, _ run: Run) async {
        do {
            for try await result in results {
                apply(result, run)
            }
        } catch {
            // The recogniser's own text is not shown during a session (R-08).
            run.engineFailed = true
            fail(TranscriberFailureText.processingFailed, run)
        }
    }

    /// A tentative text replaces the partial; a final text becomes a segment. Final results keep
    /// arriving while the last audio is finalised and after a capture failure, and are kept.
    private func apply(_ result: SystemSpeechResult, _ run: Run) {
        let text = result.text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard result.isFinal else {
            if run.state == .listening {
                emit(.partial(text), run)
            }
            return
        }
        if !text.isEmpty {
            let start = run.offset + result.startSeconds
            let end = run.offset + max(result.startSeconds, result.endSeconds)
            emit(.segment(TranscriptSegment(text: text, startSeconds: start, endSeconds: end)), run)
        }
        if run.state == .listening {
            emit(.partial(""), run)
        }
    }

    private func process(_ chunks: AsyncStream<AudioChunk>, with engine: any SystemSpeechRecognizing, _ run: Run) async {
        for await chunk in chunks {
            guard case .float32(let samples) = chunk, run.state != .closed, !run.engineFailed else { continue }
            do {
                try await engine.process(samples: samples)
            } catch {
                run.engineFailed = true
                fail(TranscriberFailureText.processingFailed, run)
            }
        }
    }

    private func watch(_ events: AsyncStream<AudioCaptureEvent>, _ run: Run) async {
        for await event in events {
            if case .level(let value) = event {
                if run.state == .listening {
                    emit(.level(value), run)
                }
            } else if let failure = TranscriberFailureText.capture(event, onDevice: true) {
                fail(failure, run)
            }
        }
    }

    /// Stops the capture, keeping the audio session, and emits `failed`. Once per run.
    private func fail(_ failure: TranscriberFailure, _ run: Run) {
        guard run.state == .listening else { return }
        run.state = .failed
        audio.stop(releaseSession: false)
        emit(.failed(failure), run)
    }

    // MARK: - Pieces

    private func close(_ run: Run) {
        run.state = .closed
        if self.run === run {
            self.run = nil
        }
    }

    private func emit(_ kind: TranscriberEvent.Kind, _ run: Run) {
        guard run.state != .closed else { return }
        channel.emit(kind, generation: run.generation)
    }

    /// The status line goes to the session and to Model Library.
    private func setStatus(_ line: String, _ run: Run) {
        guard run.state != .closed else { return }
        channel.emit(.status(line), generation: run.generation)
        models.reportStatus(line)
    }

    static let sampleRate: Double = 16_000
    /// The hand-off queue of an on-device provider.
    static let queueLimit = 128
    /// How long Pause or Stop waits for the recogniser's last results.
    static let finalizeLimitSeconds: TimeInterval = 10
}
