import Foundation
import LiveCueCore

/// The relay of the test seam (section 4.9): health, the Codex catalogue and the PC model, with no request.
///
/// It answers as the relay of protocol 2 would. The PC model follows the state machine of protocol 9.3
/// with the messages of 9.4; time is read from the clock, and an operation ends when a later call finds
/// that its time has passed, so nothing runs in the background. With `pcOffline` the PC is switched off:
/// every route gives what an unreachable PC gives.
actor FixtureRelayGateway: RelayGateway {
    private enum Change {
        case start, stop
    }

    /// A start or a stop that is running (protocol 9.3, rule 1).
    private struct ModelOperation {
        let change: Change
        let model: SpeechProvider
        let startedAt: TimeInterval
        let duration: TimeInterval
    }

    /// A failed start, remembered for C-48 (protocol 9.3, rule 3).
    private struct LatchedError {
        let model: SpeechProvider
        let since: TimeInterval
    }

    private let mode: TestMode
    private let clock: any AppClock

    private var loadedModel: SpeechProvider?
    private var operation: ModelOperation?
    private var latchedError: LatchedError?

    init(mode: TestMode, clock: any AppClock) {
        self.mode = mode
        self.clock = clock
    }

    // MARK: - RelayGateway

    func health(_ pairing: PairingPayload) async throws -> RelayHealth {
        try requireReachablePC()
        return RelayHealth(relayVersion: FixtureRelayGateway.relayVersion)
    }

    func verifyPairing(_ pairing: PairingPayload) async throws {
        try requireReachablePC()
    }

    func models(_ pairing: PairingPayload) async throws -> [AssistantModelOption] {
        try requireReachablePC()
        if mode.pcCatalogSlow {
            try await clock.sleep(seconds: FixtureValues.catalogueSlowSeconds)
        }
        return FixtureValues.codexCatalogue
    }

    func localModelStatus(_ pairing: PairingPayload) async throws -> PCModelStatus {
        try requireReachablePC()
        return currentStatus()
    }

    func startLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        try requireReachablePC()
        guard provider.isPCLocal else {
            throw RelayFailure.wire(.invalidModel)
        }
        settle()
        guard operation == nil else {
            throw RelayFailure.wire(.modelOperationInProgress)
        }
        // The requested model is already loaded: nothing needs doing (200).
        if loadedModel == provider {
            return currentStatus()
        }
        // Starting replaces whatever was loaded, and a new operation ends an error (202).
        loadedModel = nil
        latchedError = nil
        operation = ModelOperation(
            change: .start,
            model: provider,
            startedAt: clock.uptime(),
            duration: mode.pcModelSlowStart ? FixtureValues.pcModelSlowStartSeconds : FixtureValues.pcModelChangeSeconds
        )
        return currentStatus()
    }

    func stopLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        try requireReachablePC()
        guard provider.isPCLocal else {
            throw RelayFailure.wire(.invalidModel)
        }
        settle()
        guard operation == nil else {
            throw RelayFailure.wire(.modelOperationInProgress)
        }
        if let loadedModel, loadedModel != provider {
            throw RelayFailure.wire(.modelMismatch)
        }
        latchedError = nil
        operation = ModelOperation(
            change: .stop,
            model: provider,
            startedAt: clock.uptime(),
            duration: FixtureValues.pcModelChangeSeconds
        )
        return currentStatus()
    }

    /// The answer of the test seam is `FixtureAnswerService`. This is what the relay route gives for the
    /// same request: the fixed answer at once, with the selection echoed (protocol 8.3).
    func assist(_ request: AssistRequest, pairing: PairingPayload) async throws -> AssistResponse {
        try requireReachablePC()
        guard let assistant = request.assistant else {
            throw RelayFailure.wire(.invalidAssistant)
        }
        try requireOffered(assistant)

        var response = AssistResponse(
            detectedQuestion: FixtureValues.segmentText,
            answer: FixtureValues.answer,
            details: FixtureValues.details,
            // The relay never forwards a marker: the phone owns it.
            memory: SessionMemory(summary: FixtureValues.answer, facts: [], openQuestions: [], throughSegmentId: nil)
        )
        var execution = FixtureValues.execution
        execution.model = assistant.model
        execution.reasoningEffort = assistant.reasoningEffort
        response.execution = execution
        return response
    }

    func summarize(_ request: SummaryRequest, pairing: PairingPayload) async throws -> SummaryResponse {
        try requireReachablePC()
        try requireOffered(request.assistant)
        return SummaryResponse(
            title: FixtureRelayGateway.summaryTitle,
            summary: FixtureValues.answer,
            keyPoints: [FixtureValues.details],
            actionItems: []
        )
    }

    /// Nothing is ever in flight here, so there is nothing to cancel. Both answers of route 9 count as done.
    func cancel(requestId: UUID, pairing: PairingPayload) async {}

    // MARK: - Checks

    /// A PC that is switched off answers nothing (protocol 13.6 row 5).
    private func requireReachablePC() throws {
        if mode.pcOffline {
            throw RelayFailure.local(.unreachable)
        }
    }

    /// Protocol 7.3: the model must be offered, with that effort.
    private func requireOffered(_ assistant: AssistantConfiguration) throws {
        guard FixtureValues.codexCatalogue.supports(assistant) else {
            throw RelayFailure.wire(.assistantSelectionUnavailable)
        }
    }

    // MARK: - PC model

    /// Ends an operation whose time has passed, and an error that was latched long enough.
    private func settle() {
        let now = clock.uptime()
        if let running = operation, now - running.startedAt >= running.duration {
            operation = nil
            switch running.change {
            case .start:
                if mode.pcModelStartError {
                    loadedModel = nil
                    latchedError = LatchedError(model: running.model, since: running.startedAt + running.duration)
                } else {
                    loadedModel = running.model
                }
            case .stop:
                loadedModel = nil
            }
        }
        if let latched = latchedError, now - latched.since >= FixtureRelayGateway.errorLatchSeconds {
            latchedError = nil
        }
    }

    /// The status object of protocol 9.1. `model` is nil exactly when the state is "stopped", and
    /// `elapsedSeconds` counts whole seconds of the running operation.
    private func currentStatus() -> PCModelStatus {
        settle()
        if let running = operation {
            let elapsed = Int(max(0, clock.uptime() - running.startedAt))
            switch running.change {
            case .start:
                return PCModelStatus(
                    state: "starting",
                    model: running.model.rawValue,
                    busy: false,
                    elapsedSeconds: elapsed,
                    message: FixtureRelayGateway.startingMessage
                )
            case .stop:
                return PCModelStatus(
                    state: "stopping",
                    model: running.model.rawValue,
                    busy: false,
                    elapsedSeconds: elapsed,
                    message: FixtureRelayGateway.stoppingMessage
                )
            }
        }
        if let loadedModel {
            return PCModelStatus(state: "ready", model: loadedModel.rawValue, message: FixtureRelayGateway.readyMessage)
        }
        if let latched = latchedError {
            return PCModelStatus(state: "error", model: latched.model.rawValue, message: FixtureRelayGateway.startFailedMessage)
        }
        return PCModelStatus(state: "stopped", message: FixtureRelayGateway.stoppedMessage)
    }

    // MARK: - Texts and values

    private static let relayVersion = "1.0.0"
    private static let summaryTitle = "Local transcription"
    /// C-48.
    private static let errorLatchSeconds: TimeInterval = 120

    /// MSG-3. MSG-1 is never sent on the wire.
    private static let stoppedMessage = "PC model is stopped. Start opens Docker Desktop if needed."
    /// MSG-4.
    private static let readyMessage = "Model loaded on the PC GPU. Ready for live transcription."
    /// MSG-6.
    private static let startingMessage = "Starting Docker and loading the PC model. First load can take several minutes."
    /// MSG-7.
    private static let stoppingMessage = "Unloading the PC model to free GPU memory…"
    /// MSG-8.
    private static let startFailedMessage = "Could not load the PC model. Check Docker Desktop, available GPU memory and the installed model weights, then retry."
}
