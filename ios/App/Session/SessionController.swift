import Foundation
import LiveCueCore
import Observation

@MainActor struct SessionDependencies {
    let transcribers: any SpeechTranscriberFactory
    let answers: any AnswerServiceProviding
    let sessions: any SessionStoring
    let secrets: any SecretStoring
    let pairingStore: any PairingStoring
    let permissions: any PermissionProviding
    let clock: any AppClock
    let app: AppModel
    let activity: SessionActivity
    let speech: SpeechSettings
    let assistant: AssistantSettings
    let relay: RelayMonitor
    let models: OnDeviceModelManager
    let history: HistoryModel
}

/// What the last request that reached step 8 of the Assist pipeline sent, and the marker recorded when
/// its text was built (section 4.3).
struct RememberedAssist {
    let request: AssistRequest
    /// The id of the session's last segment when the fresh request was built.
    let marker: UUID?
}

/// One end-of-session summary in flight (section 4.4).
struct SummaryRun {
    let requestID: UUID
    let task: Task<Void, Never>
}

/// The session state machine, its tick and its event handling (section 4.1).
@MainActor @Observable final class SessionController {
    // Observable state. Views read it; only this class writes it.
    private(set) var phase: SessionPhase = .idle
    /// nil in idle and preflight.
    private(set) var session: Session? = nil
    /// The provider this session started with.
    private(set) var provider: SpeechProvider? = nil
    private(set) var partialText: String = ""
    /// 0 while not listening.
    private(set) var level: Float = 0
    private(set) var elapsedSeconds: Int = 0
    private(set) var transcriberStatus: String = ""
    private(set) var notice: SessionNotice? = nil
    private(set) var pausedAutomatically: Bool = false
    private(set) var isAssisting: Bool = false
    private(set) var assistStage: AssistStage? = nil
    /// Kept after a failure until the next Assist starts.
    private(set) var streamingAnswer: String = ""
    private(set) var assistError: String? = nil
    /// The turn marked "Latest" in this session.
    private(set) var latestTurnID: UUID? = nil
    /// A previous request is remembered.
    private(set) var canRetry: Bool = false
    /// Increments on every entry into `listening` (haptic trigger).
    private(set) var listeningStartedCount: Int = 0
    /// Increments at the first streamed text or saved answer of an Assist.
    private(set) var answerArrivedCount: Int = 0
    private(set) var assistFailedCount: Int = 0

    /// preflight, connecting, pausing, resuming, ending
    var isTransitioning: Bool {
        switch phase {
        case .preflight, .connecting, .pausing, .resuming, .ending:
            return true
        case .idle, .listening, .paused:
            return false
        }
    }

    /// session != nil
    var showsLiveScreen: Bool {
        session != nil
    }

    /// A session exists, not transitioning, not assisting.
    var controlsEnabled: Bool {
        session != nil && !isTransitioning && !isAssisting
    }

    /// "$0.00000 USD" for Meta sessions, else nil.
    var estimatedCost: String? {
        guard provider == .meta, let usage else { return nil }
        return SessionTexts.cost(usage)
    }

    /// Read by the two extension files; nothing outside `Session/` uses it.
    let dependencies: SessionDependencies

    /// The usage of the session in progress. It is copied into the session at every save.
    private var usage: TranscriptionUsage? = nil

    private let persistence: SessionPersistence
    @ObservationIgnored private var transcriber: (any SpeechTranscriber)?
    @ObservationIgnored private var pump: TranscriberEventPump?
    @ObservationIgnored private var tickTask: Task<Void, Never>?
    /// The generation of the current transcriber start; nil once it is retired.
    @ObservationIgnored private var currentGeneration: UUID?
    /// A `failed` of the current generation that arrived while its start call had not been applied yet.
    @ObservationIgnored private var deferredFailure: TranscriberFailure?
    /// True once the first save was made (T-04). Nothing is saved before.
    @ObservationIgnored private var hasSaved = false
    /// The last `lastChunk` event, for the next Assist's timing record.
    @ObservationIgnored private var lastChunkMilliseconds: Double?
    /// True once the Assist in progress has shown text or saved its answer.
    @ObservationIgnored private var answerHasArrived = false
    @ObservationIgnored private(set) var rememberedAssist: RememberedAssist?
    /// The summaries in flight, by session id. Written by `SessionController+Summary.swift`.
    @ObservationIgnored var summaryRuns: [UUID: SummaryRun] = [:]

    init(_ dependencies: SessionDependencies) {
        self.dependencies = dependencies
        self.persistence = SessionPersistence(store: dependencies.sessions, clock: dependencies.clock)
    }

    // Intents. Each is single-flight: it returns at once when its guard fails.

    func start() async {
        // T-01
        guard phase == .idle else { return }
        phase = .preflight
        let speechProvider = dependencies.speech.provider
        if let message = await startCheckFailure(for: speechProvider) {
            // T-02: nothing was created.
            phase = .idle
            dependencies.app.presentAlert(message)
            return
        }

        // T-03
        resetSessionState()
        session = Session(startedAt: dependencies.clock.now())
        provider = speechProvider
        usage = TranscriptionUsage(provider: speechProvider.rawValue)
        dependencies.activity.isActive = true
        dependencies.app.sessionDidStart()
        let transcriber = dependencies.transcribers.makeTranscriber(for: speechProvider)
        self.transcriber = transcriber
        pump = TranscriberEventPump(transcriber: transcriber) { [weak self] event in
            self?.apply(event)
        }
        startTick()
        transcriberStatus = TranscriberStatusText.connecting(speechProvider)
        let generation = UUID()
        currentGeneration = generation
        phase = .connecting

        do {
            try await transcriber.start(TranscriberStart(generation: generation, streamOffsetSeconds: 0))
        } catch {
            await discardAfterFailedStart(error)
            return
        }

        // T-04
        await pump?.waitUntilCaughtUp()
        hasSaved = true
        listeningStartedCount += 1
        phase = .listening
        save()
        applyDeferredFailure()
    }

    /// Pause when listening, Resume when paused.
    func togglePause() async {
        guard !isAssisting else { return }
        switch phase {
        case .listening:
            await pause()
        case .paused:
            await resume()
        case .idle, .preflight, .connecting, .pausing, .resuming, .ending:
            return
        }
    }

    func stop() async {
        guard !isAssisting, session != nil else { return }
        switch phase {
        case .listening:
            // T-14
            phase = .ending
            level = 0
            transcriberStatus = TranscriberStatusText.finishing
            var finalizeFailed = false
            do {
                try await transcriber?.finalize()
            } catch {
                finalizeFailed = true
            }
            await pump?.waitUntilCaughtUp()
            await end(finalizeFailed: finalizeFailed)
        case .paused:
            // T-15: what was heard was finalised by the pause.
            phase = .ending
            await end(finalizeFailed: false)
        case .idle, .preflight, .connecting, .pausing, .resuming, .ending:
            return
        }
    }

    /// Returns when every transcriber event emitted so far was applied and no save is queued or running.
    /// The app never needs it; tests wait with it.
    func waitUntilSettled() async {
        await pump?.waitUntilCaughtUp()
        await persistence.flush()
    }

    // MARK: - Start checks (section 4.1.2)

    /// The message of the first check that fails, or nil when all pass.
    private func startCheckFailure(for speechProvider: SpeechProvider) async -> String? {
        if speechProvider == .meta, dependencies.secrets.read(.metaAPIKey) == nil {
            return MetaSpeechError.missingKey.errorDescription ?? ""
        }

        if speechProvider.isPCLocal {
            let relay = dependencies.relay
            await relay.refreshPCModel()
            var canStart = false
            if let status = relay.pcModelStatus {
                canStart = status.isReady(for: speechProvider) && !status.busy && !relay.isPCModelCommandInFlight
            }
            guard canStart else {
                return SessionTexts.pcModelNotReady(homeLabel: relay.homeLabel(for: speechProvider))
            }

            guard dependencies.pairingStore.load() != nil else {
                return RelayMessages.text(for: RelayLocalCondition.notPaired)
            }
            if !relay.isOnline {
                await relay.checkHealthNow()
            }
            guard relay.isOnline else {
                return relay.connectionMessage ?? RelayMessages.text(for: RelayLocalCondition.unreachable)
            }
        }

        guard await dependencies.permissions.requestMicrophone() else {
            return SessionTexts.microphoneDenied
        }
        return nil
    }

    // MARK: - Transitions

    /// T-05: the transcriber did not start. Nothing was saved, and nothing is left.
    private func discardAfterFailedStart(_ error: any Error) async {
        currentGeneration = nil
        await transcriber?.shutdown()
        releaseTranscriber()
        resetSessionState()
        dependencies.activity.isActive = false
        dependencies.app.sessionDidEnd()
        phase = .idle
        // A cancellation is never shown.
        if !(error is CancellationError) {
            dependencies.app.presentAlert(SessionTexts.startFailure(error))
        }
    }

    /// T-06, then T-07 or T-08.
    private func pause() async {
        phase = .pausing
        // The level is 0 whenever the session is not listening.
        level = 0
        transcriberStatus = TranscriberStatusText.finishing
        var finalizeFailed = false
        do {
            try await transcriber?.finalize()
        } catch {
            finalizeFailed = true
        }
        await pump?.waitUntilCaughtUp()

        currentGeneration = nil
        level = 0
        if finalizeFailed {
            notice = SessionNotice(kind: .finalizeFailed, message: SessionTexts.finalizeFailed)
        }
        phase = .paused
        save()
    }

    /// T-11, then T-12 or T-13.
    private func resume() async {
        guard let provider, let transcriber else { return }
        phase = .resuming
        partialText = ""
        let offset = Double(elapsedSeconds)
        let generation = UUID()
        currentGeneration = generation
        deferredFailure = nil
        transcriberStatus = TranscriberStatusText.connecting(provider)

        do {
            try await transcriber.start(TranscriberStart(generation: generation, streamOffsetSeconds: offset))
        } catch {
            // T-13
            enterAutomaticPause(message: SessionTexts.startFailure(error))
            return
        }

        // T-12
        await pump?.waitUntilCaughtUp()
        notice = nil
        pausedAutomatically = false
        listeningStartedCount += 1
        phase = .listening
        save()
        applyDeferredFailure()
    }

    /// T-16
    private func end(finalizeFailed: Bool) async {
        currentGeneration = nil
        deferredFailure = nil
        await transcriber?.shutdown()
        releaseTranscriber()

        guard var ended = session else {
            phase = .idle
            return
        }
        ended.endedAt = dependencies.clock.now()
        ended.title = ended.finalTitle()
        ended.transcriptionUsage = usage
        session = ended
        persistence.schedule(ended)
        await persistence.flush()
        await dependencies.history.reload()

        // The answer service selected at Stop decides whether a summary is asked for.
        let selection = dependencies.assistant.configuration
        resetSessionState()
        dependencies.activity.isActive = false
        dependencies.app.sessionDidEnd()
        phase = .idle
        if finalizeFailed {
            dependencies.app.presentAlert(SessionTexts.finalizeFailed)
        }
        startSummary(for: ended, assistant: selection)
    }

    /// The end of T-09, T-10 and T-13: paused, with the reason in the notice.
    private func enterAutomaticPause(message: String) {
        currentGeneration = nil
        deferredFailure = nil
        level = 0
        pausedAutomatically = true
        notice = SessionNotice(kind: .recordingPaused, message: message)
        transcriberStatus = TranscriberStatusText.disconnected
        phase = .paused
        save()
    }

    /// T-09 and T-10. Called only while listening, for the current generation.
    private func pauseAfterFailure(_ failure: TranscriberFailure) {
        guard let provider, let sessionID = session?.id else { return }
        if provider.streamsAudio {
            // T-09: the transcriber has already torn itself down.
            enterAutomaticPause(message: failure.message)
            return
        }

        // T-10: an on-device run stays open, so that what was recognised is kept.
        phase = .pausing
        level = 0
        let transcriber = self.transcriber
        Task { [weak self] in
            _ = try? await transcriber?.finalize()
            await self?.finishOnDeviceFailure(failure, sessionID: sessionID)
        }
    }

    private func finishOnDeviceFailure(_ failure: TranscriberFailure, sessionID: UUID) async {
        guard session?.id == sessionID, phase == .pausing else { return }
        await pump?.waitUntilCaughtUp()
        guard session?.id == sessionID, phase == .pausing else { return }
        enterAutomaticPause(message: failure.message)
    }

    /// A stream can fail between `start` returning and the barrier. That `failed` belongs to the
    /// generation that is now listening, so it is applied as soon as the phase says so.
    private func applyDeferredFailure() {
        guard let failure = deferredFailure else { return }
        deferredFailure = nil
        pauseAfterFailure(failure)
    }

    private func releaseTranscriber() {
        pump?.stop()
        pump = nil
        tickTask?.cancel()
        tickTask = nil
        transcriber = nil
    }

    /// The state of a session that has not started. Used when one starts and when one ends.
    private func resetSessionState() {
        session = nil
        provider = nil
        usage = nil
        partialText = ""
        level = 0
        elapsedSeconds = 0
        transcriberStatus = ""
        notice = nil
        pausedAutomatically = false
        streamingAnswer = ""
        assistError = nil
        latestTurnID = nil
        canRetry = false
        rememberedAssist = nil
        currentGeneration = nil
        deferredFailure = nil
        hasSaved = false
        lastChunkMilliseconds = nil
    }

    // MARK: - Tick (T-17)

    private func startTick() {
        tickTask?.cancel()
        let clock = dependencies.clock
        tickTask = Task { [weak self] in
            while true {
                do {
                    try await clock.sleep(seconds: SessionController.tickSeconds)
                } catch {
                    return
                }
                if Task.isCancelled { return }
                guard let self else { return }
                self.tick()
            }
        }
    }

    private func tick() {
        guard let session else { return }
        let elapsed = dependencies.clock.now().timeIntervalSince(session.startedAt)
        elapsedSeconds = max(0, Int(elapsed.rounded(.down)))
        save()
    }

    private static let tickSeconds: TimeInterval = 1

    // MARK: - Saving (section 4.1.1, "Save")

    /// Copies the usage into the session and hands it to the save queue. Nothing happens before the
    /// first save, which T-04 makes.
    private func save() {
        guard hasSaved, var current = session else { return }
        if current.transcriptionUsage != usage {
            current.transcriptionUsage = usage
            session = current
        }
        persistence.schedule(current)
    }

    // MARK: - Events (section 4.1.3)

    private func apply(_ event: TranscriberEvent) {
        // T-19: another generation, or the generation was retired.
        guard let currentGeneration, event.generation == currentGeneration, session != nil else { return }

        // T-18
        switch event.kind {
        case .status(let text):
            transcriberStatus = text
        case .partial(let text):
            partialText = text
        case .segment(let segment):
            append(segment)
        case .level(let value):
            if phase == .listening {
                level = value
            }
        case .usage(let update):
            usage?.apply(update)
        case .lastChunk(let milliseconds):
            lastChunkMilliseconds = milliseconds
        case .failed(let failure):
            receive(failure)
        }
    }

    private func append(_ segment: TranscriptSegment) {
        guard var current = session else { return }
        current.segments.append(segment)
        if provider?.streamsAudio == true {
            // Stable: segments that start at the same time keep the order they arrived in.
            current.segments = current.segments.enumerated()
                .sorted { first, second in
                    if first.element.startSeconds != second.element.startSeconds {
                        return first.element.startSeconds < second.element.startSeconds
                    }
                    return first.offset < second.offset
                }
                .map { $0.element }
        }
        session = current
        save()
    }

    private func receive(_ failure: TranscriberFailure) {
        switch phase {
        case .listening:
            pauseAfterFailure(failure)
        case .connecting, .resuming:
            // The start call has not been applied yet. If it throws, its own failure is the outcome
            // and this one is forgotten; if it returned, this one follows T-04 or T-12.
            deferredFailure = failure
        case .idle, .preflight, .pausing, .paused, .ending:
            // T-20: the call in progress reports its own outcome.
            break
        }
    }

    // MARK: - State changes of the Assist pipeline
    //
    // `SessionController+Assist.swift` is an extension in its own file, so it cannot assign the
    // `private(set)` state above. It changes it through these methods.

    /// Step 1.
    func clearAssistCard() {
        assistError = nil
        streamingAnswer = ""
    }

    /// Step 2: a precondition is reported on the card and the Assist does not start.
    func showAssistPrecondition(_ message: String) {
        assistError = message
    }

    /// Step 3.
    func beginAssist(stage: AssistStage) {
        isAssisting = true
        dependencies.activity.isAssisting = true
        assistStage = stage
        answerHasArrived = false
    }

    func setAssistStage(_ stage: AssistStage) {
        assistStage = stage
    }

    /// Step 4, for Voz: the seconds the transcription took, once its segment was applied.
    func transcribePendingAudio() async throws -> TimeInterval {
        guard let transcriber else { return 0 }
        let seconds = try await transcriber.transcribePending()
        await pump?.waitUntilCaughtUp()
        return seconds
    }

    /// The last live chunk's processing time, for the timing record of a Parakeet or Fluid Speech Mini
    /// session.
    var lastLiveChunkMilliseconds: Double? {
        lastChunkMilliseconds
    }

    /// Step 8.
    func rememberAssist(_ request: AssistRequest, marker: UUID?) {
        rememberedAssist = RememberedAssist(request: request, marker: marker)
        canRetry = true
    }

    /// Step 9: the whole streamed text so far. Ignored for another session, and outside an Assist.
    func showStreamedAnswer(_ text: String, forSession id: UUID) {
        guard isAssisting, session?.id == id else { return }
        streamingAnswer = text
        assistStage = .replying
        noteAnswerArrived()
    }

    /// Steps 10 and 11. False when the session ended or changed: the result is dropped.
    @discardableResult
    func saveAnswer(_ turn: AssistantTurn, memory: SessionMemory, forSession id: UUID) -> Bool {
        guard var current = session, current.id == id else { return false }
        current.assistantTurns.append(turn)
        current.memory = memory
        session = current
        latestTurnID = turn.id
        streamingAnswer = ""
        canRetry = true
        noteAnswerArrived()
        save()
        return true
    }

    /// Step 12. A nil message is a cancellation, which shows nothing.
    func failAssist(_ message: String?) {
        guard let message else { return }
        assistError = message
        assistFailedCount += 1
    }

    /// Step 13.
    func endAssist() {
        isAssisting = false
        dependencies.activity.isAssisting = false
        assistStage = nil
    }

    private func noteAnswerArrived() {
        guard !answerHasArrived else { return }
        answerHasArrived = true
        answerArrivedCount += 1
    }
}
