import Foundation
import LiveCueCore
import Observation

/// Reading a payload, and verify and pair (architecture 4.9).
@MainActor @Observable final class PairingController {
    private(set) var isVerifying: Bool = false

    init(gateway: any RelayGateway, store: any PairingStoring, monitor: RelayMonitor) {
        // STUB(WP-09)
    }

    /// Parse only. Nothing is saved or sent. Throws `PairingPayloadError`.
    func readPayload(_ text: String) throws -> PairingPayload {
        // STUB(WP-09)
        try PairingPayload.parse(text)
    }

    /// Canonicalises, calls route 2, and on 200 saves and tells the monitor. Throws `PairingPayloadError`
    /// or `RelayFailure`; a previous pairing is untouched on any failure.
    func verifyAndPair(address: String, token: String) async throws {
        // STUB(WP-09)
        throw RelayFailure.local(.network)
    }

    /// Added by the review. Pure: nothing is sent or saved. Non-nil when a pairing is saved and the host
    /// of `address` (after the manual-entry rules) differs from the saved host; nil when nothing is
    /// saved, when the hosts are equal, and when `address` does not parse (the verify call reports that).
    func replacement(forAddress address: String) -> PairingReplacement? {
        // STUB(WP-09)
        nil
    }
}
