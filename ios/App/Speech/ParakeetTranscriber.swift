import Foundation
import LiveCueCore

/// Providers `parakeet` and `fluid`: live captions on the phone, from a `StreamingRecognizing` engine.
///
/// Every captured buffer goes to the recogniser. Its partial callback gives the text on screen; the
/// partial is whatever the callback last reported (IOS-A1). One segment is committed per recording
/// stretch, on Pause or Stop, from the recogniser's final text; nothing is committed at an end of
/// utterance.
///
/// Parakeet recognises every buffer as it comes. Fluid Speech Mini (D-29) keeps the buffers and
/// transcribes them together once a second; only those passes have a time worth reporting.
///
/// A failure (a processing error, a full hand-off queue, an interruption, a lost route) stops the
/// capture, keeps the audio session and emits `failed` once. The session controller then calls
/// `finalize`, which asks the recogniser for its final text, so recognised speech is kept.
@MainActor final class ParakeetTranscriber: SpeechTranscriber {
    let provider: SpeechProvider
    private let channel: TranscriberEventChannel
    private let engine: (any StreamingRecognizing)?
    private let audio: any AudioCapturing
    private let clock: any AppClock
    private let models: OnDeviceModelManager

    /// One start of the transcriber.
    @MainActor private final class Run {
        enum State {
            /// The recogniser is being reset; the microphone is not open yet.
            case starting
            case listening
            /// `failed` was emitted; the capture is down and `finalize` has not run yet.
            case failed
            case finalizing
            /// Over. Nothing is emitted for this run any more.
            case closed
        }

        let generation: UUID
        var state = State.starting
        /// The recogniser threw: the buffers still queued are not given to it.
        var processingFailed = false
        var partials: AsyncStream<String>.Continuation?
        var partialTask: Task<Void, Never>?
        var chunkTask: Task<Void, Never>?
        var eventTask: Task<Void, Never>?

        init(generation: UUID) {
            self.generation = generation
        }
    }

    private var run: Run?

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(
        provider: SpeechProvider,
        engine: (any StreamingRecognizing)?,
        audio: any AudioCapturing,
        clock: any AppClock,
        models: OnDeviceModelManager
    ) {
        self.provider = provider
        self.channel = TranscriberEventChannel()
        self.engine = engine
        self.audio = audio
        self.clock = clock
        self.models = models
    }

    // MARK: - SpeechTranscriber

    func start(_ start: TranscriberStart) async throws {
        guard let engine else {
            throw TranscriberFailureText.notPrepared(provider)
        }
        // One capture at a time. A run that is still there ends without a word.
        if let earlier = self.run {
            close(earlier)
            audio.stop(releaseSession: false)
        }

        let run = Run(generation: start.generation)
        self.run = run

        // The recogniser calls back off the main actor. Its texts pass through a stream that keeps only
        // the newest, so they arrive here in order and a slow screen cannot pile them up.
        let (partials, partialSink) = AsyncStream<String>.makeStream(bufferingPolicy: .bufferingNewest(1))
        run.partials = partialSink
        await engine.reset()
        await engine.setPartialHandler { text in
            _ = partialSink.yield(text)
        }

        guard run.state == .starting else {
            // Shut down, or started again, while the recogniser was being reset.
            throw TranscriberFailureText.processingFailed
        }

        let streams: AudioCaptureStreams
        do {
            // On-device providers open the microphone at once.
            streams = try audio.start(
                AudioCaptureRequest(
                    target: .float32(sampleRate: ParakeetTranscriber.sampleRate),
                    queueLimit: ParakeetTranscriber.queueLimit
                )
            )
        } catch {
            close(run)
            throw TranscriberFailureText.microphone(error)
        }

        run.state = .listening
        setStatus(TranscriberStatusText.listening, run)
        run.partialTask = Task { await self.show(partials, run) }
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

        // The capture has stopped: the queue ends once its buffers have been processed.
        await run.chunkTask?.value
        await run.eventTask?.value

        var failure: TranscriberFailure?
        var finalText = ""
        do {
            finalText = try await engine.finish()
        } catch {
            failure = TranscriberFailureText.processingFailed
        }

        // The text of the whole stretch becomes one segment; the times are not known.
        let text = finalText.trimmingCharacters(in: .whitespacesAndNewlines)
        if !text.isEmpty {
            emit(.segment(TranscriptSegment(text: text, startSeconds: 0, endSeconds: 0)), run)
        }
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
        if engine != nil {
            models.reportStatus(TranscriberStatusText.ready)
        }
    }

    // MARK: - The capture

    private func process(_ chunks: AsyncStream<AudioChunk>, with engine: any StreamingRecognizing, _ run: Run) async {
        for await chunk in chunks {
            guard case .float32(let samples) = chunk, run.state != .closed, !run.processingFailed else { continue }
            do {
                let milliseconds = try await ParakeetTranscriber.timed(samples, with: engine, clock: clock)
                // After a failure and while finalising the queue is still worked off, but silently. A
                // buffer the recogniser only kept has no time to report.
                if run.state == .listening, let milliseconds {
                    emit(.lastChunk(milliseconds: milliseconds), run)
                    setStatus(TranscriberStatusText.lastBuffer(milliseconds: milliseconds), run)
                }
            } catch {
                // The recogniser's own text is not shown during a session (R-08).
                run.processingFailed = true
                fail(TranscriberFailureText.processingFailed, run)
            }
        }
    }

    /// Gives one buffer to the recogniser and returns how long that took, in milliseconds; nil when the
    /// recogniser only kept the buffer. It runs off the main actor, so the time is the recogniser's and
    /// not the screen's.
    private nonisolated static func timed(
        _ samples: [Float],
        with engine: any StreamingRecognizing,
        clock: any AppClock
    ) async throws -> Double? {
        let began = clock.uptime()
        guard try await engine.process(samples: samples) else { return nil }
        return max(0, clock.uptime() - began) * 1_000
    }

    private func show(_ partials: AsyncStream<String>, _ run: Run) async {
        for await text in partials {
            if run.state == .listening {
                emit(.partial(text), run)
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
        run.partials?.finish()
        run.partials = nil
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
}
