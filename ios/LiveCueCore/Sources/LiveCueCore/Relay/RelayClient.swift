import Foundation

/// Protocol 13. A value type with no state: safe to copy and to call from any task.
/// Every method throws `RelayFailure`, or `CancellationError` when the calling task was cancelled
/// (protocol 13.6 row 1). No retries. Every request carries the headers of protocol 13.2.
public struct RelayClient: Sendable {
    private let transport: any HTTPTransport
    private let timing: CoreTiming

    public init(transport: any HTTPTransport, timing: CoreTiming = .system) {
        self.transport = transport
        self.timing = timing
    }

    /// Returns only when the relay is online: 200, `ok` true, `protocol` 2. A 200 with another or no
    /// `protocol` throws PROTOCOL_MISMATCH; `ok` not true throws L-UNREADABLE.
    public func health(_ pairing: PairingPayload) async throws -> RelayHealth {
        // STUB(WP-03)
        throw RelayFailure.local(.network)
    }

    /// Sends the body "{}".
    public func verifyPairing(_ pairing: PairingPayload) async throws {
        // STUB(WP-03)
        throw RelayFailure.local(.network)
    }

    public func models(_ pairing: PairingPayload) async throws -> [AssistantModelOption] {
        // STUB(WP-03)
        throw RelayFailure.local(.network)
    }

    public func localModelStatus(_ pairing: PairingPayload) async throws -> PCModelStatus {
        // STUB(WP-03)
        throw RelayFailure.local(.network)
    }

    /// `provider` must be PC-local. Any 2xx is success.
    public func startLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        // STUB(WP-03)
        throw RelayFailure.local(.network)
    }

    /// `provider` must be PC-local. Any 2xx is success.
    public func stopLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        // STUB(WP-03)
        throw RelayFailure.local(.network)
    }

    /// Requires `request.assistant`. Fits the body (`WireBudget.fitted`) before sending. The reply's
    /// `execution` must be present and echo the selection, else L-NOT-CONFIRMED. The returned
    /// `memory.throughSegmentId` is always nil: the reply's value is never read (protocol 13.5).
    public func assist(_ request: AssistRequest, pairing: PairingPayload) async throws -> AssistResponse {
        // STUB(WP-03)
        throw RelayFailure.local(.network)
    }

    public func summarize(_ request: SummaryRequest, pairing: PairingPayload) async throws -> SummaryResponse {
        // STUB(WP-03)
        throw RelayFailure.local(.network)
    }

    /// Route 9, best effort. Never throws; both answers count as done. The id in the path is
    /// `requestId.uuidString`, the same upper-case text the encoder wrote in the body: the relay compares
    /// ids byte for byte.
    public func cancel(requestId: UUID, pairing: PairingPayload) async {
        // STUB(WP-03)
    }
}
