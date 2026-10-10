import Foundation
import LiveCueCore

/// The relay of the test seam (section 4.9): health, the Codex catalogue and the PC model, with no request.
actor FixtureRelayGateway: RelayGateway {
    private let mode: TestMode
    private let clock: any AppClock

    init(mode: TestMode, clock: any AppClock) {
        self.mode = mode
        self.clock = clock
    }

    func health(_ pairing: PairingPayload) async throws -> RelayHealth {
        // STUB(WP-09)
        RelayHealth(relayVersion: "1.0.0")
    }

    func verifyPairing(_ pairing: PairingPayload) async throws {
        // STUB(WP-09)
    }

    func models(_ pairing: PairingPayload) async throws -> [AssistantModelOption] {
        // STUB(WP-09)
        FixtureValues.codexCatalogue
    }

    func localModelStatus(_ pairing: PairingPayload) async throws -> PCModelStatus {
        // STUB(WP-09)
        PCModelStatus(state: "stopped", message: "")
    }

    func startLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        // STUB(WP-09)
        PCModelStatus(state: "stopped", message: "")
    }

    func stopLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        // STUB(WP-09)
        PCModelStatus(state: "stopped", message: "")
    }

    func assist(_ request: AssistRequest, pairing: PairingPayload) async throws -> AssistResponse {
        // STUB(WP-09)
        throw RelayFailure.local(.network)
    }

    func summarize(_ request: SummaryRequest, pairing: PairingPayload) async throws -> SummaryResponse {
        // STUB(WP-09)
        throw RelayFailure.local(.network)
    }

    func cancel(requestId: UUID, pairing: PairingPayload) async {
        // STUB(WP-09)
    }
}
