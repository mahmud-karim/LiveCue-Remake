import Foundation
import LiveCueCore
import XCTest

/// Headers, bodies and one request per call on every route.
///
/// P2-201, P2-204, P2-211 and P2-221 of protocol 15.9, and the route details that
/// `docs/architecture/04-ios-architecture.md` section 6.1 lists for this file.
final class RelayClientRoutesTests: XCTestCase {
    // MARK: - Fixtures

    private static let healthJSON = #"{"ok":true,"protocol":2,"relayVersion":"1.0.0"}"#
    private static let statusJSON = #"{"state":"ready","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Model loaded on the PC GPU. Ready for live transcription."}"#
    private static let startingJSON = #"{"state":"starting","model":"qwen3","busy":false,"elapsedSeconds":0,"message":"Starting Docker and loading the PC model. First load can take several minutes."}"#
    private static let internalErrorJSON = #"{"error":"The PC relay hit an internal error.","code":"INTERNAL_ERROR"}"#

    private static func reply(_ status: Int, _ json: String) -> FakeHTTPTransport.Reply {
        .response(status: status, headers: ["content-type": "application/json; charset=utf-8"], body: Data(json.utf8))
    }

    private static func assistRequest() -> AssistRequest {
        var request = AssistRequest(
            sessionId: UUID(),
            instruction: "Answer briefly",
            memory: SessionMemory(summary: "Earlier context"),
            transcript: "segment 2\nsegment 3\nsegment 4\nsegment 5",
            partialTranscript: "latest words"
        )
        request.assistant = AssistantConfiguration(model: "gpt-test", reasoningEffort: "low", provider: .codex)
        return request
    }

    private static func summaryRequest() -> SummaryRequest {
        SummaryRequest(
            sessionId: UUID(),
            assistant: AssistantConfiguration(model: "gpt-test", reasoningEffort: "low", provider: .codex),
            memory: SessionMemory(summary: "Earlier context"),
            transcript: "every segment, one per line"
        )
    }

    /// The error a call ended with, or nil when it returned.
    private static func caught(_ call: () async throws -> Void) async -> (any Error)? {
        do {
            try await call()
            return nil
        } catch {
            return error
        }
    }

    /// Calls the nine HTTP routes once each, in the order of protocol 6.1. Every call may fail.
    private static func callEveryRoute(
        _ client: RelayClient,
        pairing: PairingPayload,
        assist: AssistRequest,
        summary: SummaryRequest,
        cancelling requestId: UUID
    ) async -> [(any Error)?] {
        var errors: [(any Error)?] = []
        errors.append(await caught { _ = try await client.health(pairing) })
        errors.append(await caught { try await client.verifyPairing(pairing) })
        errors.append(await caught { _ = try await client.models(pairing) })
        errors.append(await caught { _ = try await client.localModelStatus(pairing) })
        errors.append(await caught { _ = try await client.startLocalModel(.nemotron, pairing: pairing) })
        errors.append(await caught { _ = try await client.stopLocalModel(.nemotron, pairing: pairing) })
        errors.append(await caught { _ = try await client.assist(assist, pairing: pairing) })
        errors.append(await caught { _ = try await client.summarize(summary, pairing: pairing) })
        await client.cancel(requestId: requestId, pairing: pairing)
        errors.append(nil)
        return errors
    }

    /// The nine routes in the same order, for a cancel of `requestId`.
    private static func everyRoute(cancelling requestId: UUID) -> [RelayRoute] {
        [
            .health, .pairVerify, .models, .localModel, .localModelStart, .localModelStop,
            .assist, .sessionSummary, .cancel(requestId: requestId.uuidString)
        ]
    }

    private static func successReplies() -> [FakeHTTPTransport.Reply] {
        [
            reply(200, healthJSON),
            reply(200, #"{"ok":true}"#),
            reply(200, CoreFixtures.catalogueJSON),
            reply(200, statusJSON),
            reply(202, startingJSON),
            reply(202, startingJSON),
            reply(200, CoreFixtures.assistReplyJSON),
            reply(200, CoreFixtures.summaryReplyJSON),
            reply(200, #"{"ok":true,"cancelled":true}"#)
        ]
    }

    // MARK: - P2-201: headers, and the request of every route

    func testEveryRouteCarriesTheHeadersOfProtocol13_2() async throws {
        for endpoint in ["https://pc.example.test:11443/", "https://pc.example.test:10000/livecue/"] {
            let pairing = CoreFixtures.pairing(endpoint)
            let transport = FakeHTTPTransport(replies: Self.successReplies())
            let client = RelayClient(transport: transport)
            let requestId = UUID()

            let errors = await Self.callEveryRoute(
                client,
                pairing: pairing,
                assist: Self.assistRequest(),
                summary: Self.summaryRequest(),
                cancelling: requestId
            )
            for (index, error) in errors.enumerated() {
                XCTAssertNil(error, "Route \(index + 1) on \(endpoint)")
            }

            let requests = await transport.requests
            let routes = Self.everyRoute(cancelling: requestId)
            XCTAssertEqual(requests.count, routes.count, endpoint)

            for (request, route) in zip(requests, routes) {
                let name = route.path
                XCTAssertEqual(request.method, route.method, name)
                // P2-200: the endpoint plus the route without its leading slash, as text.
                XCTAssertEqual(request.url.absoluteString, endpoint + String(route.path.dropFirst()), name)
                XCTAssertNil(request.url.query, name)
                XCTAssertNil(request.url.fragment, name)

                XCTAssertEqual(request.headers["Authorization"], "Bearer " + CoreFixtures.token, name)
                XCTAssertEqual(request.headers["X-LiveCue-Protocol"], "2", name)
                XCTAssertEqual(request.headers["Accept"], "application/json", name)
                if route.hasBody {
                    XCTAssertEqual(request.headers["Content-Type"], "application/json", name)
                    XCTAssertNotNil(request.body, name)
                } else {
                    XCTAssertNil(request.headers["Content-Type"], name)
                    XCTAssertNil(request.body, name)
                }

                let names = request.headers.keys.map { $0.lowercased() }
                XCTAssertFalse(names.contains("origin"), name)
                XCTAssertFalse(names.contains("cookie"), name)
                XCTAssertFalse(names.contains(where: { $0.hasPrefix("sec-fetch-") }), name)
                var expectedNames: Set<String> = ["authorization", "x-livecue-protocol", "accept"]
                if route.hasBody {
                    expectedNames.insert("content-type")
                }
                XCTAssertEqual(Set(names), expectedNames, name)
            }
        }
    }

    func testEveryRequestHasAnIdleTimeoutTenSecondsAboveItsPhoneTimeout() async throws {
        let pairing = CoreFixtures.pairing()
        let transport = FakeHTTPTransport(replies: Self.successReplies())
        let client = RelayClient(transport: transport)
        let requestId = UUID()

        _ = await Self.callEveryRoute(
            client,
            pairing: pairing,
            assist: Self.assistRequest(),
            summary: Self.summaryRequest(),
            cancelling: requestId
        )

        let requests = await transport.requests
        let routes = Self.everyRoute(cancelling: requestId)
        XCTAssertEqual(requests.count, routes.count)
        // Protocol 6.1, plus the 10 s of protocol 13.1.
        XCTAssertEqual(requests.map(\.timeout), [18, 25, 55, 18, 25, 25, 80, 80, 18])
        for (request, route) in zip(requests, routes) {
            XCTAssertEqual(request.timeout, route.phoneTimeout + 10, route.path)
            XCTAssertGreaterThan(request.timeout, route.phoneTimeout, route.path)
        }
    }

    // MARK: - P2-204: health

    func testAHealthReplyOfTheProtocolMeansOnline() async throws {
        let transport = FakeHTTPTransport(replies: [Self.reply(200, Self.healthJSON)])
        let client = RelayClient(transport: transport)

        let health = try await client.health(CoreFixtures.pairing())

        XCTAssertEqual(health, RelayHealth(relayVersion: "1.0.0"))
        let requests = await transport.requests
        XCTAssertEqual(requests.count, 1)
        XCTAssertEqual(requests.first?.method, "GET")
        XCTAssertEqual(requests.first?.url.absoluteString, "https://pc.example.test:11443/v1/health")
    }

    func testAHealthReplyWithAnotherOrNoProtocolIsAProtocolMismatch() async throws {
        let bodies = [
            #"{"ok":true,"protocol":3,"relayVersion":"2.0.0"}"#,
            #"{"ok":true,"relayVersion":"1.0.0"}"#,
            #"{"ok":true,"protocol":"2","relayVersion":"1.0.0"}"#,
            #"{"ok":true,"protocol":null,"relayVersion":"1.0.0"}"#,
            #"{"ok":true,"protocol":2.5,"relayVersion":"1.0.0"}"#,
            // The version decides first: another version may answer in another shape.
            #"{"protocol":1}"#
        ]
        for body in bodies {
            let transport = FakeHTTPTransport(replies: [Self.reply(200, body)])
            let client = RelayClient(transport: transport)

            let error = await Self.caught { _ = try await client.health(CoreFixtures.pairing()) }

            let failure = try XCTUnwrap(error as? RelayFailure, body)
            XCTAssertTrue(failure.isCode(.protocolMismatch), body)
            XCTAssertEqual(failure.origin, .http, body)
            XCTAssertEqual(
                RelayMessages.text(for: failure),
                "This iPhone app and LiveCue Desktop are different versions. Update both, then retry.",
                body
            )
            let count = await transport.requests.count
            XCTAssertEqual(count, 1, body)
        }
    }

    func testAHealthReplyWithProtocolTwoAndOkNotTrueIsUnreadable() async throws {
        let bodies = [
            #"{"ok":false,"protocol":2,"relayVersion":"1.0.0"}"#,
            #"{"protocol":2,"relayVersion":"1.0.0"}"#,
            #"{"ok":"true","protocol":2,"relayVersion":"1.0.0"}"#,
            #"{"ok":1,"protocol":2,"relayVersion":"1.0.0"}"#,
            // All three members are always present (protocol 6.2).
            #"{"ok":true,"protocol":2}"#,
            #"{"ok":true,"protocol":2,"relayVersion":7}"#
        ]
        for body in bodies {
            let transport = FakeHTTPTransport(replies: [Self.reply(200, body)])
            let client = RelayClient(transport: transport)

            let error = await Self.caught { _ = try await client.health(CoreFixtures.pairing()) }

            let failure = try XCTUnwrap(error as? RelayFailure, body)
            XCTAssertTrue(failure.isLocal(.unreadable), body)
            XCTAssertEqual(RelayMessages.text(for: failure), "The relay returned an unreadable response.", body)
        }
    }

    func testAHealthReplyThatIsNotAJSONObjectIsUnreadable() async throws {
        for body in ["", "<html>Sign in</html>", "[]", "true", #""ok""#] {
            let transport = FakeHTTPTransport(replies: [Self.reply(200, body)])
            let client = RelayClient(transport: transport)

            let error = await Self.caught { _ = try await client.health(CoreFixtures.pairing()) }

            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.unreadable), body)
        }
    }

    // MARK: - Verify

    func testVerifySendsAnEmptyObject() async throws {
        let transport = FakeHTTPTransport(replies: [Self.reply(200, #"{"ok":true}"#)])
        let client = RelayClient(transport: transport)

        try await client.verifyPairing(CoreFixtures.pairing("https://pc.example.test:10000/livecue/"))

        let requests = await transport.requests
        XCTAssertEqual(requests.count, 1)
        let request = try XCTUnwrap(requests.first)
        XCTAssertEqual(request.method, "POST")
        XCTAssertEqual(request.url.absoluteString, "https://pc.example.test:10000/livecue/v1/pair/verify")
        XCTAssertEqual(request.body, Data("{}".utf8))
        XCTAssertEqual(request.headers["Content-Type"], "application/json")
    }

    func testVerifyNeedsTheRelaysOwnConfirmation() async throws {
        // A 2xx that is not `{"ok":true}` (a sign-in page in between, for example) confirms nothing.
        for body in [#"{"ok":false}"#, "{}", "<html></html>", "", #"{"ok":"true"}"#] {
            let transport = FakeHTTPTransport(replies: [Self.reply(200, body)])
            let client = RelayClient(transport: transport)

            let error = await Self.caught { try await client.verifyPairing(CoreFixtures.pairing()) }

            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.unreadable), body)
        }
    }

    func testVerifyWithATokenTheRelayRefusesIsUnauthorized() async throws {
        let transport = FakeHTTPTransport(replies: [
            Self.reply(401, #"{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}"#)
        ])
        let client = RelayClient(transport: transport)

        let error = await Self.caught { try await client.verifyPairing(CoreFixtures.pairing()) }

        let failure = try XCTUnwrap(error as? RelayFailure)
        XCTAssertTrue(failure.isCode(.unauthorized))
        let count = await transport.requests.count
        XCTAssertEqual(count, 1)
    }

    // MARK: - Catalogue and PC model status

    func testModelsDecodesTheCatalogue() async throws {
        let transport = FakeHTTPTransport(replies: [
            Self.reply(200, CoreFixtures.catalogueJSON),
            Self.reply(200, #"{"models":[]}"#),
            Self.reply(200, #"{"models":[{"id":"gpt-a","name":"A","reasoningEfforts":["none","low"],"later":true},{"id":"gpt-b","name":"B","reasoningEfforts":["high"]}],"unknown":1}"#)
        ])
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()

        let first = try await client.models(pairing)
        XCTAssertEqual(first, [AssistantModelOption(id: "gpt-test", name: "GPT Test", reasoningEfforts: ["low"])])

        let empty = try await client.models(pairing)
        XCTAssertEqual(empty, [])

        let two = try await client.models(pairing)
        XCTAssertEqual(two.map(\.id), ["gpt-a", "gpt-b"])
        XCTAssertEqual(two.first?.reasoningEfforts, ["none", "low"])
        XCTAssertNil(two.first?.structuredOutputs)

        let requests = await transport.requests
        XCTAssertEqual(requests.map(\.method), ["GET", "GET", "GET"])
        XCTAssertEqual(requests.first?.url.absoluteString, "https://pc.example.test:11443/v1/models")
    }

    func testACatalogueThatIsNotTheSuccessShapeIsUnreadable() async throws {
        for body in ["{}", #"{"models":{}}"#, #"{"models":[{"id":"gpt-a"}]}"#, "not json"] {
            let transport = FakeHTTPTransport(replies: [Self.reply(200, body)])
            let client = RelayClient(transport: transport)

            let error = await Self.caught { _ = try await client.models(CoreFixtures.pairing()) }

            XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.unreadable), body)
        }
    }

    func testLocalModelStatusDecodesTheStatusObject() async throws {
        let transport = FakeHTTPTransport(replies: [
            Self.reply(200, Self.statusJSON),
            Self.reply(200, #"{"state":"stopped","model":null,"busy":false,"elapsedSeconds":0,"message":"PC model is stopped."}"#)
        ])
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()

        let ready = try await client.localModelStatus(pairing)
        XCTAssertEqual(
            ready,
            PCModelStatus(
                state: "ready",
                model: "nemotron",
                busy: false,
                elapsedSeconds: 0,
                message: "Model loaded on the PC GPU. Ready for live transcription."
            )
        )

        let stopped = try await client.localModelStatus(pairing)
        XCTAssertEqual(stopped.state, "stopped")
        XCTAssertNil(stopped.model)
        XCTAssertEqual(stopped.message, "PC model is stopped.")

        let requests = await transport.requests
        XCTAssertEqual(requests.first?.method, "GET")
        XCTAssertEqual(requests.first?.url.absoluteString, "https://pc.example.test:11443/v1/local-model")
        XCTAssertNil(requests.first?.body)
    }

    func testAStatusObjectWithAMissingMemberIsUnreadable() async throws {
        let transport = FakeHTTPTransport(replies: [Self.reply(200, #"{"state":"ready","model":"nemotron"}"#)])
        let client = RelayClient(transport: transport)

        let error = await Self.caught { _ = try await client.localModelStatus(CoreFixtures.pairing()) }

        XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.unreadable))
    }

    // MARK: - Start and stop

    func testStartAndStopSendTheModelAndAccept200And202() async throws {
        for provider in [SpeechProvider.nemotron, SpeechProvider.qwen3] {
            for status in [200, 202] {
                let transport = FakeHTTPTransport(replies: [
                    Self.reply(status, Self.startingJSON),
                    Self.reply(status, Self.statusJSON)
                ])
                let client = RelayClient(transport: transport)
                let pairing = CoreFixtures.pairing()
                let name = "\(provider.rawValue) \(status)"

                let started = try await client.startLocalModel(provider, pairing: pairing)
                XCTAssertEqual(started.state, "starting", name)
                XCTAssertEqual(started.model, "qwen3", name)

                let stopped = try await client.stopLocalModel(provider, pairing: pairing)
                XCTAssertEqual(stopped.state, "ready", name)

                let requests = await transport.requests
                XCTAssertEqual(requests.count, 2, name)
                XCTAssertEqual(requests.map(\.method), ["POST", "POST"], name)
                XCTAssertEqual(
                    requests.map(\.url.absoluteString),
                    [
                        "https://pc.example.test:11443/v1/local-model/start",
                        "https://pc.example.test:11443/v1/local-model/stop"
                    ],
                    name
                )
                for request in requests {
                    let body = try XCTUnwrap(request.body, name)
                    XCTAssertEqual(JSONValue.parse(body), JSONValue.object(["model": .string(provider.rawValue)]), name)
                    XCTAssertEqual(request.headers["Content-Type"], "application/json", name)
                }
            }
        }
    }

    func testStartAndStopNameOnlyAPCSpeechModel() async throws {
        for provider in [SpeechProvider.meta, SpeechProvider.voz, SpeechProvider.parakeet] {
            let transport = FakeHTTPTransport(replies: [Self.reply(202, Self.startingJSON)])
            let client = RelayClient(transport: transport)
            let pairing = CoreFixtures.pairing()

            let startError = await Self.caught { _ = try await client.startLocalModel(provider, pairing: pairing) }
            let stopError = await Self.caught { _ = try await client.stopLocalModel(provider, pairing: pairing) }

            XCTAssertEqual((startError as? RelayFailure)?.wireCode, .invalidModel, provider.rawValue)
            XCTAssertEqual((stopError as? RelayFailure)?.wireCode, .invalidModel, provider.rawValue)
            let count = await transport.requests.count
            XCTAssertEqual(count, 0, provider.rawValue)
        }
    }

    func testAStopRefusedByTheRelayCarriesItsCode() async throws {
        let transport = FakeHTTPTransport(replies: [
            Self.reply(409, #"{"error":"A different PC model is loaded. Select it before stopping.","code":"MODEL_MISMATCH"}"#),
            Self.reply(503, #"{"error":"PC relay is paused. Resume it in LiveCue Desktop.","code":"RELAY_PAUSED"}"#)
        ])
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()

        let stopError = await Self.caught { _ = try await client.stopLocalModel(.nemotron, pairing: pairing) }
        let startError = await Self.caught { _ = try await client.startLocalModel(.nemotron, pairing: pairing) }

        XCTAssertEqual((stopError as? RelayFailure)?.wireCode, .modelMismatch)
        XCTAssertEqual((startError as? RelayFailure)?.wireCode, .relayPaused)
    }

    // MARK: - Summary

    func testSummarizeReturnsTheSummaryAndDoesNotReadExecution() async throws {
        // The echo is checked on assist replies only (protocol 8.3): a summary whose `execution` names
        // another model, and one without `execution`, are both read.
        let transport = FakeHTTPTransport(replies: [
            Self.reply(200, CoreFixtures.summaryReplyJSON),
            Self.reply(200, #"{"title":"T","summary":"S","keyPoints":[],"actionItems":[],"execution":{"model":"gpt-other","reasoningEffort":"high","codexMs":1,"relayTotalMs":2,"requestReadMs":0.5,"relayOverheadMs":1}}"#),
            Self.reply(200, #"{"title":"T","summary":"S","keyPoints":["k"],"actionItems":["a"]}"#)
        ])
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()
        let request = Self.summaryRequest()

        let first = try await client.summarize(request, pairing: pairing)
        XCTAssertEqual(
            first,
            SummaryResponse(
                title: "Local transcription",
                summary: "A short summary.",
                keyPoints: ["A key point"],
                actionItems: ["An action item"]
            )
        )
        let second = try await client.summarize(request, pairing: pairing)
        XCTAssertEqual(second, SummaryResponse(title: "T", summary: "S", keyPoints: [], actionItems: []))
        let third = try await client.summarize(request, pairing: pairing)
        XCTAssertEqual(third, SummaryResponse(title: "T", summary: "S", keyPoints: ["k"], actionItems: ["a"]))

        let requests = await transport.requests
        XCTAssertEqual(requests.count, 3)
        let sent = try XCTUnwrap(requests.first)
        XCTAssertEqual(sent.method, "POST")
        XCTAssertEqual(sent.url.absoluteString, "https://pc.example.test:11443/v1/session-summary")
        XCTAssertEqual(sent.body, try WireBudget.encodedBody(request))
        let body = try XCTUnwrap(JSONValue.parse(try XCTUnwrap(sent.body)))
        XCTAssertEqual(body["requestId"]?.stringValue, request.requestId.uuidString)
        XCTAssertEqual(body["assistant"]?["model"]?.stringValue, "gpt-test")
        XCTAssertEqual(body["assistant"]?["provider"]?.stringValue, "codex")
        XCTAssertEqual(body["transcript"]?.stringValue, "every segment, one per line")
        XCTAssertNil(body["transcriptTruncated"])
    }

    func testASummaryThatIsNotTheSuccessShapeIsUnreadable() async throws {
        let transport = FakeHTTPTransport(replies: [Self.reply(200, #"{"title":"T","summary":"S"}"#)])
        let client = RelayClient(transport: transport)

        let error = await Self.caught { _ = try await client.summarize(Self.summaryRequest(), pairing: CoreFixtures.pairing()) }

        XCTAssertEqual(error as? RelayFailure, RelayFailure.local(.unreadable))
    }

    // MARK: - P2-211: no retries

    func testAFailingRouteIsRequestedExactlyOnce() async throws {
        let failures: [FakeHTTPTransport.Reply] = [
            Self.reply(500, Self.internalErrorJSON),
            .failure(.unreachable),
            .failure(.timedOut),
            .failure(.tls),
            .failure(.other),
            Self.reply(503, ""),
            Self.reply(429, #"{"error":"Too many requests in progress.","code":"TOO_MANY_REQUESTS"}"#),
            Self.reply(200, "not json")
        ]
        for (index, failing) in failures.enumerated() {
            let transport = FakeHTTPTransport()
            await transport.setDefault(failing)
            let client = RelayClient(transport: transport)
            let requestId = UUID()

            let errors = await Self.callEveryRoute(
                client,
                pairing: CoreFixtures.pairing(),
                assist: Self.assistRequest(),
                summary: Self.summaryRequest(),
                cancelling: requestId
            )

            // Every throwing route failed, with a relay failure and nothing else.
            for (route, error) in errors.dropLast().enumerated() {
                XCTAssertNotNil(error as? RelayFailure, "Failure \(index), route \(route + 1)")
            }
            // Nine calls, nine requests, in order: no retry, and no cancel after a failed answer request.
            let requests = await transport.requests
            let routes = Self.everyRoute(cancelling: requestId)
            XCTAssertEqual(requests.map(\.method), routes.map(\.method), "Failure \(index)")
            XCTAssertEqual(
                requests.map(\.url.absoluteString),
                routes.map { "https://pc.example.test:11443" + $0.path },
                "Failure \(index)"
            )
        }
    }

    // MARK: - P2-221: redirects

    func testARedirectIsNotFollowedAndIsClassifiedByItsStatus() async throws {
        for status in [301, 302, 303, 307, 308] {
            let transport = FakeHTTPTransport(replies: [
                .response(
                    status: status,
                    headers: ["location": "https://other.example.test/v1/health"],
                    body: Data()
                ),
                // What a client that followed the redirect would get.
                Self.reply(200, Self.healthJSON)
            ])
            let client = RelayClient(transport: transport)

            let error = await Self.caught { _ = try await client.health(CoreFixtures.pairing()) }

            let failure = try XCTUnwrap(error as? RelayFailure, "\(status)")
            XCTAssertTrue(failure.isLocal(.http), "\(status)")
            XCTAssertEqual(failure.httpStatus, status)
            XCTAssertEqual(RelayMessages.text(for: failure), "Relay error (\(status)).")
            let requests = await transport.requests
            XCTAssertEqual(requests.count, 1, "\(status)")
            XCTAssertEqual(requests.first?.url.host, "pc.example.test", "\(status)")
        }
    }

    // MARK: - Cancel

    func testTheCancelPathEndsInTheRequestIdAsTheBodyWroteIt() async throws {
        let transport = FakeHTTPTransport(replies: [
            Self.reply(200, CoreFixtures.assistReplyJSON),
            Self.reply(200, #"{"ok":true,"cancelled":true}"#)
        ])
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing("https://pc.example.test:10000/livecue/")
        // Read from lower-case text: the wire form is `uuidString`, upper case, in both places.
        let requestId = try XCTUnwrap(UUID(uuidString: "6f9619ff-8b86-4011-b42d-00c04fc964ff"))
        var request = AssistRequest(requestId: requestId, sessionId: UUID(), memory: SessionMemory(), transcript: "Hello")
        request.assistant = AssistantConfiguration(model: "gpt-test", reasoningEffort: "low", provider: .codex)

        _ = try await client.assist(request, pairing: pairing)
        await client.cancel(requestId: requestId, pairing: pairing)

        let requests = await transport.requests
        XCTAssertEqual(requests.count, 2)
        let body = try XCTUnwrap(JSONValue.parse(try XCTUnwrap(requests.first?.body)))
        let written = try XCTUnwrap(body["requestId"]?.stringValue)
        XCTAssertEqual(written, "6F9619FF-8B86-4011-B42D-00C04FC964FF")
        XCTAssertEqual(written, requestId.uuidString)

        let cancel = try XCTUnwrap(requests.last)
        XCTAssertEqual(cancel.method, "DELETE")
        XCTAssertEqual(
            cancel.url.absoluteString,
            "https://pc.example.test:10000/livecue/v1/requests/6F9619FF-8B86-4011-B42D-00C04FC964FF"
        )
        XCTAssertTrue(cancel.url.absoluteString.hasSuffix("/" + written))
        XCTAssertNil(cancel.body)
        XCTAssertNil(cancel.headers["Content-Type"])
    }

    func testCancelNeverFailsWhateverTheRelayAnswers() async throws {
        let answers: [FakeHTTPTransport.Reply] = [
            Self.reply(200, #"{"ok":true,"cancelled":true}"#),
            Self.reply(404, #"{"error":"No request with that id is in progress.","code":"REQUEST_NOT_FOUND"}"#),
            Self.reply(401, #"{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}"#),
            Self.reply(502, ""),
            .failure(.unreachable),
            .failure(.cancelled)
        ]
        let transport = FakeHTTPTransport(replies: answers)
        let client = RelayClient(transport: transport)
        let pairing = CoreFixtures.pairing()

        for _ in answers {
            // The call has no error to throw: both answers of route 9, and every failure, count as done.
            await client.cancel(requestId: UUID(), pairing: pairing)
        }

        let requests = await transport.requests
        XCTAssertEqual(requests.count, answers.count)
        XCTAssertEqual(Set(requests.map(\.method)), ["DELETE"])
    }
}
