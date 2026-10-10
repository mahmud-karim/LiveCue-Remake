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
}
