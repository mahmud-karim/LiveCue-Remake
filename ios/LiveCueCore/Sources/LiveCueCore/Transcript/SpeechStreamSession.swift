import Foundation

/// One streaming speech connection, Meta or relay. One actor carries a whole stream, so the microphone
/// rule, framing, usage and the completed rule are tested on Linux with a fake socket.
///
/// How it works: every wait is a state change on this actor. One task of the session's own opens the
/// socket and reads it; one task sleeps for the time limit of the current wait; `start()` and `finish()`
/// suspend on a continuation that the state change resumes. A time limit therefore never depends on the
/// transport noticing a cancelled task: when it passes, the socket is closed and the wait ends here.
///
/// What counts as a clean Pause or Stop (IOS-A11, as changed in 1.1.0 after the first use on a real
/// iPhone, where the cloud provider did not close with code 1000). Once `endStream` has left:
/// - a close by the peer with code 1000, 1001 or no status code (1005) is completed;
/// - when no started turn is still without its final text and nothing arrives for `quietSeconds`, the
///   phone closes and the stream is completed (for the relay only after its `streamComplete`, because a
///   PC model may still be working through the last audio in silence);
/// - any other end (another close code, a drop, the 15 s limit) is completed when no started turn is
///   without its final text, and interrupted when one is. A close whose reason is a code of the relay
///   stays that failure.
/// Before `endStream` has left, every end of the connection is a failure, as before.
public actor SpeechStreamSession {
    /// How long nothing may arrive after `endStream`, with every started turn final, before the phone
    /// closes the stream as completed.
    public static let quietSeconds: TimeInterval = 1.5

    /// Finishes when the stream has ended, after the last event.
    public nonisolated let events: AsyncStream<SpeechStreamEvent>

    private let continuation: AsyncStream<SpeechStreamEvent>.Continuation
    private let dialect: SpeechStreamDialect
    private let transport: any WebSocketTransport
    private let timing: CoreTiming

    // MARK: - State

    private enum Phase {
        /// `start()` was not called yet.
        case idle
        /// Opening the socket and waiting for the provider: no audio is sent.
        case starting
        /// The provider is ready: audio is sent, events are read.
        case streaming
        /// `finish()` began: the remainder and `endStream` go out, then the close is awaited.
        case finishing
        /// The stream is over. `terminated` says whether its last event was delivered too.
        case ended
    }

    /// What `start()` needs to open the socket, worked out before anything is opened.
    private struct Opening: Sendable {
        var url: URL
        var headers: [String: String]
        var maximumMessageSize: Int
        /// The limit for connecting and for the provider to become ready.
        var limitSeconds: TimeInterval
        /// The Meta handshake. The relay expects nothing before audio.
        var firstMessage: String?
    }

    private var phase = Phase.idle
    private var connection: (any WebSocketConnection)?
    /// Opens the socket and reads it until the stream ends.
    private var reader: Task<Void, Never>?
    /// Sleeps for the limit of the current wait: becoming ready, then finalising.
    private var limit: Task<Void, Never>?

    private var startWaiter: CheckedContinuation<Void, any Error>?
    private var startOutcome: Result<Void, any Error>?
    /// True once the socket is closed and `events` has finished.
    private var terminated = false
    private var terminationWaiters: [CheckedContinuation<Void, Never>] = []

    private var negotiation: RelaySpeechNegotiation?
    private var transcript = StreamTranscript()
    private var framer: PCMFramer
    private let sampleRate: Int
    /// Full frames not yet sent. One loop at a time empties it, so frames leave in order.
    private var outbox: [Data] = []
    private var isDraining = false
    private var drainWaiters: [CheckedContinuation<Void, Never>] = []

    private let usageID = UUID()
    /// True between `usage(.begin)` and `usage(.finish)`.
    private var usageOpen = false
    private var sentBytes = 0
    private var lastPartial = ""
    private var firstTextSeen = false
    private var endStreamSent = false
    /// True once the send of `endStream` returned. The quiet period and the leniency of the 15 s limit
    /// need it: a stream whose `endStream` never left was not asked to end.
    private var endStreamDelivered = false
    /// The relay said that all audio was processed.
    private var streamCompleteSeen = false
    private var finalizationReported = false
    /// While finishing: when `endStream` left, or when the provider last said something.
    private var lastActivityAt: TimeInterval = 0
    /// Sleeps for the quiet period after `endStream`.
    private var quiet: Task<Void, Never>?
    /// Names the quiet period in flight; an older one that wakes does nothing.
    private var quietRound = 0
    private var ending = SpeechStreamEnding.interrupted

    private var openedAt: TimeInterval = 0
    private var readyAt: TimeInterval = 0
    private var finishBeganAt: TimeInterval = 0

    public init(dialect: SpeechStreamDialect, transport: any WebSocketTransport, timing: CoreTiming = .system) {
        let (events, continuation) = AsyncStream<SpeechStreamEvent>.makeStream()
        self.events = events
        self.continuation = continuation
        self.dialect = dialect
        self.transport = transport
        self.timing = timing
        switch dialect {
        case .meta:
            self.negotiation = nil
            self.framer = PCMFramer(frameBytes: MetaRealtimeAPI.frameBytes)
            self.sampleRate = MetaRealtimeAPI.sampleRate
        case .relay(_, let model):
            self.negotiation = RelaySpeechNegotiation(model: model)
            self.framer = PCMFramer(frameBytes: RelayConstants.speechFrameBytes)
            self.sampleRate = RelayConstants.speechSampleRate
        }
    }

    // MARK: - Public operations

    /// Opens the socket and returns when the provider is ready for audio: the Meta acknowledgement
    /// (limit 30 s) or the relay's valid `ready` (limit C-59). Emits `.status` while waiting, then
    /// `.status(.connected)` and `.usage(.begin)`. For the relay dialect, when C-59 passes first the socket
    /// is closed and the failure is `.relay(.local(.timeout, origin: .stream))`. Throws
    /// `SpeechStreamFailure` or `CancellationError`; after a throw the socket is closed and `events` has
    /// finished.
    public func start() async throws {
        guard phase == .idle else {
            // One session carries one stream. A second start ends it, so the rule "after a throw the
            // socket is closed" holds here too.
            await abort()
            throw alreadyUsedFailure
        }

        let opening: Opening
        do {
            opening = try makeOpening()
        } catch {
            // A key of the wrong shape or an unusable address: nothing is opened.
            phase = .ended
            continuation.finish()
            markTerminated()
            throw error
        }

        phase = .starting
        // C-59 and the Meta limit count from the start of the connection attempt.
        openedAt = timing.uptime()
        emit(.status(.connecting))

        reader = Task { await self.read(opening) }
        limit = Task {
            do {
                try await self.timing.sleep(opening.limitSeconds)
            } catch {
                // Cancelled: the wait ended before the limit.
                return
            }
            await self.startLimitPassed()
        }

        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (waiter: CheckedContinuation<Void, any Error>) in
                if let outcome = startOutcome {
                    SpeechStreamSession.resume(waiter, with: outcome)
                } else {
                    startWaiter = waiter
                }
            }
        } onCancel: {
            Task { await self.startCancelled() }
        }
    }

    /// PCM16 mono at the dialect's rate, any length. Frames it and sends it. Ignored before `start`
    /// returned and after `finish` began. A send error ends the stream with `.failed(.sendFailed)`.
    public func send(audio: Data) async {
        guard phase == .streaming else { return }
        outbox.append(contentsOf: framer.append(audio))
        await drainOutbox()
    }

    /// Flushes the remainder as one shorter frame, sends `endStream` once, then waits up to 15 s for the
    /// end of the stream. Returns after `events` has finished. `completed` by the rule in the type's
    /// description: a clean close, a quiet period with every started turn final, or any other end that
    /// leaves no started turn without its final text.
    public func finish() async -> SpeechStreamEnding {
        switch phase {
        case .idle:
            phase = .ended
            continuation.finish()
            markTerminated()
            return .interrupted
        case .starting:
            // Nothing was started, so there is nothing to finalise: the caller went away.
            await failStart(CancellationError())
            await waitUntilTerminated()
            return .interrupted
        case .finishing, .ended:
            await waitUntilTerminated()
            return ending
        case .streaming:
            break
        }

        phase = .finishing
        finishBeganAt = timing.uptime()

        // One limit for the whole of finalising, the flush included (spec 02 section 5.5: draining and
        // closing are bounded together), so a send that never returns cannot hold a Pause or Stop.
        let seconds = finalizeSeconds
        limit = Task {
            do {
                try await self.timing.sleep(seconds)
            } catch {
                // Cancelled: the stream ended before the limit.
                return
            }
            await self.finishLimitPassed()
        }

        if let remainder = framer.flush() {
            outbox.append(remainder)
        }
        await drainOutbox()

        if phase == .finishing, let open = connection {
            // Marked before the send: a close that arrives while the send is returning is then already
            // judged against a stream that asked to end.
            endStreamSent = true
            negotiation?.markEndStreamSent()
            do {
                try await open.send(text: endStreamMessage)
                if phase == .finishing {
                    endStreamDelivered = true
                    lastActivityAt = timing.uptime()
                    restartQuietPeriod()
                }
            } catch {
                await fail(SpeechStreamFailure(.sendFailed))
            }
        }

        await waitUntilTerminated()
        return ending
    }

    /// Closes now. Idempotent. Marks the usage stream interrupted if it was open.
    public func abort() async {
        switch phase {
        case .idle:
            phase = .ended
            continuation.finish()
            markTerminated()
        case .starting:
            await failStart(CancellationError())
        case .streaming, .finishing:
            await end(.interrupted, failure: nil, closeCode: CloseCode.goingAway)
        case .ended:
            await waitUntilTerminated()
        }
    }

    // MARK: - Opening

    private func makeOpening() throws -> Opening {
        switch dialect {
        case .meta(let key):
            let handshake: String
            do {
                handshake = try MetaRealtimeAPI.handshake(key: key)
            } catch {
                throw SpeechStreamFailure(.meta((error as? MetaSpeechError) ?? .connection))
            }
            // No header and no query: the key travels only inside the handshake.
            return Opening(
                url: MetaRealtimeAPI.endpoint,
                headers: [:],
                maximumMessageSize: MetaRealtimeAPI.maximumMessageBytes,
                limitSeconds: MetaRealtimeAPI.connectSeconds,
                firstMessage: handshake
            )

        case .relay(let pairing, let model):
            guard let url = pairing.relayEndpoint.speechURL() else {
                throw SpeechStreamFailure(.relay(RelayFailure.local(.badEndpoint, origin: .stream)))
            }
            // Protocol 10.1 and 13.2: these three headers and nothing else.
            return Opening(
                url: url,
                headers: [
                    "Authorization": "Bearer " + pairing.token,
                    "X-LiveCue-Protocol": String(RelayConstants.protocolVersion),
                    "X-LiveCue-Speech-Model": model.rawValue
                ],
                maximumMessageSize: RelayConstants.maximumInboundSpeechMessageBytes,
                limitSeconds: RelayRoute.speech.phoneTimeout,
                firstMessage: nil
            )
        }
    }

    /// The reader task: opens the socket, sends the first message if the dialect has one, then hands
    /// every inbound message to `handle` until the stream ends.
    private func read(_ opening: Opening) async {
        let opened: any WebSocketConnection
        do {
            opened = try await transport.open(
                url: opening.url,
                headers: opening.headers,
                maximumMessageSize: opening.maximumMessageSize
            )
        } catch {
            await failStart(startError(forOpenFailure: error))
            return
        }

        guard phase == .starting else {
            // The start ended (limit, cancellation or abort) while the socket was opening.
            await opened.close(code: CloseCode.goingAway, reason: "")
            return
        }
        connection = opened

        if let first = opening.firstMessage {
            do {
                try await opened.send(text: first)
            } catch {
                await fail(dropFailure)
                return
            }
        }

        while phase != .ended {
            let message: WebSocketMessage
            do {
                message = try await opened.receive()
            } catch {
                // The socket dropped with no close frame.
                await connectionEndedUnexplained()
                return
            }
            await handle(message)
            if case .closed = message {
                // Nothing can be received after the close frame.
                return
            }
        }
    }

    private func startError(forOpenFailure error: any Error) -> any Error {
        if let failure = error as? TransportFailure, failure == .cancelled {
            // Protocol 13.6 row 1: a transport that reports cancellation is the caller going away.
            return CancellationError()
        }
        switch dialect {
        case .meta:
            return SpeechStreamFailure(.meta(.connection))
        case .relay:
            // Protocol 13.7 step 1.
            if let rejected = error as? WebSocketUpgradeRejected {
                return SpeechStreamFailure(.relay(RelaySpeechNegotiation.failure(forUpgradeStatus: rejected.status)))
            }
            if let failure = error as? TransportFailure {
                return SpeechStreamFailure(.relay(RelaySpeechNegotiation.failure(for: failure)))
            }
            return SpeechStreamFailure(.relay(RelayFailure.local(.network, origin: .stream)))
        }
    }

    // MARK: - Inbound messages

    private func handle(_ message: WebSocketMessage) async {
        guard phase == .starting || phase == .streaming || phase == .finishing else { return }

        switch message {
        case .closed(let code, let reason):
            await handleClose(code: code, reason: reason)

        case .data:
            // Both providers speak text. A binary message cannot be the Meta acknowledgement; anywhere
            // else it is not part of either protocol and is ignored.
            if phase == .starting, case .meta = dialect {
                await fail(SpeechStreamFailure(.meta(.authentication)))
            }

        case .text(let text):
            switch dialect {
            case .meta(let key):
                await handleMeta(text, key: key)
            case .relay:
                await handleRelay(text)
            }
        }

        // The provider said something while the end is awaited: the quiet period starts again.
        if phase == .finishing, endStreamDelivered {
            lastActivityAt = timing.uptime()
            restartQuietPeriod()
        }
    }

    private func handleMeta(_ text: String, key: String) async {
        if phase == .starting {
            // The first message must be the acknowledgement; the microphone opens only after it.
            do {
                try MetaRealtimeAPI.acknowledge(text)
            } catch {
                await fail(SpeechStreamFailure(.meta((error as? MetaSpeechError) ?? .authentication)))
                return
            }
            becomeReady(setupSeconds: timing.uptime() - openedAt)
            return
        }

        let event: MetaEvent
        do {
            event = try MetaRealtimeAPI.event(text, key: key)
        } catch {
            await fail(SpeechStreamFailure(.meta((error as? MetaSpeechError) ?? .provider)))
            return
        }
        guard !event.isIgnored else { return }
        apply(
            event.transcriptEvent,
            audioProcessedMs: event.audioProcessedMs,
            hasText: event.transcript?.isEmpty == false
        )
    }

    private func handleRelay(_ text: String) async {
        guard var state = negotiation else { return }

        let step: RelaySpeechNegotiation.Step
        do {
            step = try state.receive(text: text)
        } catch {
            let failure = (error as? RelayFailure) ?? RelayFailure.local(.unexpectedFrame, origin: .stream)
            // After an `error` frame the relay closes by itself; anything else the phone cuts.
            let closeCode: Int
            if case .wire = failure.kind {
                closeCode = CloseCode.normal
            } else {
                closeCode = CloseCode.goingAway
            }
            await fail(SpeechStreamFailure(.relay(failure)), closeCode: closeCode)
            return
        }
        negotiation = state

        switch step {
        case .loading(let elapsedSeconds):
            emit(.status(.loading(elapsedSeconds: elapsedSeconds)))
        case .ready(let handshakeMs):
            // The relay measures the setup from the upgrade to the lab being connected.
            becomeReady(setupSeconds: handshakeMs / 1000)
        case .event(let event, let audioProcessedMs, let hasText):
            apply(event, audioProcessedMs: audioProcessedMs, hasText: hasText)
        case .streamComplete(let finalizationMs):
            streamCompleted(frameText: text, finalizationMs: finalizationMs)
        case .ignore:
            break
        }
    }

    private func handleClose(code: Int, reason: String) async {
        if phase == .finishing {
            lastActivityAt = timing.uptime()
        }
        switch dialect {
        case .relay:
            guard let state = negotiation else { return }
            // Protocol 13.7 steps 6 and 7. The limit has not passed, or this would not be running.
            switch state.ending(closeCode: code, closeReason: reason, insideWindow: true) {
            case .completed:
                await end(.completed, failure: nil, closeCode: CloseCode.normal)
            case .interrupted(let failure):
                // A close the relay explained with a code of its own is that failure. One it did not
                // explain is judged by what is still missing.
                if failure == RelayFailure.local(.streamDropped, origin: .stream), nothingIsMissing {
                    await complete()
                } else {
                    await fail(SpeechStreamFailure(.relay(failure)), closeCode: CloseCode.normal)
                }
            }

        case .meta:
            // IOS-A11: after `endStream`, a close with 1000, 1001 or no status code is the provider
            // ending the stream. Any other close is judged by what is still missing.
            let asked = phase == .finishing && endStreamSent
            if (asked && CloseCode.clean.contains(code)) || nothingIsMissing {
                await complete()
            } else {
                await fail(SpeechStreamFailure(.meta(.connection)), closeCode: CloseCode.normal)
            }
        }
    }

    /// The provider is ready for audio: `start()` returns.
    private func becomeReady(setupSeconds: Double) {
        guard phase == .starting else { return }
        phase = .streaming
        limit?.cancel()
        limit = nil
        readyAt = timing.uptime()
        emit(.status(.connected(setupSeconds: max(0, setupSeconds))))
        usageOpen = true
        emit(.usage(.begin(id: usageID)))
        resolveStart(.success(()))
    }

    /// Feeds one caption event to the state machine and reports what changed.
    private func apply(_ event: StreamTranscriptEvent, audioProcessedMs: Double?, hasText: Bool) {
        // Every inbound event updates the usage stream: the processed position when the event has one,
        // and whether it carried text (spec 02 section 9.4).
        emit(.usage(.update(id: usageID, sentMs: nil, processedMs: audioProcessedMs, hasTranscript: hasText)))

        let segment = transcript.apply(event)
        if let segment {
            emit(.segment(segment))
        }
        let partial = transcript.partialText
        if partial != lastPartial {
            lastPartial = partial
            emit(.partial(partial))
        }

        // First words: the first text this stream shows, as a partial or as a turn that completed
        // without one. Measured from the moment the provider was ready, which is when audio starts.
        if !firstTextSeen, segment != nil || !partial.isEmpty {
            firstTextSeen = true
            let seconds = max(0, timing.uptime() - readyAt)
            emit(.usage(.firstText(milliseconds: seconds * 1000)))
            emit(.status(.firstWords(seconds: seconds)))
        }
    }

    /// The relay's `streamComplete`: all audio was processed. A normal close follows.
    private func streamCompleted(frameText: String, finalizationMs: Double?) {
        // The negotiation step carries only the finalisation time. The frame's processed position is
        // the last word on how much audio the PC handled, and for a stream without speech the only one.
        var processed: Double?
        if case .streamComplete(let position, _)? = RelaySpeechFrame.parse(frameText) {
            processed = position
        }
        emit(.usage(.update(id: usageID, sentMs: nil, processedMs: processed, hasTranscript: false)))
        streamCompleteSeen = true

        if let finalizationMs {
            reportFinalization(milliseconds: finalizationMs)
        } else if phase == .finishing {
            reportFinalization(milliseconds: (timing.uptime() - finishBeganAt) * 1000)
        }
    }

    private func reportFinalization(milliseconds: Double) {
        finalizationReported = true
        let value = max(0, milliseconds)
        emit(.usage(.finalization(milliseconds: value)))
        emit(.status(.finalized(seconds: value / 1000)))
    }

    // MARK: - Outbound audio

    /// Sends what is in the outbox, in order. Returns when the outbox is empty and nothing is in
    /// flight, or when the stream has ended.
    private func drainOutbox() async {
        if isDraining {
            // Another call is sending. It also sends what this call queued; wait for it, so that the
            // caller's audio has left (and `endStream` cannot overtake a frame) when this returns.
            await withCheckedContinuation { (waiter: CheckedContinuation<Void, Never>) in
                if isDraining {
                    drainWaiters.append(waiter)
                } else {
                    waiter.resume()
                }
            }
            return
        }
        guard !outbox.isEmpty else { return }

        isDraining = true
        while phase == .streaming || phase == .finishing, !outbox.isEmpty, let open = connection {
            let frame = outbox.removeFirst()
            do {
                try await open.send(data: frame)
            } catch {
                await fail(SpeechStreamFailure(.sendFailed))
                break
            }
            sentBytes += frame.count
            emit(.usage(.update(id: usageID, sentMs: sentMilliseconds, processedMs: nil, hasTranscript: false)))
        }
        isDraining = false

        let waiting = drainWaiters
        drainWaiters.removeAll()
        for waiter in waiting {
            waiter.resume()
        }
    }

    /// Bytes sent so far as milliseconds of 16-bit mono audio at the dialect's rate.
    private var sentMilliseconds: Double {
        Double(sentBytes) / (Double(sampleRate) * 2 / 1000)
    }

    // MARK: - Time limits and cancellation

    private func startLimitPassed() async {
        guard phase == .starting else { return }
        // This runs on the limit task, which has done its work: forget it, so that closing the socket
        // does not happen on a task that was just cancelled.
        limit = nil
        switch dialect {
        case .relay:
            // Protocol 13.7 step 2: C-59 passed.
            await fail(SpeechStreamFailure(.relay(RelayFailure.local(.timeout, origin: .stream))))
        case .meta:
            await fail(SpeechStreamFailure(.meta(.connection)))
        }
    }

    private func finishLimitPassed() async {
        guard phase == .finishing else { return }
        limit = nil
        // Protocol 13.7 step 6: C-60 passed with no close. The same limit serves Meta. It is a failure
        // when `endStream` never left or a started turn is still without its final text.
        if endStreamDelivered, nothingIsMissing {
            await complete()
        } else {
            await fail(dropFailure)
        }
    }

    // MARK: - The end after `endStream` (IOS-A11)

    /// The stream asked to end (all audio has left, `endStream` is on its way) and no started turn is
    /// still without its final text: whatever ends the connection now, nothing that was heard is missing.
    private var nothingIsMissing: Bool {
        phase == .finishing && endStreamSent && !transcript.hasUnfinishedTurn
    }

    /// The phone may end the stream itself after a quiet period. The relay announces the end of its work
    /// with `streamComplete`; until then a silent PC model may still be transcribing the last audio.
    private var mayEndAfterQuiet: Bool {
        guard endStreamDelivered, nothingIsMissing else { return false }
        switch dialect {
        case .meta:
            return true
        case .relay:
            return streamCompleteSeen
        }
    }

    /// Starts the quiet period again, or stops it while something is still expected.
    private func restartQuietPeriod() {
        quiet?.cancel()
        quiet = nil
        quietRound += 1
        guard mayEndAfterQuiet else { return }

        let round = quietRound
        let seconds = SpeechStreamSession.quietSeconds
        quiet = Task {
            do {
                try await self.timing.sleep(seconds)
            } catch {
                // Cancelled: something arrived, or the stream ended.
                return
            }
            await self.quietPeriodPassed(round)
        }
    }

    private func quietPeriodPassed(_ round: Int) async {
        guard round == quietRound, mayEndAfterQuiet else { return }
        // This runs on the quiet task, which has done its work: forget it, so that closing the socket
        // does not cancel the task it runs on.
        quiet = nil
        await complete()
    }

    /// The connection dropped with no close frame.
    private func connectionEndedUnexplained() async {
        if nothingIsMissing {
            await complete()
        } else {
            await fail(dropFailure)
        }
    }

    /// Ends the stream as completed. Meta has no frame that says how long the finalisation took, so the
    /// time up to the provider's last word (or to `endStream`) is reported.
    private func complete() async {
        if case .meta = dialect, phase == .finishing, !finalizationReported {
            reportFinalization(milliseconds: (lastActivityAt - finishBeganAt) * 1000)
        }
        await end(.completed, failure: nil, closeCode: CloseCode.normal)
    }

    private func startCancelled() async {
        await failStart(CancellationError())
    }

    // MARK: - Ending

    /// Ends the stream because of `failure`. During the start, `start()` throws it; after a successful
    /// start it is the last event.
    private func fail(_ failure: SpeechStreamFailure, closeCode: Int = CloseCode.goingAway) async {
        switch phase {
        case .starting:
            await failStart(failure, closeCode: closeCode)
        case .streaming, .finishing:
            await end(.interrupted, failure: failure, closeCode: closeCode)
        case .idle, .ended:
            break
        }
    }

    /// Ends a start that did not reach "ready": the socket is closed, `events` finishes, `start()` throws.
    private func failStart(_ error: any Error, closeCode: Int = CloseCode.goingAway) async {
        guard phase == .starting else { return }
        phase = .ended
        await closeSocket(code: closeCode)
        continuation.finish()
        markTerminated()
        resolveStart(.failure(error))
    }

    /// Ends a stream that had started. The socket is closed first, then the usage stream is finished
    /// exactly once, then the failure (if any) is delivered as the last event.
    private func end(_ ending: SpeechStreamEnding, failure: SpeechStreamFailure?, closeCode: Int) async {
        guard phase == .streaming || phase == .finishing else { return }
        phase = .ended
        self.ending = ending
        await closeSocket(code: closeCode)

        if usageOpen {
            usageOpen = false
            emit(.usage(.finish(id: usageID, completed: ending == .completed)))
        }
        if let failure {
            emit(.failed(failure))
        }
        continuation.finish()
        markTerminated()
    }

    /// Stops the limit, closes the socket and stops the reader. Called once, right after the phase
    /// became `ended`.
    private func closeSocket(code: Int) async {
        limit?.cancel()
        limit = nil
        quiet?.cancel()
        quiet = nil

        let open = connection
        connection = nil
        if let open {
            // Closing ends a `receive()` or a `send` that is waiting on this socket.
            await open.close(code: code, reason: "")
        }

        // Ends an `open` that has not answered yet. Done after the close because this may be the
        // reader itself.
        reader?.cancel()
        reader = nil
    }

    private func markTerminated() {
        terminated = true
        outbox.removeAll()
        let waiting = terminationWaiters + drainWaiters
        terminationWaiters.removeAll()
        drainWaiters.removeAll()
        for waiter in waiting {
            waiter.resume()
        }
    }

    private func waitUntilTerminated() async {
        guard !terminated else { return }
        await withCheckedContinuation { (waiter: CheckedContinuation<Void, Never>) in
            if terminated {
                waiter.resume()
            } else {
                terminationWaiters.append(waiter)
            }
        }
    }

    private func resolveStart(_ outcome: Result<Void, any Error>) {
        guard startOutcome == nil else { return }
        startOutcome = outcome
        if let waiter = startWaiter {
            startWaiter = nil
            SpeechStreamSession.resume(waiter, with: outcome)
        }
    }

    private static func resume(_ waiter: CheckedContinuation<Void, any Error>, with outcome: Result<Void, any Error>) {
        switch outcome {
        case .success:
            waiter.resume()
        case .failure(let error):
            waiter.resume(throwing: error)
        }
    }

    // MARK: - Dialect values

    /// The socket dropped, or a wait for it ran out: `L-STREAM-DROPPED` for the relay, the connection
    /// text for Meta.
    private var dropFailure: SpeechStreamFailure {
        switch dialect {
        case .relay:
            if let state = negotiation, case .interrupted(let failure) = state.endingAfterDrop() {
                return SpeechStreamFailure(.relay(failure))
            }
            return SpeechStreamFailure(.relay(RelayFailure.local(.streamDropped, origin: .stream)))
        case .meta:
            return SpeechStreamFailure(.meta(.connection))
        }
    }

    /// What a second `start()` throws.
    private var alreadyUsedFailure: SpeechStreamFailure {
        dropFailure
    }

    /// C-60 for the relay; the same 15 s for Meta.
    private var finalizeSeconds: TimeInterval {
        switch dialect {
        case .relay:
            return RelayConstants.speechFinalizeSeconds
        case .meta:
            return MetaRealtimeAPI.finalizeSeconds
        }
    }

    /// Exactly `{"type":"endStream"}` in both dialects (protocol 10.3, spec 02 section 5.5).
    private var endStreamMessage: String {
        MetaRealtimeAPI.endStreamMessage
    }

    private func emit(_ event: SpeechStreamEvent) {
        continuation.yield(event)
    }
}

/// The WebSocket close codes the phone sends.
private enum CloseCode {
    /// The stream ended the way the peer ended it.
    static let normal = 1000
    /// The phone cut the stream itself: a limit passed, a failure, or an abort.
    static let goingAway = 1001
    /// What the peer closes with when it ends a stream that asked to end: normal, going away, or no
    /// status code at all (1005).
    static let clean: Set<Int> = [1000, 1001, 1005]
}
