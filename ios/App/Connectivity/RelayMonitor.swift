import Foundation
import LiveCueCore
import Observation

/// Health and PC model polling, and the PC model commands (protocol 13.3; spec 01 section 3.7).
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

    var isPaired: Bool {
        pairing != nil
    }

    /// `PCRequirement.needsPC` for the current selections.
    var needsPC: Bool {
        // STUB(WP-09)
        false
    }

    /// Command in flight, PC changing, session active, PC busy, or not paired.
    var isPCModelLocked: Bool {
        // STUB(WP-09)
        true
    }

    init(
        gateway: any RelayGateway,
        pairingStore: any PairingStoring,
        clock: any AppClock,
        speech: SpeechSettings,
        assistant: AssistantSettings,
        activity: SessionActivity
    ) {
        // STUB(WP-09)
    }

    /// Loads the pairing and starts the two poll loops; idempotent.
    func start() {
        // STUB(WP-09)
    }

    /// One health check now, when needed.
    func appBecameActive() {
        // STUB(WP-09)
    }

    /// Provider or answer service changed: re-evaluate polling at once.
    func selectionChanged() {
        // STUB(WP-09)
    }

    /// "Reconnect" and the start check; joins a running check.
    @discardableResult
    func checkHealthNow() async -> Bool {
        // STUB(WP-09)
        false
    }

    /// Sets connected and "Connected. Your pairing is saved on this iPhone."
    func pairingSaved(_ pairing: PairingPayload) {
        // STUB(WP-09)
    }

    /// Reads the store again.
    func reloadPairing() {
        // STUB(WP-09)
    }

    /// A screen that shows the PC model appeared.
    func beginPCModelObservation() {
        // STUB(WP-09)
    }

    func endPCModelObservation() {
        // STUB(WP-09)
    }

    func refreshPCModel() async {
        // STUB(WP-09)
    }

    /// Refused locally while locked.
    func startPCModel() async {
        // STUB(WP-09)
    }

    func stopPCModel() async {
        // STUB(WP-09)
    }

    func isReady(for provider: SpeechProvider) -> Bool {
        // STUB(WP-09)
        false
    }

    /// Online, ready for it, not busy, no command in flight.
    func canStartSession(for provider: SpeechProvider) -> Bool {
        // STUB(WP-09)
        false
    }

    /// Spec 01 section 3.7, ten rows.
    func homeLabel(for provider: SpeechProvider) -> String {
        // STUB(WP-09)
        "Checking…"
    }
}
