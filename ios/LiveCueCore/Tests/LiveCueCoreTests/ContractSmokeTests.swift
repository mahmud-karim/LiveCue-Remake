import Foundation
import LiveCueCore
import XCTest

/// The constants of protocol section 2 have their values, the route table is that of protocol 6.1, and
/// the test support that every other core test builds on behaves as documented.
final class ContractSmokeTests: XCTestCase {
    // MARK: - Constants

    func testConstantsHaveTheValuesOfProtocolSection2() {
        XCTAssertEqual(RelayConstants.protocolVersion, 2)
        XCTAssertEqual(RelayConstants.tokenLength, 43)
        XCTAssertEqual(RelayConstants.maximumEncodedBodyBytes, 126_976)
        XCTAssertEqual(RelayConstants.maximumEncodedBodyBytes, 124 * 1_024)
        XCTAssertEqual(RelayConstants.assistTranscriptBytes, 80_000)
        XCTAssertEqual(RelayConstants.summaryTranscriptBytes, 100_000)
        XCTAssertEqual(RelayConstants.partialTranscriptBytes, 16_000)
        XCTAssertEqual(RelayConstants.instructionCharacters, 4_000)
        XCTAssertEqual(RelayConstants.instructionBytes, 8_000)
        XCTAssertEqual(RelayConstants.memorySummaryBytes, 24_000)
        XCTAssertEqual(RelayConstants.memoryListItems, 16)
        XCTAssertEqual(RelayConstants.memoryListItemBytes, 256)
        XCTAssertEqual(RelayConstants.pairingPayloadBytes, 8_192)
        XCTAssertEqual(RelayConstants.maximumInboundSpeechMessageBytes, 131_072)
        XCTAssertEqual(RelayConstants.speechConnectSeconds, 180)
        XCTAssertEqual(RelayConstants.speechFinalizeSeconds, 15)
        XCTAssertEqual(RelayConstants.healthPollSeconds, 5)
        XCTAssertEqual(RelayConstants.pcModelPollSeconds, 2)
        XCTAssertEqual(RelayConstants.speechSampleRate, 16_000)
        XCTAssertEqual(RelayConstants.speechFrameBytes, 2_560)
        XCTAssertEqual(RelayConstants.unknownCodeTextMaximumCharacters, 300)
        XCTAssertEqual(RelayConstants.speechModels, ["nemotron", "qwen3"])
    }

    func testASpeechFrameIsEightyMillisecondsOfAudio() {
        // C-70 and C-71: 16-bit mono, so two bytes per sample.
        XCTAssertEqual(RelayConstants.speechFrameBytes, RelayConstants.speechSampleRate * 2 * 80 / 1_000)
        XCTAssertEqual(MetaRealtimeAPI.frameBytes, MetaRealtimeAPI.sampleRate * 2 * 80 / 1_000)
    }

    func testRouteTableIsThatOfProtocol6_1() {
        let requestID = "6F9619FF-8B86-4011-B42D-00C04FC964FF"
        let rows: [(route: RelayRoute, method: String, path: String, hasBody: Bool, timeout: TimeInterval)] = [
            (.health, "GET", "/v1/health", false, 8),
            (.pairVerify, "POST", "/v1/pair/verify", true, 15),
            (.models, "GET", "/v1/models", false, 45),
            (.localModel, "GET", "/v1/local-model", false, 8),
            (.localModelStart, "POST", "/v1/local-model/start", true, 15),
            (.localModelStop, "POST", "/v1/local-model/stop", true, 15),
            (.assist, "POST", "/v1/assist", true, 70),
            (.sessionSummary, "POST", "/v1/session-summary", true, 70),
            (.cancel(requestId: requestID), "DELETE", "/v1/requests/" + requestID, false, 8),
            (.speech, "GET", "/v1/speech", false, 180)
        ]
        for row in rows {
            XCTAssertEqual(row.route.method, row.method, row.path)
            XCTAssertEqual(row.route.path, row.path)
            XCTAssertEqual(row.route.hasBody, row.hasBody, row.path)
            XCTAssertEqual(row.route.phoneTimeout, row.timeout, row.path)
        }
    }

    func testEveryCodeAndConditionIsSpelledAsInProtocol14() {
        XCTAssertEqual(RelayWireCode.allCases.count, 43)
        for code in RelayWireCode.allCases {
            let allowed = code.rawValue.utf8.allSatisfy { byte in
                (UInt8(ascii: "A")...UInt8(ascii: "Z")).contains(byte) || byte == UInt8(ascii: "_")
            }
            XCTAssertTrue(allowed, code.rawValue)
        }
        XCTAssertEqual(RelayWireCode.unauthorized.rawValue, "UNAUTHORIZED")
        XCTAssertEqual(RelayWireCode.invalidJSON.rawValue, "INVALID_JSON")
        XCTAssertEqual(RelayWireCode.invalidRequestID.rawValue, "INVALID_REQUEST_ID")
        XCTAssertEqual(RelayWireCode.assistantCatalogUnavailable.rawValue, "ASSISTANT_CATALOG_UNAVAILABLE")
        XCTAssertEqual(RelayWireCode.modelOperationInProgress.rawValue, "MODEL_OPERATION_IN_PROGRESS")
        XCTAssertEqual(RelayWireCode.relayStopping.rawValue, "RELAY_STOPPING")

        XCTAssertEqual(RelayLocalCondition.allCases.count, 13)
        for condition in RelayLocalCondition.allCases {
            XCTAssertTrue(condition.rawValue.hasPrefix("L-"), condition.rawValue)
        }
        XCTAssertEqual(RelayLocalCondition.speechNotConfirmed.rawValue, "L-SPEECH-NOT-CONFIRMED")
    }

    func testRelayFailureKeepsCodesItDoesNotKnow() {
        let known = RelayFailure.wire(.modelBusy, origin: .stream)
        XCTAssertEqual(known.wireCode, .modelBusy)
        XCTAssertTrue(known.isCode(.modelBusy))
        XCTAssertFalse(known.isCode(.speechBusy))
        XCTAssertFalse(known.isLocal(.timeout))
        XCTAssertEqual(known.origin, .stream)

        let unknown = RelayFailure(kind: .wire("FUTURE_CODE"), origin: .http, httpStatus: 418, relayText: "Later.")
        XCTAssertNil(unknown.wireCode)
        XCTAssertFalse(unknown.isCode(.internalError))
        XCTAssertEqual(unknown.httpStatus, 418)

        let local = RelayFailure.local(.http, httpStatus: 502)
        XCTAssertNil(local.wireCode)
        XCTAssertTrue(local.isLocal(.http))
        XCTAssertEqual(local.origin, .http)
        XCTAssertEqual(local.httpStatus, 502)
    }

    // MARK: - Texts that later packages only pass on

    func testErrorTextsCarryTheWordingOfArchitecture1_3() {
        XCTAssertEqual(
            MetaSpeechError.missingKey.errorDescription,
            "Add your Meta API key in Transcription. It stays in this iPhone's Keychain."
        )
        XCTAssertEqual(
            MetaSpeechError.authentication.errorDescription,
            "Meta did not accept the connection. Check the API key, account access, and billing in Transcription."
        )
        XCTAssertEqual(
            OpenRouterError.missingKey.errorDescription,
            "Add your OpenRouter API key in Assistant models & timing first."
        )
        XCTAssertEqual(
            OpenRouterError.invalidKey.errorDescription,
            "OpenRouter rejected this API key. Replace it in Assistant models & timing."
        )
        XCTAssertEqual(
            OpenRouterError.noModel.errorDescription,
            "Choose an OpenRouter model in Assistant models & timing first."
        )
        XCTAssertEqual(MetaSpeechError.malformedKey.errorDescription, OpenRouterError.malformedKey.errorDescription)
        XCTAssertEqual(
            OpenRouterError.malformedKey.errorDescription,
            "This key contains spaces or line breaks. Paste it again exactly as issued."
        )
        XCTAssertEqual(
            SpeechStreamFailure(.sendFailed).message,
            "Audio connection failed. Resume to reconnect."
        )
        XCTAssertEqual(SpeechStreamFailure(.meta(.provider)).message, MetaSpeechError.provider.errorDescription)
    }

    func testProviderCatalogueIsThatOfSpec02Section7() {
        XCTAssertEqual(SpeechProvider.allCases, [.meta, .nemotron, .qwen3, .voz, .parakeet, .apple])
        XCTAssertEqual(SpeechProvider.allCases.map(\.rawValue), ["meta", "nemotron", "qwen3", "voz", "parakeet", "apple"])
        XCTAssertEqual(RelayConstants.speechModels, SpeechProvider.allCases.filter(\.isPCLocal).map(\.rawValue))
        XCTAssertEqual(SpeechProvider.meta.sampleRate, MetaRealtimeAPI.sampleRate)
        XCTAssertEqual(SpeechProvider.nemotron.sampleRate, RelayConstants.speechSampleRate)
        XCTAssertEqual(AssistantProvider.codex.displayName, "PC · Codex")
        XCTAssertEqual(AssistantProvider.openrouter.displayName, "OpenRouter · direct")
        XCTAssertEqual(TranscriptionModel.onDevice(for: .voz), TranscriptionModel.voz)
        XCTAssertEqual(TranscriptionModel.onDevice(for: .parakeet), TranscriptionModel.parakeet)
        XCTAssertNil(TranscriptionModel.onDevice(for: .meta))
    }

    // MARK: - Test support

    func testFakeHTTPTransportRepliesInOrderThenWithItsDefault() async throws {
        let transport = FakeHTTPTransport(replies: [
            .response(status: 200, headers: ["content-type": "application/json"], body: Data("first".utf8)),
            .failure(.tls)
        ])
        await transport.setDefault(.response(status: 503, headers: [:], body: Data()))
        let request = try makeRequest()

        let (head, body) = try await transport.send(request)
        XCTAssertEqual(head.status, 200)
        XCTAssertEqual(head.headers["content-type"], "application/json")
        XCTAssertEqual(String(decoding: body, as: UTF8.self), "first")

        do {
            _ = try await transport.send(request)
            XCTFail("The second reply is a failure.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .tls)
        }

        let (later, _) = try await transport.send(request)
        XCTAssertEqual(later.status, 503)

        let requests = await transport.requests
        XCTAssertEqual(requests.count, 3)
        XCTAssertEqual(requests.first?.url, request.url)
        XCTAssertEqual(requests.first?.method, "GET")
    }

    func testFakeHTTPTransportWithNothingScriptedFails() async throws {
        let transport = FakeHTTPTransport()
        do {
            _ = try await transport.send(try makeRequest())
            XCTFail("An unscripted request fails.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .other)
        }
    }

    func testFakeHTTPTransportNeverReplyEndsWhenTheTaskIsCancelled() async throws {
        let transport = FakeHTTPTransport(replies: [.never])
        let request = try makeRequest()
        let task = Task { try await transport.send(request) }

        await transport.waitForRequests(count: 1)
        task.cancel()

        do {
            _ = try await task.value
            XCTFail("A request that is never answered ends only by cancellation.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .cancelled)
        }
    }

    func testFakeHTTPTransportStreamsChunksAndCanBreakOff() async throws {
        let transport = FakeHTTPTransport()
        await transport.enqueueChunks(status: 200, headers: [:], chunks: [Data("a".utf8), Data("b".utf8)])
        await transport.enqueueChunks(status: 200, headers: [:], chunks: [Data("c".utf8)], ending: .fail(.unreachable))
        let request = try makeRequest()

        let (head, whole) = try await transport.stream(request)
        XCTAssertEqual(head.status, 200)
        var received: [String] = []
        for try await chunk in whole {
            received.append(String(decoding: chunk, as: UTF8.self))
        }
        XCTAssertEqual(received, ["a", "b"])

        let (_, broken) = try await transport.stream(request)
        var beforeFailure: [String] = []
        do {
            for try await chunk in broken {
                beforeFailure.append(String(decoding: chunk, as: UTF8.self))
            }
            XCTFail("The second body breaks off.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .unreachable)
        }
        XCTAssertEqual(beforeFailure, ["c"])
    }

    func testManualTimeWakesSleepersInDeadlineOrder() async throws {
        let time = ManualTime()
        let timing = time.timing
        let woken = Recorder()

        let late = Task {
            try await timing.sleep(2)
            await woken.append("late")
        }
        let early = Task {
            try await timing.sleep(1)
            await woken.append("early")
        }
        await time.waitForSleepers(count: 2)
        XCTAssertEqual(timing.uptime(), 0)

        await time.advance(by: 1)
        try await early.value
        XCTAssertEqual(timing.uptime(), 1)
        let afterOneSecond = await woken.values
        XCTAssertEqual(afterOneSecond, ["early"])
        XCTAssertEqual(time.sleeperCount, 1)

        await time.advance(by: 5)
        try await late.value
        XCTAssertEqual(timing.uptime(), 6)
        let afterSixSeconds = await woken.values
        XCTAssertEqual(afterSixSeconds, ["early", "late"])
        XCTAssertEqual(time.sleeperCount, 0)
    }

    func testManualTimeSleepEndsWithCancellation() async throws {
        let time = ManualTime()
        let timing = time.timing
        let task = Task { try await timing.sleep(60) }

        await time.waitForSleepers()
        task.cancel()

        do {
            try await task.value
            XCTFail("A cancelled sleep throws.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }
        XCTAssertEqual(time.sleeperCount, 0)
        XCTAssertEqual(time.now, 0)
    }

    func testFakeWebSocketRecordsSendsAndDeliversInOrder() async throws {
        let transport = FakeWebSocketTransport()
        let url = try XCTUnwrap(URL(string: "wss://pc.example.test:11443/v1/speech"))
        let connection = try await transport.open(url: url, headers: ["X-LiveCue-Protocol": "2"], maximumMessageSize: 131_072)

        try await connection.send(text: "hello")
        try await connection.send(data: Data([1, 2, 3, 4]))
        await transport.deliver(.text("one"))
        await transport.deliver(.closed(code: 1000, reason: ""))

        let first = try await connection.receive()
        XCTAssertEqual(first, .text("one"))
        let second = try await connection.receive()
        XCTAssertEqual(second, .closed(code: 1000, reason: ""))
        do {
            _ = try await connection.receive()
            XCTFail("Nothing can be received after the close frame.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .other)
        }

        await connection.close(code: 1000, reason: "")

        let openedURL = await transport.openedURL
        let openedHeaders = await transport.openedHeaders
        let maximum = await transport.openedMaximumMessageSize
        let texts = await transport.sentTexts
        let frames = await transport.sentFrames
        let closes = await transport.closeCalls
        XCTAssertEqual(openedURL, url)
        XCTAssertEqual(openedHeaders, ["X-LiveCue-Protocol": "2"])
        XCTAssertEqual(maximum, 131_072)
        XCTAssertEqual(texts, ["hello"])
        XCTAssertEqual(frames, [Data([1, 2, 3, 4])])
        XCTAssertEqual(closes, [1000])
    }

    func testFakeWebSocketWakesAWaitingReceiveOnDeliveryDropAndClose() async throws {
        let transport = FakeWebSocketTransport()
        let url = try XCTUnwrap(URL(string: "wss://pc.example.test/v1/speech"))
        let connection = try await transport.open(url: url, headers: [:], maximumMessageSize: 1)

        let delivered = Task { try await connection.receive() }
        await transport.waitForReceive()
        await transport.deliver(.data(Data([9])))
        let message = try await delivered.value
        XCTAssertEqual(message, .data(Data([9])))

        let dropped = Task { try await connection.receive() }
        await transport.waitForReceive()
        await transport.drop()
        do {
            _ = try await dropped.value
            XCTFail("A dropped connection throws.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .other)
        }

        let closed = Task { try await connection.receive() }
        await transport.waitForReceive()
        await connection.close(code: 1001, reason: "going away")
        do {
            _ = try await closed.value
            XCTFail("Closing ends a waiting receive.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .other)
        }

        do {
            try await connection.send(text: "late")
            XCTFail("Nothing can be sent after the close.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .other)
        }
    }

    func testFakeWebSocketOpenResults() async throws {
        let url = try XCTUnwrap(URL(string: "wss://pc.example.test/v1/speech"))

        let rejecting = FakeWebSocketTransport(open: .rejected(status: 401))
        do {
            _ = try await rejecting.open(url: url, headers: [:], maximumMessageSize: 1)
            XCTFail("The upgrade is refused.")
        } catch {
            XCTAssertEqual(error as? WebSocketUpgradeRejected, WebSocketUpgradeRejected(status: 401))
        }

        let failing = FakeWebSocketTransport(open: .failure(.tls))
        do {
            _ = try await failing.open(url: url, headers: [:], maximumMessageSize: 1)
            XCTFail("The connection fails.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .tls)
        }

        let hanging = FakeWebSocketTransport(open: .never)
        let task = Task { _ = try await hanging.open(url: url, headers: [:], maximumMessageSize: 1) }
        await hanging.waitForOpen()
        task.cancel()
        do {
            try await task.value
            XCTFail("An open that never answers ends only by cancellation.")
        } catch {
            XCTAssertEqual(error as? TransportFailure, .cancelled)
        }
    }

    func testFixturesAreValid() throws {
        XCTAssertEqual(CoreFixtures.token.count, RelayConstants.tokenLength)
        XCTAssertEqual(CoreFixtures.pairing().endpoint, "https://pc.example.test:11443/")
        XCTAssertEqual(CoreFixtures.pairing("https://pc.example.test:10000/livecue/").token, CoreFixtures.token)

        let session = CoreFixtures.session(segments: ["one", "two"])
        XCTAssertEqual(session.segments.map(\.text), ["one", "two"])
        XCTAssertEqual(session.segments.map(\.startSeconds), [0, 1])
        XCTAssertEqual(session.segments.map(\.endSeconds), [1, 2])
        XCTAssertEqual(session.title, Session.defaultTitle)
        XCTAssertNil(session.endedAt)

        for text in [CoreFixtures.assistReplyJSON, CoreFixtures.summaryReplyJSON, CoreFixtures.catalogueJSON] {
            let value = try XCTUnwrap(JSONValue.parse(text))
            XCTAssertNotNil(value.objectValue, text)
        }
        let execution = JSONValue.parse(CoreFixtures.assistReplyJSON)?["execution"]
        XCTAssertEqual(execution?["model"]?.stringValue, "gpt-test")
        XCTAssertEqual(execution?["reasoningEffort"]?.stringValue, "low")
    }

    // MARK: - Helpers

    private func makeRequest() throws -> HTTPRequest {
        let url = try XCTUnwrap(URL(string: "https://pc.example.test:11443/v1/health"))
        return HTTPRequest(method: "GET", url: url, timeout: 8)
    }
}

/// Collects values from several tasks.
private actor Recorder {
    private(set) var values: [String] = []

    func append(_ value: String) {
        values.append(value)
    }
}
