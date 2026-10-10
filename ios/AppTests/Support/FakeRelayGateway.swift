import Foundation
import LiveCueCore
@testable import LiveCueRemake

/// A relay a test scripts by hand. It sends nothing.
///
/// Defaults: health, verify, models (empty) and the three PC model calls succeed; `assist` and
/// `summarize` fail with L-NETWORK. The names in `calls`, and the names `setHold` and `release` take,
/// are the method names: "health", "verifyPairing", "models", "localModelStatus", "startLocalModel",
/// "stopLocalModel", "assist", "summarize", "cancel".
actor FakeRelayGateway: RelayGateway {
    private var healthResult: Result<RelayHealth, RelayFailure> = .success(RelayHealth(relayVersion: "1.0.0"))
    private var verifyResult: Result<Void, RelayFailure> = .success(())
    private var modelsResult: Result<[AssistantModelOption], RelayFailure> = .success([])
    private var statusResult: Result<PCModelStatus, RelayFailure> = .success(PCModelStatus(state: "stopped", message: "Stopped"))
    private var startResult: Result<PCModelStatus, RelayFailure> = .success(PCModelStatus(state: "starting", message: "Starting"))
    private var stopResult: Result<PCModelStatus, RelayFailure> = .success(PCModelStatus(state: "stopping", message: "Stopping"))
    private var assistResult: Result<AssistResponse, RelayFailure> = .failure(RelayFailure.local(.network))
    private var summaryResult: Result<SummaryResponse, RelayFailure> = .failure(RelayFailure.local(.network))

    private var heldNames: Set<String> = []
    private var held: [(id: Int, name: String, continuation: CheckedContinuation<Void, any Error>)] = []
    private var nextHoldID = 0
    private var waiters: [(count: Int, continuation: CheckedContinuation<Void, Never>)] = []

    /// Method names in call order.
    private(set) var calls: [String] = []
    /// The pairing of every call, in call order.
    private(set) var pairings: [PairingPayload] = []
    /// The provider of every start and stop, in call order.
    private(set) var modelRequests: [SpeechProvider] = []
    private(set) var assistRequests: [AssistRequest] = []
    private(set) var summaryRequests: [SummaryRequest] = []
    /// The id of every `cancel`, in call order.
    private(set) var cancelledIDs: [UUID] = []

    init() {}

    // MARK: - Scripting

    func setHealth(_ result: Result<RelayHealth, RelayFailure>) {
        healthResult = result
    }

    func setVerify(_ result: Result<Void, RelayFailure>) {
        verifyResult = result
    }

    func setModels(_ result: Result<[AssistantModelOption], RelayFailure>) {
        modelsResult = result
    }

    func setStatus(_ result: Result<PCModelStatus, RelayFailure>) {
        statusResult = result
    }

    func setStart(_ result: Result<PCModelStatus, RelayFailure>) {
        startResult = result
    }

    func setStop(_ result: Result<PCModelStatus, RelayFailure>) {
        stopResult = result
    }

    func setAssist(_ result: Result<AssistResponse, RelayFailure>) {
        assistResult = result
    }

    func setSummary(_ result: Result<SummaryResponse, RelayFailure>) {
        summaryResult = result
    }

    /// True: calls of this name suspend, after being recorded, until `release(_:)` or until their task
    /// is cancelled (they then throw `CancellationError`).
    func setHold(_ hold: Bool, for name: String) {
        if hold {
            heldNames.insert(name)
        } else {
            heldNames.remove(name)
        }
    }

    /// Lets every held call of this name go on to its result.
    func release(_ name: String) {
        let waiting = held.filter { $0.name == name }
        held.removeAll { $0.name == name }
        for call in waiting {
            call.continuation.resume()
        }
    }

    /// Returns when at least `count` calls were received.
    func waitForCalls(count: Int) async {
        if calls.count >= count { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiters.append((count: count, continuation: continuation))
        }
    }

    // MARK: - RelayGateway

    func health(_ pairing: PairingPayload) async throws -> RelayHealth {
        try await record("health", pairing)
        return try healthResult.get()
    }

    func verifyPairing(_ pairing: PairingPayload) async throws {
        try await record("verifyPairing", pairing)
        try verifyResult.get()
    }

    func models(_ pairing: PairingPayload) async throws -> [AssistantModelOption] {
        try await record("models", pairing)
        return try modelsResult.get()
    }

    func localModelStatus(_ pairing: PairingPayload) async throws -> PCModelStatus {
        try await record("localModelStatus", pairing)
        return try statusResult.get()
    }

    func startLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        modelRequests.append(provider)
        try await record("startLocalModel", pairing)
        return try startResult.get()
    }

    func stopLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        modelRequests.append(provider)
        try await record("stopLocalModel", pairing)
        return try stopResult.get()
    }

    func assist(_ request: AssistRequest, pairing: PairingPayload) async throws -> AssistResponse {
        assistRequests.append(request)
        try await record("assist", pairing)
        return try assistResult.get()
    }

    func summarize(_ request: SummaryRequest, pairing: PairingPayload) async throws -> SummaryResponse {
        summaryRequests.append(request)
        try await record("summarize", pairing)
        return try summaryResult.get()
    }

    func cancel(requestId: UUID, pairing: PairingPayload) async {
        cancelledIDs.append(requestId)
        calls.append("cancel")
        pairings.append(pairing)
        wake()
    }

    // MARK: - Internals

    private func record(_ name: String, _ pairing: PairingPayload) async throws {
        calls.append(name)
        pairings.append(pairing)
        wake()
        guard heldNames.contains(name) else { return }

        nextHoldID += 1
        let id = nextHoldID
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, any Error>) in
                if Task.isCancelled {
                    continuation.resume(throwing: CancellationError())
                } else {
                    held.append((id: id, name: name, continuation: continuation))
                }
            }
        } onCancel: {
            Task { await self.cancelHeld(id) }
        }
    }

    private func cancelHeld(_ id: Int) {
        guard let index = held.firstIndex(where: { $0.id == id }) else { return }
        held.remove(at: index).continuation.resume(throwing: CancellationError())
    }

    private func wake() {
        let received = calls.count
        let ready = waiters.filter { $0.count <= received }
        waiters.removeAll { $0.count <= received }
        for waiter in ready {
            waiter.continuation.resume()
        }
    }
}
