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
        let body = try await send(.health, pairing: pairing)
        guard let reply = JSONValue.parse(body), reply.objectValue != nil else {
            throw RelayFailure.local(.unreadable)
        }
        // The version is checked first: a relay of another version may answer in another shape.
        guard reply["protocol"]?.intValue == RelayConstants.protocolVersion else {
            throw RelayFailure.wire(.protocolMismatch)
        }
        guard reply["ok"]?.boolValue == true, let relayVersion = reply["relayVersion"]?.stringValue else {
            throw RelayFailure.local(.unreadable)
        }
        return RelayHealth(relayVersion: relayVersion)
    }

    /// Sends the body "{}".
    public func verifyPairing(_ pairing: PairingPayload) async throws {
        let body = try await send(.pairVerify, pairing: pairing, body: Data("{}".utf8))
        // Only the relay's own `{"ok":true}` confirms the token. Any other 2xx (a sign-in page of a
        // network in between, for example) does not.
        guard JSONValue.parse(body)?["ok"]?.boolValue == true else {
            throw RelayFailure.local(.unreadable)
        }
    }

    public func models(_ pairing: PairingPayload) async throws -> [AssistantModelOption] {
        let body = try await send(.models, pairing: pairing)
        return try RelayClient.decode(CatalogueReply.self, from: body).models
    }

    public func localModelStatus(_ pairing: PairingPayload) async throws -> PCModelStatus {
        let body = try await send(.localModel, pairing: pairing)
        return try RelayClient.decode(PCModelStatus.self, from: body)
    }

    /// `provider` must be PC-local. Any 2xx is success.
    public func startLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        try await changeLocalModel(.localModelStart, provider: provider, pairing: pairing)
    }

    /// `provider` must be PC-local. Any 2xx is success.
    public func stopLocalModel(_ provider: SpeechProvider, pairing: PairingPayload) async throws -> PCModelStatus {
        try await changeLocalModel(.localModelStop, provider: provider, pairing: pairing)
    }

    /// Requires `request.assistant`. Fits the body (`WireBudget.fitted`) before sending. The reply's
    /// `execution` must be present and echo the selection, else L-NOT-CONFIRMED. The returned
    /// `memory.throughSegmentId` is always nil: the reply's value is never read (protocol 13.5).
    public func assist(_ request: AssistRequest, pairing: PairingPayload) async throws -> AssistResponse {
        // Without a selection the relay would answer INVALID_ASSISTANT (protocol 7.3); nothing is sent.
        guard let assistant = request.assistant else {
            throw RelayFailure.wire(.invalidAssistant)
        }
        // `fitted` returns only a body of at most C-11 bytes. It throws for one that cannot be made that
        // small, which a selection from a relay catalogue never causes; such a body is not sent.
        let body: Data
        do {
            let fitted = try WireBudget.fitted(request)
            body = try WireBudget.encodedBody(fitted)
        } catch {
            throw RelayFailure.wire(.payloadTooLarge)
        }

        let reply = try await sendAnswerRequest(.assist, requestId: request.requestId, pairing: pairing, body: body)
        var response = try RelayClient.decodeAssistReply(reply)

        // Protocol 13.5, the echo rule.
        guard let execution = response.execution,
              execution.model == assistant.model,
              execution.reasoningEffort == assistant.reasoningEffort
        else {
            throw RelayFailure.local(.notConfirmed)
        }
        response.memory.throughSegmentId = nil
        return response
    }

    public func summarize(_ request: SummaryRequest, pairing: PairingPayload) async throws -> SummaryResponse {
        let body: Data
        do {
            let fitted = try WireBudget.fitted(request)
            body = try WireBudget.encodedBody(fitted)
        } catch {
            throw RelayFailure.wire(.payloadTooLarge)
        }
        let reply = try await sendAnswerRequest(
            .sessionSummary,
            requestId: request.requestId,
            pairing: pairing,
            body: body
        )
        // The reply's `execution` is not read: the echo is checked on assist replies only (protocol 8.3).
        return try RelayClient.decode(SummaryResponse.self, from: reply)
    }

    /// Route 9, best effort. Never throws; both answers count as done. The id in the path is
    /// `requestId.uuidString`, the same upper-case text the encoder wrote in the body: the relay compares
    /// ids byte for byte.
    public func cancel(requestId: UUID, pairing: PairingPayload) async {
        _ = await exchange(.cancel(requestId: requestId.uuidString), pairing: pairing, body: nil)
    }

    // MARK: - Requests

    /// The request for a route (protocol 13.2), or L-BAD-ENDPOINT when the endpoint formed no URL
    /// (protocol 13.6 row 2).
    ///
    /// `HTTPRequest.timeout` is the transport's idle timeout. It is the route's phone timeout plus 10 s,
    /// so the transport's own timer can never end a request before the total timer of `exchange` does.
    static func makeRequest(route: RelayRoute, url: URL?, token: String, body: Data? = nil) throws -> HTTPRequest {
        guard let url else {
            throw RelayFailure.local(.badEndpoint)
        }
        var headers = [
            "Authorization": "Bearer " + token,
            "X-LiveCue-Protocol": String(RelayConstants.protocolVersion),
            "Accept": "application/json"
        ]
        if route.hasBody {
            headers["Content-Type"] = "application/json"
        }
        return HTTPRequest(
            method: route.method,
            url: url,
            headers: headers,
            body: route.hasBody ? body : nil,
            timeout: route.phoneTimeout + RelayClient.idleTimeoutMargin
        )
    }

    /// Seconds added to a route's phone timeout to make the transport's idle timeout (protocol 13.1).
    static let idleTimeoutMargin: TimeInterval = 10

    // MARK: - One exchange

    /// How one request ended, before any decoding.
    private enum Exchange: Sendable {
        /// The body of a 2xx reply.
        case success(Data)
        case failure(RelayFailure)
        /// Protocol 13.6 row 3. The request was still in flight.
        case timedOut
        /// Protocol 13.6 row 1. `inFlight` is true when the request was under way when it was cancelled.
        case cancelled(inFlight: Bool)
    }

    /// Sends one request, once, and classifies the outcome (protocol 13.6). The route's phone timeout is
    /// a total time, raced against `timing.sleep`.
    private func exchange(_ route: RelayRoute, pairing: PairingPayload, body: Data?) async -> Exchange {
        if Task.isCancelled {
            return .cancelled(inFlight: false)
        }

        let request: HTTPRequest
        do {
            request = try RelayClient.makeRequest(
                route: route,
                url: pairing.relayEndpoint.url(for: route),
                token: pairing.token,
                body: body
            )
        } catch let failure as RelayFailure {
            return .failure(failure)
        } catch {
            return .failure(RelayFailure.local(.badEndpoint))
        }

        let transport = self.transport
        let outcome = await RequestDeadline.run(seconds: route.phoneTimeout, timing: timing) {
            () async -> Result<(HTTPResponseHead, Data), TransportFailure> in
            do {
                let reply = try await transport.send(request)
                return .success(reply)
            } catch let failure as TransportFailure {
                return .failure(failure)
            } catch is CancellationError {
                return .failure(TransportFailure.cancelled)
            } catch {
                return .failure(TransportFailure.other)
            }
        }

        switch outcome {
        case .timedOut:
            // Row 3, also when the transport then reported cancellation: it was this timer that stopped it.
            return .timedOut
        case .finished(let result):
            if case .failure(let failure) = result, failure == TransportFailure.cancelled {
                return .cancelled(inFlight: true)
            }
            if Task.isCancelled {
                return .cancelled(inFlight: false)
            }
            switch RelayOutcome.classify(route: route, result: result) {
            case .success(let data):
                return .success(data)
            case .failure(let failure):
                return .failure(failure)
            }
        }
    }

    /// One request of a route that needs no cancel: the reply's bytes, or the error to throw.
    private func send(_ route: RelayRoute, pairing: PairingPayload, body: Data? = nil) async throws -> Data {
        switch await exchange(route, pairing: pairing, body: body) {
        case .success(let data):
            return data
        case .failure(let failure):
            throw failure
        case .timedOut:
            throw RelayFailure.local(.timeout)
        case .cancelled:
            throw CancellationError()
        }
    }

    /// Routes 7 and 8. When the phone timeout wins, or when the calling task is cancelled while the
    /// request is in flight, one cancel for the request id is started and not waited for
    /// (protocol 13.5). Nothing else sends route 9.
    private func sendAnswerRequest(
        _ route: RelayRoute,
        requestId: UUID,
        pairing: PairingPayload,
        body: Data
    ) async throws -> Data {
        switch await exchange(route, pairing: pairing, body: body) {
        case .success(let data):
            return data
        case .failure(let failure):
            throw failure
        case .timedOut:
            startCancel(requestId: requestId, pairing: pairing)
            throw RelayFailure.local(.timeout)
        case .cancelled(let inFlight):
            if inFlight {
                startCancel(requestId: requestId, pairing: pairing)
            }
            throw CancellationError()
        }
    }

    /// The one unstructured task of the client. It outlives the call that started it, which is the
    /// point: that call is ending because its time is up or its task was cancelled.
    private func startCancel(requestId: UUID, pairing: PairingPayload) {
        let client = self
        Task {
            await client.cancel(requestId: requestId, pairing: pairing)
        }
    }

    private func changeLocalModel(
        _ route: RelayRoute,
        provider: SpeechProvider,
        pairing: PairingPayload
    ) async throws -> PCModelStatus {
        // Only the models of C-04 can be named; the relay would answer INVALID_MODEL. Nothing is sent.
        guard provider.isPCLocal else {
            throw RelayFailure.wire(.invalidModel)
        }
        let body: Data
        do {
            body = try LiveCueJSON.encode(ModelCommand(model: provider.rawValue))
        } catch {
            throw RelayFailure.wire(.invalidRequest)
        }
        let reply = try await send(route, pairing: pairing, body: body)
        return try RelayClient.decode(PCModelStatus.self, from: reply)
    }

    // MARK: - Decoding

    /// The body of routes 5 and 6.
    private struct ModelCommand: Encodable {
        let model: String
    }

    /// The reply of route 3.
    private struct CatalogueReply: Decodable {
        let models: [AssistantModelOption]
    }

    /// Protocol 13.6 rows 7 and 8: a 2xx body that is not the route's success shape is L-UNREADABLE.
    private static func decode<Reply: Decodable>(_ type: Reply.Type, from body: Data) throws -> Reply {
        do {
            return try LiveCueJSON.decode(type, from: body)
        } catch {
            throw RelayFailure.local(.unreadable)
        }
    }

    /// An assist reply is never handed to the synthesised decoder as it arrived: `SessionMemory` needs
    /// its marker to be a UUID. The relay sends `null` there (protocol 8.3), and the phone must still
    /// accept, and ignore, any string. So the member is removed first, and what is left is decoded.
    private static func decodeAssistReply(_ body: Data) throws -> AssistResponse {
        guard let reply = JSONValue.parse(body), var members = reply.objectValue else {
            throw RelayFailure.local(.unreadable)
        }
        if var memory = members["memory"]?.objectValue {
            memory["throughSegmentId"] = nil
            members["memory"] = JSONValue.object(memory)
        }
        do {
            let cleaned = try LiveCueJSON.encode(JSONValue.object(members))
            return try LiveCueJSON.decode(AssistResponse.self, from: cleaned)
        } catch {
            throw RelayFailure.local(.unreadable)
        }
    }
}
