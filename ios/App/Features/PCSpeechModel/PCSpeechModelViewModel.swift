import Foundation
import LiveCueCore
import Observation

/// PC speech model (screens 8): start, stop and watch the speech model on the PC.
///
/// The status, its message and the lock are the relay monitor's. This class only words them.
@MainActor @Observable final class PCSpeechModelViewModel {
    private let speech: SpeechSettings
    private let relay: RelayMonitor
    /// True between `appeared()` and `disappeared()`, so the two calls on the monitor stay in pairs.
    @ObservationIgnored private var isObservingPCModel = false

    init(speech: SpeechSettings, relay: RelayMonitor) {
        self.speech = speech
        self.relay = relay
    }

    /// The provider's display name.
    var title: String {
        speech.provider.displayName
    }

    /// "Stopped", "Starting", "Ready", "Stopping", "Error", "Unavailable"
    var stateText: String {
        guard let status = relay.pcModelStatus else { return "Unavailable" }
        switch status.state {
        case "stopped": return "Stopped"
        case "starting": return "Starting"
        case "ready": return "Ready"
        case "stopping": return "Stopping"
        case "error": return "Error"
        default: return "Unavailable"
        }
    }

    /// A command is on its way to the PC, or the PC reports starting or stopping.
    var isChanging: Bool {
        relay.isPCModelCommandInFlight || relay.pcModelStatus?.isChanging == true
    }

    var isError: Bool {
        relay.pcModelStatus?.state == "error"
    }

    /// "N s" while changing.
    var elapsedText: String? {
        guard let status = relay.pcModelStatus, status.isChanging else { return nil }
        return "\(status.elapsedSeconds) s"
    }

    var message: String {
        relay.pcModelMessage
    }

    /// "Currently loaded: <id>"
    var otherLoadedModel: String? {
        guard let other = relay.pcModelStatus?.otherLoadedModel(than: speech.provider) else { return nil }
        return "Currently loaded: " + other
    }

    /// Ready and busy: the chip "In use".
    var isInUse: Bool {
        guard let status = relay.pcModelStatus else { return false }
        return status.state == "ready" && status.busy
    }

    /// Not locked and not already ready for this provider.
    var canStart: Bool {
        let provider = speech.provider
        guard provider.isPCLocal, !relay.isPCModelLocked else { return false }
        return !relay.isReady(for: provider)
    }

    /// Not locked, status known, not stopped, the loaded model is this provider.
    var canStop: Bool {
        let provider = speech.provider
        guard provider.isPCLocal, !relay.isPCModelLocked, let status = relay.pcModelStatus else { return false }
        return status.state != "stopped" && status.model == provider.rawValue
    }

    func start() async {
        await relay.startPCModel()
    }

    func stop() async {
        await relay.stopPCModel()
    }

    func refresh() async {
        await relay.refreshPCModel()
    }

    /// The status is refreshed every 2 seconds while the screen is visible.
    func appeared() {
        guard !isObservingPCModel else { return }
        isObservingPCModel = true
        relay.beginPCModelObservation()
    }

    func disappeared() {
        guard isObservingPCModel else { return }
        isObservingPCModel = false
        relay.endPCModelObservation()
    }
}
