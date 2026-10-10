import Foundation
import LiveCueCore

/// The app's view of the relay. `RelayClient` already has these methods, so the conformance is empty.
protocol RelayGateway: Sendable {
    func health(_ pairing: PairingPayload) async throws -> RelayHealth
    func verifyPairing(_ pairing: PairingPayload) async throws
    func models(_ pairing: PairingPayload) async throws -> [AssistantModelOption]
    func localModelStatus(_ pairing: PairingPayload) async throws -> PCModelStatus
    func startLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus
    func stopLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus
    func assist(_ request: AssistRequest, pairing: PairingPayload) async throws -> AssistResponse
    func summarize(_ request: SummaryRequest, pairing: PairingPayload) async throws -> SummaryResponse
    func cancel(requestId: UUID, pairing: PairingPayload) async
}

extension RelayClient: RelayGateway {}
