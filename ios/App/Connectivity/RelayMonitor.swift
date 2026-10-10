import Foundation
import LiveCueCore
import Observation

/// Health and PC model polling, and the PC model commands (protocol 13.3; spec 01 section 3.7).
///
/// Nothing is polled before `start()`. When the PC is not needed, or nothing is paired, the monitor
/// sends nothing by itself (P2-220); only the explicit actions of the Pairing screen ("Reconnect",
/// "Verify and pair") reach the relay then.
@MainActor @Observable final class RelayMonitor {
    private(set) var pairing: PairingPayload? = nil
    private(set) var isOnline: Bool = false
    /// Follows the most recent check only (IOS-B11).
    private(set) var savedPCState: SavedPCState = .reconnecting
    /// nil until the first check ends.
    private(set) var connectionMessage: String? = nil
    private(set) var isCheckingHealth: Bool = false
    /// nil = unknown.
    private(set) var pcModelStatus: PCModelStatus? = nil
    private(set) var pcModelMessage: String = "Checking PC model…"
    private(set) var isPCModelCommandInFlight: Bool = false

    private let gateway: any RelayGateway
    private let pairingStore: any PairingStoring
    private let clock: any AppClock
    private let speech: SpeechSettings
    private let assistant: AssistantSettings
    private let activity: SessionActivity

    @ObservationIgnored private var isStarted = false
    /// `needsPC` as the last selection change left it.
    @ObservationIgnored private var wasPCNeeded = false
    @ObservationIgnored private var healthLoop: Task<Void, Never>?
    /// The health check in flight, with the PC model check that follows a success.
    @ObservationIgnored private var healthCheckTask: Task<Bool, Never>?
    /// Changes when a check starts and when the pairing changes, so a check of an older pairing is dropped.
    @ObservationIgnored private var healthCheckID = 0
    /// How many visible screens show the PC model.
    @ObservationIgnored private var observationCount = 0
    @ObservationIgnored private var pcModelLoop: Task<Void, Never>?
    /// The PC model status check in flight.
    @ObservationIgnored private var pcModelRefreshTask: Task<Void, Never>?

    var isPaired: Bool {
        pairing != nil
    }

    /// `PCRequirement.needsPC` for the current selections.
    var needsPC: Bool {
        PCRequirement.needsPC(speech: speech.provider, answers: assistant.configuration.provider)
    }

    /// Command in flight, PC changing, session active, PC busy, or not paired.
    var isPCModelLocked: Bool {
        if isPCModelCommandInFlight || activity.isActive || pairing == nil {
            return true
        }
        guard let status = pcModelStatus else { return false }
        return status.isChanging || status.busy
    }

    init(
        gateway: any RelayGateway,
        pairingStore: any PairingStoring,
        clock: any AppClock,
        speech: SpeechSettings,
        assistant: AssistantSettings,
        activity: SessionActivity
    ) {
        self.gateway = gateway
        self.pairingStore = pairingStore
        self.clock = clock
        self.speech = speech
        self.assistant = assistant
        self.activity = activity
        // The saved pairing is known from the first frame; `start()` reads it again.
        pairing = pairingStore.load()
        wasPCNeeded = PCRequirement.needsPC(speech: speech.provider, answers: assistant.configuration.provider)
    }

    /// Loads the pairing and starts the two poll loops; idempotent.
    func start() {
        guard !isStarted else { return }
        isStarted = true
        reloadPairing()
        wasPCNeeded = needsPC

        // One task for the life of the app. Its first pass is the check at launch.
        healthLoop = Task { [weak self, clock] in
            while self != nil {
                await self?.pollHealth()
                do {
                    try await clock.sleep(seconds: RelayConstants.healthPollSeconds)
                } catch {
                    return
                }
            }
        }
        updatePCModelLoop()
    }

    /// One health check now, when needed.
    func appBecameActive() {
        guard isStarted, needsPC, pairing != nil else { return }
        startExtraHealthCheck()
    }

    /// Provider or answer service changed: re-evaluate polling at once.
    func selectionChanged() {
        let isNeeded = needsPC
        let becameNeeded = isNeeded && !wasPCNeeded
        wasPCNeeded = isNeeded
        updatePCModelLoop()
        guard isStarted, becameNeeded, pairing != nil else { return }
        startExtraHealthCheck()
    }

    /// "Reconnect" and the start check; joins a running check.
    ///
    /// It asks whenever a pairing is saved, because the user or a start asked for it. False without a
    /// pairing, and nothing is sent then.
    @discardableResult
    func checkHealthNow() async -> Bool {
        await joinedHealthCheck()
    }

    /// Sets connected and "Connected. Your pairing is saved on this iPhone."
    func pairingSaved(_ pairing: PairingPayload) {
        if self.pairing != pairing {
            forgetWhatTheOldPairingShowed()
            self.pairing = pairing
        }
        isOnline = true
        savedPCState = .connected
        connectionMessage = RelayMonitor.connectedMessage
    }

    /// Reads the store again.
    func reloadPairing() {
        let stored = pairingStore.load()
        guard stored != pairing else { return }
        forgetWhatTheOldPairingShowed()
        pairing = stored
        isOnline = false
        savedPCState = .reconnecting
        connectionMessage = nil
    }

    /// A screen that shows the PC model appeared.
    func beginPCModelObservation() {
        observationCount += 1
        updatePCModelLoop()
    }

    func endPCModelObservation() {
        observationCount = max(0, observationCount - 1)
        updatePCModelLoop()
    }

    /// One status check. It does nothing for a provider that is not PC-local and while a command is in
    /// flight. While another check is in flight it sends nothing and returns when that check ends, so the
    /// status is fresh for whoever waited.
    func refreshPCModel() async {
        guard speech.provider.isPCLocal, !isPCModelCommandInFlight else { return }
        if let running = pcModelRefreshTask {
            await running.value
            return
        }
        guard let pairing else {
            pcModelStatus = nil
            pcModelMessage = RelayMonitor.pairFirstMessage
            return
        }

        let task = Task { [weak self, gateway] in
            let outcome: PCModelOutcome
            do {
                outcome = .status(try await gateway.localModelStatus(pairing))
            } catch let failure as RelayFailure {
                outcome = .failed(failure)
            } catch is CancellationError {
                outcome = .cancelled
            } catch {
                outcome = .failed(RelayFailure.local(.network))
            }
            self?.finishPCModelRefresh(outcome, wasCancelled: Task.isCancelled, checked: pairing)
        }
        pcModelRefreshTask = task
        await task.value
    }

    /// Refused locally while locked.
    func startPCModel() async {
        await runPCModelCommand(.start)
    }

    func stopPCModel() async {
        await runPCModelCommand(.stop)
    }

    func isReady(for provider: SpeechProvider) -> Bool {
        pcModelStatus?.isReady(for: provider) ?? false
    }

    /// Online, ready for it, not busy, no command in flight.
    func canStartSession(for provider: SpeechProvider) -> Bool {
        guard isOnline, !isPCModelCommandInFlight, let status = pcModelStatus else { return false }
        return status.isReady(for: provider) && !status.busy
    }

    /// Spec 01 section 3.7, ten rows.
    func homeLabel(for provider: SpeechProvider) -> String {
        guard pairing != nil else { return "Pair PC first" }
        guard isOnline else { return "PC offline" }
        guard let status = pcModelStatus else { return "Checking…" }
        return status.homeLabel(for: provider)
    }

    // MARK: - Texts

    private static let connectedMessage = "Connected. Your pairing is saved on this iPhone."
    private static let checkingPCModelMessage = "Checking PC model…"
    private static let pairFirstMessage = "Pair your PC first."

    // MARK: - Health

    private enum HealthOutcome {
        case online
        case failed(RelayFailure)
        case cancelled
    }

    /// One pass of the health loop: nothing is sent when the PC is not needed or nothing is paired, and
    /// the stored state is left as it was.
    private func pollHealth() async {
        guard needsPC, pairing != nil else { return }
        _ = await joinedHealthCheck()
    }

    /// An extra check outside the loop. It joins the check in flight.
    private func startExtraHealthCheck() {
        Task { [weak self] in
            _ = await self?.joinedHealthCheck()
        }
    }

    /// Joins the check in flight, or runs one. Checks never overlap. Returns `isOnline` as that check
    /// left it; false without a pairing.
    private func joinedHealthCheck() async -> Bool {
        if let running = healthCheckTask {
            return await running.value
        }
        guard let pairing else { return false }

        healthCheckID += 1
        let id = healthCheckID
        isCheckingHealth = true
        let task = Task { [weak self, gateway] () -> Bool in
            let outcome: HealthOutcome
            do {
                _ = try await gateway.health(pairing)
                outcome = .online
            } catch let failure as RelayFailure {
                outcome = .failed(failure)
            } catch is CancellationError {
                outcome = .cancelled
            } catch {
                outcome = .failed(RelayFailure.local(.network))
            }
            guard let self else { return false }
            return await self.finishHealthCheck(id: id, outcome: outcome)
        }
        healthCheckTask = task
        return await task.value
    }

    /// Applies the table of architecture 4.7, then lets the PC model check follow a success.
    private func finishHealthCheck(id: Int, outcome: HealthOutcome) async -> Bool {
        // The pairing changed while this check ran: its answer is about another PC.
        guard id == healthCheckID else { return isOnline }

        isCheckingHealth = false
        var cameOnline = false
        switch outcome {
        case .online:
            cameOnline = true
            isOnline = true
            savedPCState = .connected
            connectionMessage = RelayMonitor.connectedMessage
        case .failed(let failure):
            isOnline = false
            if failure.isCode(.unauthorized) {
                savedPCState = .needsAttention
                connectionMessage = RelayMessages.text(for: failure)
            } else {
                savedPCState = .reconnecting
                connectionMessage = RelayMessages.savedPairingHealthFailure(failure)
            }
        case .cancelled:
            break
        }

        let result = isOnline
        if cameOnline, speech.provider.isPCLocal {
            await refreshPCModel()
        }
        if id == healthCheckID {
            healthCheckTask = nil
        }
        return result
    }

    /// What the monitor knows belongs to the pairing it had. A check in flight for it is dropped.
    private func forgetWhatTheOldPairingShowed() {
        healthCheckID += 1
        healthCheckTask?.cancel()
        healthCheckTask = nil
        isCheckingHealth = false
        pcModelRefreshTask?.cancel()
        pcModelStatus = nil
        pcModelMessage = RelayMonitor.checkingPCModelMessage
    }

    // MARK: - PC model

    private enum PCModelOutcome {
        case status(PCModelStatus)
        case failed(RelayFailure)
        case cancelled
    }

    private enum PCModelCommand {
        case start, stop
    }

    /// Runs the PC model loop while a screen shows the PC model and a PC-local provider is selected.
    private func updatePCModelLoop() {
        let shouldRun = isStarted && observationCount > 0 && speech.provider.isPCLocal
        guard shouldRun else {
            pcModelLoop?.cancel()
            pcModelLoop = nil
            return
        }
        guard pcModelLoop == nil else { return }
        pcModelLoop = Task { [weak self, clock] in
            while self != nil, !Task.isCancelled {
                await self?.refreshPCModel()
                do {
                    try await clock.sleep(seconds: RelayConstants.pcModelPollSeconds)
                } catch {
                    return
                }
            }
        }
    }

    private func finishPCModelRefresh(_ outcome: PCModelOutcome, wasCancelled: Bool, checked: PairingPayload) {
        pcModelRefreshTask = nil
        // A check that a command or a new pairing ended, or whose provider is no longer PC-local, says
        // nothing.
        guard !wasCancelled, pairing == checked, speech.provider.isPCLocal else { return }
        switch outcome {
        case .status(let status):
            pcModelStatus = status
            pcModelMessage = status.message
        case .failed(let failure):
            pcModelStatus = nil
            pcModelMessage = RelayMessages.pcModelCheckFailure(failure)
        case .cancelled:
            break
        }
    }

    private func runPCModelCommand(_ command: PCModelCommand) async {
        let provider = speech.provider
        guard provider.isPCLocal, !isPCModelLocked, let pairing else { return }
        isPCModelCommandInFlight = true

        // A status check never overlaps a command (protocol 13.3): the one in flight is ended first.
        if let running = pcModelRefreshTask {
            running.cancel()
            await running.value
        }

        do {
            let status: PCModelStatus
            switch command {
            case .start:
                status = try await gateway.startLocalModel(provider, pairing: pairing)
            case .stop:
                status = try await gateway.stopLocalModel(provider, pairing: pairing)
            }
            if self.pairing == pairing {
                pcModelStatus = status
                pcModelMessage = status.message
            }
        } catch let failure as RelayFailure {
            // Only the message changes.
            pcModelMessage = RelayMessages.text(for: failure)
        } catch is CancellationError {
            // The caller went away. Nothing is shown.
        } catch {
            pcModelMessage = RelayMessages.text(for: RelayFailure.local(.network))
        }
        isPCModelCommandInFlight = false
    }
}
