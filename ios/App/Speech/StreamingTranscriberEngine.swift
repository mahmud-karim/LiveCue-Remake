import Foundation
import LiveCueCore

/// Shared by Meta and relay: owns one `SpeechStreamSession` per start, pumps capture into it and maps
/// its events to `TranscriberEvent`s (adding the stream offset to segment times).
///
/// One start is one `Run`. Three tasks serve it: one passes the session's events on, in order; one
/// hands the captured buffers to the session, in order and off the main actor; one reads the capture's
/// own events (level, overflow, interruption, lost route).
///
/// The rules the session controller relies on:
/// - The microphone opens only after the provider is ready.
/// - When `start` or `finalize` returns or throws, every event that call caused has been emitted.
/// - A stream that fails before `start` has returned makes `start` throw; it emits no `failed`.
/// - A stream that fails afterwards tears itself down, says "Disconnected · resume to retry" and then
///   emits `failed`, which is the last event of its generation.
/// - Nothing reconnects by itself.
@MainActor final class StreamingTranscriberEngine {
    private let provider: SpeechProvider
    private let audio: any AudioCapturing
    private let transport: any WebSocketTransport
    private let clock: any AppClock
    private let channel: TranscriberEventChannel

    /// One start of the transcriber.
    @MainActor private final class Run {
        enum State {
            /// `start` has not returned yet.
            case starting
            /// Audio is flowing.
            case running
            /// A failure is being handled; `failed` has not been emitted yet.
            case failing
            /// `finalize` is ending the stream.
            case finalizing
            /// Over. Nothing is emitted for this run any more.
            case closed
        }

        let generation: UUID
        let offsetSeconds: Double
        let session: SpeechStreamSession
        var state = State.starting
        var sessionTask: Task<Void, Never>?
        var uploadTask: Task<Void, Never>?
        var captureTask: Task<Void, Never>?
        var isCapturing = false
        /// The last status line of this run, without a finalisation time.
        var statusLine = ""
        /// The failure the session reported as its last event.
        var sessionFailure: SpeechStreamFailure?
        /// Why `start` must throw although the provider was ready.
        var startFailure: TranscriberFailure?
        /// True once everything `session.start()` reported has been passed on.
        var hasBegun = false
        var beginWaiter: CheckedContinuation<Void, Never>?

        init(generation: UUID, offsetSeconds: Double, session: SpeechStreamSession) {
            self.generation = generation
            self.offsetSeconds = offsetSeconds
            self.session = session
        }
    }

    private var run: Run?

    init(
        provider: SpeechProvider,
        audio: any AudioCapturing,
        transport: any WebSocketTransport,
        clock: any AppClock,
        channel: TranscriberEventChannel
    ) {
        self.provider = provider
        self.audio = audio
        self.transport = transport
        self.clock = clock
        self.channel = channel
    }

    // MARK: - Start

    func start(_ start: TranscriberStart, dialect: SpeechStreamDialect) async throws {
        // One stream at a time. A run that is still there ends without a word.
        if self.run != nil {
            await shutdown()
        }

        let session = SpeechStreamSession(dialect: dialect, transport: transport, timing: clock.coreTiming)
        let run = Run(generation: start.generation, offsetSeconds: start.streamOffsetSeconds, session: session)
        self.run = run
        setStatus(TranscriberStatusText.connecting(provider), run)
        run.sessionTask = Task { await self.follow(session.events, run) }

        do {
            try await session.start()
        } catch {
            // The socket is closed and the session's events have finished: the task ends once it has
            // passed on what the start reported.
            await run.sessionTask?.value
            close(run)
            throw startFailure(for: error)
        }

        // The provider is ready: only now does the microphone open (AC-IOS-020).
        if run.state == .starting, run.startFailure == nil {
            do {
                let request = AudioCaptureRequest(
                    target: .pcm16(sampleRate: Double(provider.sampleRate)),
                    queueLimit: StreamingTranscriberEngine.queueLimit
                )
                let streams = try audio.start(request)
                run.isCapturing = true
                run.uploadTask = Task { await StreamingTranscriberEngine.upload(streams.chunks, to: session) }
                run.captureTask = Task { await self.watch(streams.events, run) }
            } catch {
                run.startFailure = TranscriberFailureText.microphone(error)
            }
        }

        // The session reported "connected" and the usage stream before it returned. Wait until both
        // have been passed on, so that the caller finds them emitted.
        if run.state == .starting, run.startFailure == nil, !run.hasBegun {
            await withCheckedContinuation { (waiter: CheckedContinuation<Void, Never>) in
                run.beginWaiter = waiter
            }
        }

        // A stream that ended in the meantime never started as far as the caller knows.
        guard run.state == .starting, run.startFailure == nil else {
            let failure = run.startFailure ?? TranscriberFailure(.stream, message: lostMessage)
            await abandon(run)
            throw failure
        }
        run.state = .running
    }

    /// Ends a run whose start failed after the provider was ready. The usage stream it opened is closed
    /// as interrupted; no `failed` is emitted, because `start` throws instead.
    private func abandon(_ run: Run) async {
        stopCapture(run)
        await run.session.abort()
        await run.sessionTask?.value
        await run.uploadTask?.value
        close(run)
    }

    private func startFailure(for error: any Error) -> TranscriberFailure {
        if let failure = error as? SpeechStreamFailure {
            return TranscriberFailure(.stream, message: failure.message)
        }
        if let failure = error as? TranscriberFailure {
            return failure
        }
        // Cancelled: the caller went away, and nobody shows this.
        return TranscriberFailure(.stream, message: lostMessage)
    }

    // MARK: - Finalize and shutdown

    func finalize() async throws {
        guard let run, run.state == .running else { return }
        run.state = .finalizing
        stopCapture(run)

        // One limit for the whole of finalising (spec 01 section 3.6): the buffers still queued, the
        // remainder, `endStream` and the close. The session bounds its own part; this bounds the
        // hand-over of the queue as well, so a send that never returns cannot hold a Pause or a Stop.
        let session = run.session
        let seconds = finalizeSeconds
        let limit = Task { [clock] in
            do {
                try await clock.sleep(seconds: seconds)
            } catch {
                return
            }
            await session.abort()
        }

        await run.uploadTask?.value
        let ending = await session.finish()
        limit.cancel()
        await run.sessionTask?.value
        await run.captureTask?.value
        close(run)

        if ending != .completed {
            throw TranscriberFailureText.finalizeFailed
        }
    }

    func shutdown() async {
        guard let run else {
            audio.stop(releaseSession: true)
            return
        }
        // Closed first: from here on this run emits nothing.
        close(run)
        resumeBeginWaiter(run)
        run.isCapturing = false
        audio.stop(releaseSession: true)
        await run.session.abort()
        await run.sessionTask?.value
        await run.uploadTask?.value
    }

    // MARK: - The session's events

    private func follow(_ events: AsyncStream<SpeechStreamEvent>, _ run: Run) async {
        for await event in events {
            pass(event, run)
        }
        // Nothing more will come from this stream.
        run.hasBegun = true
        resumeBeginWaiter(run)
        await streamEnded(run)
    }

    private func pass(_ event: SpeechStreamEvent, _ run: Run) {
        switch event {
        case .status(let status):
            switch status {
            case .connecting:
                // Said at the top of `start`.
                break
            case .loading(let elapsedSeconds):
                setStatus(TranscriberStatusText.loading(provider, seconds: elapsedSeconds), run)
            case .connected(let setupSeconds):
                setStatus(TranscriberStatusText.connected(provider, setupSeconds: setupSeconds), run)
            case .firstWords(let seconds):
                setStatus(TranscriberStatusText.firstWords(seconds: seconds), run)
            case .finalized(let seconds):
                // Appended to the line as it stood, which is kept: a second time replaces the first.
                emit(.status(TranscriberStatusText.appendingFinal(run.statusLine, seconds: seconds)), run)
            }

        case .partial(let text):
            emit(.partial(text), run)

        case .segment(let segment):
            // The session's times are relative to its stream; the session's clock is the offset.
            let moved = TranscriptSegment(
                id: segment.id,
                text: segment.text,
                startSeconds: segment.startSeconds + run.offsetSeconds,
                endSeconds: segment.endSeconds + run.offsetSeconds
            )
            emit(.segment(moved), run)

        case .usage(let update):
            emit(.usage(update), run)
            if case .begin = update {
                run.hasBegun = true
                resumeBeginWaiter(run)
            }

        case .failed(let failure):
            // The stream has ended; what follows from that is decided when its events have finished.
            run.sessionFailure = failure
        }
    }

    /// The session's events have finished: the stream is over.
    private func streamEnded(_ run: Run) async {
        switch run.state {
        case .starting:
            // `start` has not returned: it throws this failure itself.
            if let failure = run.sessionFailure, run.startFailure == nil {
                run.startFailure = TranscriberFailure(.stream, message: failure.message)
            }
        case .running:
            // The stream failed by itself: a relay error or drop, a Meta error or drop, a send failure.
            run.state = .failing
            stopCapture(run)
            await run.uploadTask?.value
            finishFailing(TranscriberFailure(.stream, message: run.sessionFailure?.message ?? lostMessage), run)
        case .failing, .finalizing, .closed:
            // The call in progress reports its own outcome.
            break
        }
    }

    // MARK: - The capture's events

    private func watch(_ events: AsyncStream<AudioCaptureEvent>, _ run: Run) async {
        for await event in events {
            if case .level(let value) = event {
                if run.state == .running {
                    emit(.level(value), run)
                }
            } else if let failure = TranscriberFailureText.capture(event, onDevice: false) {
                await captureFailed(failure, run)
            }
        }
    }

    /// The upload queue overflowed, an interruption began or the input device went away.
    private func captureFailed(_ failure: TranscriberFailure, _ run: Run) async {
        switch run.state {
        case .starting:
            // `start` has not returned: it throws this failure and tears the run down.
            if run.startFailure == nil {
                run.startFailure = failure
            }
            resumeBeginWaiter(run)
        case .running:
            run.state = .failing
            stopCapture(run)
            await run.session.abort()
            await run.sessionTask?.value
            await run.uploadTask?.value
            finishFailing(failure, run)
        case .failing, .finalizing, .closed:
            break
        }
    }

    /// The last two events of a failed run. Everything is down by now.
    private func finishFailing(_ failure: TranscriberFailure, _ run: Run) {
        guard run.state == .failing else { return }
        setStatus(TranscriberStatusText.disconnected, run)
        emit(.failed(failure), run)
        close(run)
    }

    // MARK: - Pieces

    /// Hands the captured buffers to the session, in order. It runs off the main actor, so a busy
    /// screen does not fill the hand-off queue. It ends when the capture has stopped and the queue is
    /// empty.
    private nonisolated static func upload(_ chunks: AsyncStream<AudioChunk>, to session: SpeechStreamSession) async {
        for await chunk in chunks {
            if case .pcm16(let bytes) = chunk {
                await session.send(audio: bytes)
            }
        }
    }

    /// Stops the capture of this run, releasing the audio session.
    private func stopCapture(_ run: Run) {
        guard run.isCapturing else { return }
        run.isCapturing = false
        audio.stop(releaseSession: true)
    }

    private func close(_ run: Run) {
        run.state = .closed
        if self.run === run {
            self.run = nil
        }
    }

    private func resumeBeginWaiter(_ run: Run) {
        guard let waiter = run.beginWaiter else { return }
        run.beginWaiter = nil
        waiter.resume()
    }

    private func emit(_ kind: TranscriberEvent.Kind, _ run: Run) {
        guard run.state != .closed else { return }
        channel.emit(kind, generation: run.generation)
    }

    private func setStatus(_ line: String, _ run: Run) {
        guard run.state != .closed else { return }
        run.statusLine = line
        channel.emit(.status(line), generation: run.generation)
    }

    /// The text for a stream that is gone without a reason of its own.
    private var lostMessage: String {
        if provider == .meta {
            return SpeechStreamFailure(.meta(.connection)).message
        }
        return SpeechStreamFailure(.relay(RelayFailure.local(.streamDropped, origin: .stream))).message
    }

    /// C-60 for the relay; the same 15 s for Meta.
    private var finalizeSeconds: TimeInterval {
        provider == .meta ? MetaRealtimeAPI.finalizeSeconds : RelayConstants.speechFinalizeSeconds
    }

    /// The hand-off queue of a streaming provider.
    static let queueLimit = 64
}
