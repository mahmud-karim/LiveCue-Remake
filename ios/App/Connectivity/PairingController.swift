import Foundation
import LiveCueCore
import Observation

/// Reading a payload, and verify and pair (architecture 4.9).
@MainActor @Observable final class PairingController {
    private(set) var isVerifying: Bool = false

    private let gateway: any RelayGateway
    private let store: any PairingStoring
    private let monitor: RelayMonitor

    init(gateway: any RelayGateway, store: any PairingStoring, monitor: RelayMonitor) {
        self.gateway = gateway
        self.store = store
        self.monitor = monitor
    }

    /// Parse only. Nothing is saved or sent. Throws `PairingPayloadError`.
    func readPayload(_ text: String) throws -> PairingPayload {
        try PairingPayload.parse(text)
    }

    /// Canonicalises, calls route 2, and on 200 saves and tells the monitor. Throws `PairingPayloadError`
    /// or `RelayFailure`; a previous pairing is untouched on any failure.
    ///
    /// Two more errors can leave it, and neither has a text of its own: `CancellationError`, when the
    /// caller's task was cancelled or a verification is already running, and the store's error, when the
    /// PC accepted the pairing and the phone could not save it.
    func verifyAndPair(address: String, token: String) async throws {
        // Invalid input is reported before anything is sent.
        let candidate = try PairingPayload.manual(address: address, token: token)
        guard !isVerifying else {
            throw CancellationError()
        }

        isVerifying = true
        defer { isVerifying = false }

        do {
            try await gateway.verifyPairing(candidate)
        } catch let failure as RelayFailure {
            throw failure
        } catch let cancellation as CancellationError {
            throw cancellation
        } catch {
            throw RelayFailure.local(.network)
        }

        // Only now, after the relay's 200, is anything saved. A store that refuses keeps the old pairing.
        try store.save(candidate)
        monitor.pairingSaved(candidate)
    }

    /// Added by the review. Pure: nothing is sent or saved. Non-nil when a pairing is saved and the host
    /// of `address` (after the manual-entry rules) differs from the saved host; nil when nothing is
    /// saved, when the hosts are equal, and when `address` does not parse (the verify call reports that).
    func replacement(forAddress address: String) -> PairingReplacement? {
        guard let saved = store.load() else { return nil }
        // The manual-entry rules need a token of the right shape beside the address. This one is a
        // stand-in: it is never sent and never saved, and only the endpoint of the result is read.
        let standIn = String(repeating: "A", count: RelayConstants.tokenLength)
        guard let entered = try? PairingPayload.manual(address: address, token: standIn) else { return nil }

        let currentHost = saved.relayEndpoint.host
        let newHost = entered.relayEndpoint.host
        guard newHost != currentHost else { return nil }
        return PairingReplacement(currentHost: currentHost, newHost: newHost)
    }
}
