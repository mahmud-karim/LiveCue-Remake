import Foundation
import LiveCueCore

/// Provider `voz`: records on the phone and transcribes when Assist is tapped.
///
/// Captured audio accumulates in memory, in a `PendingAudioBuffer` (thirty minutes at most, never on
/// disk). A batch runs on Assist, on Pause and on Stop: it transcribes everything pending, keeps the words
/// that end after the previous batch, and leaves two seconds of audio as context for the next one.
///
/// A failure (the cap, a full hand-off queue, an interruption, a lost route) stops the capture, keeps
/// the audio session and emits `failed` once. The session controller then calls `finalize`, which
/// transcribes what is pending, so no recorded speech is lost.
@MainActor final class VozTranscriber: SpeechTranscriber {
    let provider: SpeechProvider = .voz
    private let channel: TranscriberEventChannel
    private let engine: (any BatchRecognizing)?
    private let audio: any AudioCapturing
    private let clock: any AppClock
    private let models: OnDeviceModelManager

    /// One start of the transcriber.
    @MainActor private final class Run {
        enum State {
            case recording
            /// `failed` was emitted; the capture is down and `finalize` has not run yet.
            case failed
            case finalizing
            /// Over. Nothing is emitted for this run any more.
            case closed
        }

        let generation: UUID
        var state = State.recording
        var chunkTask: Task<Void, Never>?
        var eventTask: Task<Void, Never>?

        init(generation: UUID) {
            self.generation = generation
        }
    }

    private var run: Run?
    private var pending = PendingAudioBuffer()

    // `transcribePending` and `finalize` run one at a time, in the order they were called.
    private var isBusy = false
    private var turns: [CheckedContinuation<Void, Never>] = []

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(engine: (any BatchRecognizing)?, audio: any AudioCapturing, clock: any AppClock, models: OnDeviceModelManager) {
        self.channel = TranscriberEventChannel()
        self.engine = engine
        self.audio = audio
        self.clock = clock
        self.models = models
    }

    // MARK: - SpeechTranscriber

    func start(_ start: TranscriberStart) async throws {
        guard engine != nil else {
            throw TranscriberFailureText.notPrepared(.voz)
        }
        // One capture at a time. A run that is still there ends without a word.
        if let earlier = self.run {
            close(earlier)
            audio.stop(releaseSession: false)
        }

        let streams: AudioCaptureStreams
        do {
            // On-device providers open the microphone at once.
            streams = try audio.start(
                AudioCaptureRequest(target: .float32(sampleRate: VozTranscriber.sampleRate), queueLimit: VozTranscriber.queueLimit)
            )
        } catch {
            throw TranscriberFailureText.microphone(error)
        }

        pending.reset()
        let run = Run(generation: start.generation)
        self.run = run
        setStatus(TranscriberStatusText.recordingOnDevice, run)
        run.chunkTask = Task { await self.record(streams.chunks, run) }
        run.eventTask = Task { await self.watch(streams.events, run) }
    }

    func finalize() async throws {
        await takeTurn()
        defer { endTurn() }

        guard let run, run.state == .recording || run.state == .failed else { return }
        if run.state == .recording {
            // The audio session stays active across a Pause (IOS-B6).
            audio.stop(releaseSession: false)
        }
        run.state = .finalizing

        // The capture has stopped: the queue ends, and what was in it is in the pending buffer.
        await run.chunkTask?.value
        await run.eventTask?.value

        var failure: (any Error)?
        do {
            _ = try await transcribe()
        } catch {
            failure = error
        }

        setStatus(TranscriberStatusText.paused, run)
        close(run)
        if let failure {
            throw failure
        }
    }

    func transcribePending() async throws -> TimeInterval {
        await takeTurn()
        defer { endTurn() }
        return try await transcribe()
    }

    func shutdown() async {
        if let run {
            close(run)
        }
        audio.stop(releaseSession: true)
        pending.reset()
        if engine != nil {
            models.reportStatus(TranscriberStatusText.ready)
        }
    }

    // MARK: - The batch

    /// Transcribes what is pending and returns the seconds it took; 0 when there is nothing new.
    private func transcribe() async throws -> TimeInterval {
        guard let engine, let run, run.state != .closed, let snapshot = pending.snapshot() else { return 0 }

        setStatus(TranscriberStatusText.transcribing, run)
        let began = clock.uptime()
        let recognition: BatchRecognition
        do {
            recognition = try await engine.transcribe(samples: snapshot.samples)
        } catch {
            // The recogniser's own text is not shown during a session (R-08).
            if run.state == .recording {
                setStatus(TranscriberStatusText.recordingOnDevice, run)
            }
            throw TranscriberFailureText.processingFailed
        }
        let seconds = max(0, clock.uptime() - began)

        // A result for a start that has ended meanwhile belongs to audio that is gone.
        guard self.run === run, run.state != .closed else { return seconds }

        let words = recognition.words.map { PendingAudioBuffer.TimedWord(text: $0.text, end: $0.end) }
        let text = pending.complete(snapshot, words: words)
        if !text.isEmpty {
            emit(.segment(TranscriptSegment(text: text, startSeconds: 0, endSeconds: recognition.duration)), run)
        }
        setStatus(TranscriberStatusText.batch(audioSeconds: recognition.duration, transcriptionSeconds: seconds), run)
        return seconds
    }

    // MARK: - The capture

    private func record(_ chunks: AsyncStream<AudioChunk>, _ run: Run) async {
        for await chunk in chunks {
            guard case .float32(let samples) = chunk, run.state != .closed else { continue }
            if !pending.append(samples) {
                // Thirty minutes are waiting. The buffer that did not fit is not kept.
                fail(TranscriberFailureText.pendingAudioLimit, run)
            }
        }
    }

    private func watch(_ events: AsyncStream<AudioCaptureEvent>, _ run: Run) async {
        for await event in events {
            if case .level(let value) = event {
                if run.state == .recording {
                    emit(.level(value), run)
                }
            } else if let failure = TranscriberFailureText.capture(event, onDevice: true) {
                fail(failure, run)
            }
        }
    }

    /// Stops the capture, keeping the audio session, and emits `failed`. Once per run.
    private func fail(_ failure: TranscriberFailure, _ run: Run) {
        guard run.state == .recording else { return }
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

    private func takeTurn() async {
        if !isBusy {
            isBusy = true
            return
        }
        await withCheckedContinuation { (turn: CheckedContinuation<Void, Never>) in
            turns.append(turn)
        }
    }

    private func endTurn() {
        if turns.isEmpty {
            isBusy = false
        } else {
            // The turn passes straight to the next caller.
            turns.removeFirst().resume()
        }
    }

    static let sampleRate: Double = 16_000
    /// The hand-off queue of an on-device provider.
    static let queueLimit = 128
}
