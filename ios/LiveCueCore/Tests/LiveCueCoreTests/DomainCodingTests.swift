import Foundation
import LiveCueCore
import XCTest

/// Every coded type round-trips under the default encoder and under `LiveCueJSON`.
final class DomainCodingTests: XCTestCase {
    // MARK: - Old tests (spec 02 section 10.1, 10.4, 10.7, 10.8)

    /// AC-CORE-001.
    func testLegacyAnswersDecodeWithoutTiming() throws {
        let turn = AssistantTurn(detectedQuestion: "Q", answer: "A", details: "")

        let decoded = try JSONDecoder().decode(AssistantTurn.self, from: JSONEncoder().encode(turn))
        XCTAssertNil(decoded.performance)
        XCTAssertEqual(decoded, turn)

        let stored = try LiveCueJSON.decode(AssistantTurn.self, from: LiveCueJSON.encode(turn))
        XCTAssertNil(stored.performance)
        XCTAssertEqual(stored, turn)
    }

    /// AC-CORE-002 and AC-CORE-003.
    func testPerformanceRoundTripAndTransportEstimate() throws {
        let configuration = AssistantConfiguration(model: "gpt-5.6-luna", reasoningEffort: "low")
        let execution = RelayExecution(
            model: "gpt-5.6-luna",
            reasoningEffort: "low",
            codexMs: 500,
            relayTotalMs: 520,
            requestReadMs: 2,
            relayOverheadMs: 20
        )
        let performance = AssistPerformance(
            configuration: configuration,
            speechModel: "voz",
            reusedText: false,
            transcriptionMs: 100,
            contextMs: 1,
            roundTripMs: 550,
            totalMs: 651,
            transcriptCharacters: 20,
            execution: execution
        )
        XCTAssertEqual(performance.transportEstimateMs, 30)

        let turn = AssistantTurn(detectedQuestion: "Q", answer: "A", details: "", performance: performance)
        let decoded = try JSONDecoder().decode(AssistantTurn.self, from: JSONEncoder().encode(turn))
        XCTAssertEqual(decoded, turn)
        let stored = try LiveCueJSON.decode(AssistantTurn.self, from: LiveCueJSON.encode(turn))
        XCTAssertEqual(stored, turn)
    }

    func testTransportEstimateIsNeverNegativeAndNeedsAnExecution() {
        var performance = AssistPerformance(
            configuration: AssistantConfiguration(),
            speechModel: "meta",
            reusedText: true,
            transcriptionMs: 0,
            contextMs: 2,
            roundTripMs: 400,
            totalMs: 402,
            transcriptCharacters: 5
        )
        XCTAssertNil(performance.transportEstimateMs)

        performance.execution = RelayExecution(
            model: "gpt-5.6-sol",
            reasoningEffort: "low",
            codexMs: 600,
            relayTotalMs: 620,
            requestReadMs: 2,
            relayOverheadMs: 20
        )
        XCTAssertEqual(performance.transportEstimateMs, 0)
    }

    /// AC-CORE-038.
    func testExecutionWithUsageRoundTrips() throws {
        var execution = RelayExecution(
            model: "test/model",
            reasoningEffort: "default",
            codexMs: 0,
            relayTotalMs: 0,
            requestReadMs: 0,
            relayOverheadMs: 0
        )
        execution.usage = AssistantUsage(promptTokens: 100, completionTokens: 30, costCredits: 0.001)

        let decoded = try JSONDecoder().decode(RelayExecution.self, from: JSONEncoder().encode(execution))
        XCTAssertEqual(decoded, execution)
        XCTAssertEqual(decoded.usage?.promptTokens, 100)
        XCTAssertEqual(decoded.usage?.completionTokens, 30)
        XCTAssertEqual(decoded.usage?.costCredits, 0.001)
        XCTAssertNil(decoded.usage?.generationId)

        let stored = try LiveCueJSON.decode(RelayExecution.self, from: LiveCueJSON.encode(execution))
        XCTAssertEqual(stored, execution)
    }

    /// AC-CORE-083.
    func testLocalProviderUsageRoundTrips() throws {
        for provider in [SpeechProvider.nemotron, SpeechProvider.qwen3] {
            var usage = TranscriptionUsage(provider: provider.rawValue)
            let stream = UUID()
            usage.begin(stream)
            usage.update(stream, sentMs: 60_000, processedMs: 60_000, hasTranscript: true)
            usage.finish(stream, completed: true)

            let decoded = try JSONDecoder().decode(TranscriptionUsage.self, from: JSONEncoder().encode(usage))
            XCTAssertEqual(decoded, usage, provider.rawValue)
            let stored = try LiveCueJSON.decode(TranscriptionUsage.self, from: LiveCueJSON.encode(usage))
            XCTAssertEqual(stored, usage, provider.rawValue)
        }
    }

    /// AC-CORE-104, AC-CORE-105 and AC-CORE-106.
    func testHistoryRoundTripLegacyAndLocalCosts() throws {
        var session = Session()
        let legacy = try JSONDecoder().decode(Session.self, from: JSONEncoder().encode(session))
        XCTAssertNil(legacy.transcriptionUsage)
        XCTAssertEqual(legacy, session)

        var usage = TranscriptionUsage()
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: 600_000, processedMs: 600_000, hasTranscript: true)
        usage.finish(stream, completed: true)
        session.transcriptionUsage = usage

        let decoded = try JSONDecoder().decode(Session.self, from: JSONEncoder().encode(session))
        XCTAssertEqual(decoded, session)
        XCTAssertEqual(try XCTUnwrap(decoded.transcriptionUsage).estimatedUSD, 0.03, accuracy: 0.000_000_1)

        let stored = try LiveCueJSON.decode(Session.self, from: LiveCueJSON.encode(session))
        XCTAssertEqual(stored, session)
        XCTAssertEqual(try XCTUnwrap(stored.transcriptionUsage).estimatedUSD, 0.03, accuracy: 0.000_000_1)
    }

    // MARK: - Every coded type, both encoders

    func testConversationTypesRoundTrip() throws {
        try assertRoundTrips(TranscriptSegment(text: "Hello there.", startSeconds: 0.2, endSeconds: 3.1))
        try assertRoundTrips(SessionMemory())
        try assertRoundTrips(Self.memory)
        try assertRoundTrips(SessionNotes())
        try assertRoundTrips(SessionNotes(summary: "A summary", keyPoints: ["One", "Two"], actionItems: ["Do it"]))
        try assertRoundTrips(Self.turn)
        try assertRoundTrips(Self.session)
        try assertRoundTrips(CoreFixtures.session(segments: ["segment 0", "segment 1"]))
    }

    func testAssistantTypesRoundTrip() throws {
        try assertRoundTrips(AssistantProvider.allCases)
        try assertRoundTrips(AssistantConfiguration())
        try assertRoundTrips(AssistantConfiguration(model: "vendor/model", reasoningEffort: "default", provider: .openrouter))
        try assertRoundTrips(AssistantModelOption(id: "gpt-test", name: "GPT Test", reasoningEfforts: ["low", "high"]))
        try assertRoundTrips(
            AssistantModelOption(
                id: "vendor/model",
                name: "Vendor Model",
                reasoningEfforts: ["default"],
                structuredOutputs: true,
                promptPrice: "0.000001",
                completionPrice: "0.000002"
            )
        )
        try assertRoundTrips(AssistantUsage())
        try assertRoundTrips(Self.usage)
        try assertRoundTrips(Self.execution)
        try assertRoundTrips(Self.performance)
    }

    func testRequestsAndRepliesRoundTrip() throws {
        var request = AssistRequest(
            sessionId: UUID(),
            instruction: "Answer briefly",
            memory: Self.memory,
            transcript: "segment 2\nsegment 3",
            partialTranscript: "latest words"
        )
        try assertRoundTrips(request)
        request.assistant = AssistantConfiguration()
        try assertRoundTrips(request)
        try assertRoundTrips(AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: ""))

        let summary = SummaryRequest(
            sessionId: UUID(),
            assistant: AssistantConfiguration(),
            memory: Self.memory,
            transcript: "every segment"
        )
        try assertRoundTrips(summary)
        var truncated = summary
        truncated.transcriptTruncated = true
        try assertRoundTrips(truncated)

        var response = AssistResponse(detectedQuestion: "Q", answer: "A", details: "D", memory: Self.memory)
        try assertRoundTrips(response)
        response.execution = Self.execution
        try assertRoundTrips(response)
        try assertRoundTrips(SummaryResponse(title: "T", summary: "S", keyPoints: ["K"], actionItems: ["A"]))
    }

    func testSpeechAndUsageTypesRoundTrip() throws {
        try assertRoundTrips(SpeechProvider.allCases)
        try assertRoundTrips(TranscriptionModel.voz)
        try assertRoundTrips(TranscriptionModel.parakeet)
        try assertRoundTrips(PCModelStatus(state: "stopped", message: "Stopped"))
        try assertRoundTrips(PCModelStatus(state: "starting", model: "qwen3", busy: false, elapsedSeconds: 12, message: "Loading"))
        try assertRoundTrips(SpeechStreamUsage(id: UUID()))
        try assertRoundTrips(
            SpeechStreamUsage(
                id: UUID(),
                sentMs: 31_900,
                processedMs: 30_400,
                hasTranscript: true,
                state: SpeechStreamUsage.completed
            )
        )
        try assertRoundTrips(TranscriptionUsage())
        try assertRoundTrips(TranscriptionUsage(provider: "parakeet"))
        try assertRoundTrips(Self.transcriptionUsage)
    }

    /// The fixture replies of protocol 8.3 decode into the reply types.
    func testProtocolRepliesDecode() throws {
        let assist = try LiveCueJSON.decode(AssistResponse.self, from: Data(CoreFixtures.assistReplyJSON.utf8))
        XCTAssertEqual(assist.detectedQuestion, "What is the main advantage of local transcription?")
        XCTAssertEqual(assist.memory, SessionMemory(summary: "Earlier context", facts: ["A fact"]))
        XCTAssertEqual(assist.execution?.model, "gpt-test")
        XCTAssertEqual(assist.execution?.relayTotalMs, 620.4)
        XCTAssertNil(assist.execution?.usage)

        let summary = try LiveCueJSON.decode(SummaryResponse.self, from: Data(CoreFixtures.summaryReplyJSON.utf8))
        XCTAssertEqual(
            summary,
            SummaryResponse(
                title: "Local transcription",
                summary: "A short summary.",
                keyPoints: ["A key point"],
                actionItems: ["An action item"]
            )
        )
    }

    // MARK: - What is and is not written

    func testComputedValuesAreNeverWritten() throws {
        let performance = try keys(of: Self.performance)
        XCTAssertFalse(performance.contains("transportEstimateMs"))
        XCTAssertEqual(
            performance,
            [
                "configuration", "contextMs", "execution", "lastLiveChunkMs", "reusedText", "roundTripMs",
                "speechModel", "totalMs", "transcriptCharacters", "transcriptionMs"
            ]
        )

        XCTAssertEqual(
            try keys(of: TranscriptionModel.voz),
            ["approximateMegabytes", "displayName", "quality", "recommended", "variant"]
        )
        XCTAssertEqual(
            try keys(of: Self.transcriptionUsage),
            ["finalizationMs", "firstTextMs", "hourlyRateUSD", "pricingDate", "provider", "streams"]
        )
        XCTAssertEqual(
            try keys(of: PCModelStatus(state: "ready", model: "nemotron", message: "Ready")),
            ["busy", "elapsedSeconds", "message", "model", "state"]
        )
    }

    func testUnknownKeysAreIgnored() throws {
        let json = """
        {"summary":"S","facts":["F"],"openQuestions":[],"somethingNew":{"nested":[1,2,3]},"another":true}
        """
        let memory = try LiveCueJSON.decode(SessionMemory.self, from: Data(json.utf8))
        XCTAssertEqual(memory, SessionMemory(summary: "S", facts: ["F"]))
    }

    func testAMarkerThatIsNotAUUIDFailsTheWholeMemory() {
        let json = """
        {"summary":"S","facts":[],"openQuestions":[],"throughSegmentId":"not-a-uuid"}
        """
        XCTAssertThrowsError(try LiveCueJSON.decode(SessionMemory.self, from: Data(json.utf8)))
        XCTAssertThrowsError(try JSONDecoder().decode(SessionMemory.self, from: Data(json.utf8)))
    }

    func testRequiredMembersAreRequired() {
        let noFacts = """
        {"summary":"S","openQuestions":[]}
        """
        XCTAssertThrowsError(try LiveCueJSON.decode(SessionMemory.self, from: Data(noFacts.utf8)))

        let noDetails = """
        {"title":"T","summary":"S","keyPoints":[]}
        """
        XCTAssertThrowsError(try LiveCueJSON.decode(SummaryResponse.self, from: Data(noDetails.utf8)))

        let nullState = """
        {"id":"6F9619FF-8B86-4011-B42D-00C04FC964FF","sentMs":0,"hasTranscript":false,"state":null}
        """
        XCTAssertThrowsError(try LiveCueJSON.decode(SpeechStreamUsage.self, from: Data(nullState.utf8)))
    }

    // MARK: - Helpers: API key shape

    func testAPIKeyShape() {
        XCTAssertEqual(APIKeyShape.check(""), .empty)
        XCTAssertEqual(APIKeyShape.check("synthetic-unit-key"), .valid)
        XCTAssertEqual(APIKeyShape.check("x"), .valid)

        XCTAssertEqual(APIKeyShape.check(" "), .containsWhitespace)
        XCTAssertEqual(APIKeyShape.check(" synthetic-unit-key"), .containsWhitespace)
        XCTAssertEqual(APIKeyShape.check("synthetic-unit-key "), .containsWhitespace)
        XCTAssertEqual(APIKeyShape.check("synthetic unit key"), .containsWhitespace)
        XCTAssertEqual(APIKeyShape.check("synthetic-unit-key\n"), .containsWhitespace)
        XCTAssertEqual(APIKeyShape.check("synthetic-unit-key\r\n"), .containsWhitespace)
        XCTAssertEqual(APIKeyShape.check("synthetic\tunit-key"), .containsWhitespace)
        XCTAssertEqual(APIKeyShape.check("synthetic\u{00A0}unit-key"), .containsWhitespace, "no-break space")
        XCTAssertEqual(APIKeyShape.check("synthetic\u{2028}unit-key"), .containsWhitespace, "line separator")
        XCTAssertEqual(APIKeyShape.check("synthetic\u{3000}unit-key"), .containsWhitespace, "ideographic space")
    }

    // MARK: - Support

    private static let memory = SessionMemory(
        summary: "Earlier context",
        facts: ["A fact", "Another fact"],
        openQuestions: ["An open question"],
        throughSegmentId: UUID()
    )

    private static let usage = AssistantUsage(
        generationId: "gen-abc",
        promptTokens: 120,
        completionTokens: 40,
        costCredits: 0.0001
    )

    private static var execution: RelayExecution {
        RelayExecution(
            model: "vendor/model",
            reasoningEffort: "default",
            codexMs: 600.1,
            relayTotalMs: 620.4,
            requestReadMs: 2,
            relayOverheadMs: 20.3,
            usage: usage
        )
    }

    private static var performance: AssistPerformance {
        AssistPerformance(
            configuration: AssistantConfiguration(model: "vendor/model", reasoningEffort: "default", provider: .openrouter),
            speechModel: "parakeet",
            reusedText: false,
            transcriptionMs: 0,
            contextMs: 12.5,
            roundTripMs: 1_830.2,
            totalMs: 1_850.9,
            lastLiveChunkMs: 41.5,
            transcriptCharacters: 51,
            execution: execution
        )
    }

    private static var turn: AssistantTurn {
        AssistantTurn(
            createdAt: Date(timeIntervalSince1970: 1_790_000_038.25),
            request: "Answer briefly",
            detectedQuestion: AssistantTurn.latestConversation,
            answer: "Your audio stays on the phone.",
            details: "",
            performance: performance
        )
    }

    private static var transcriptionUsage: TranscriptionUsage {
        var usage = TranscriptionUsage()
        let first = UUID()
        let second = UUID()
        usage.begin(first)
        usage.update(first, sentMs: 31_900, processedMs: 30_400, hasTranscript: true)
        usage.finish(first, completed: true)
        usage.begin(second)
        usage.update(second, sentMs: 1_200)
        usage.apply(.firstText(milliseconds: 840.5))
        usage.apply(.finalization(milliseconds: 310.2))
        return usage
    }

    /// A finished session with everything a session can hold.
    private static var session: Session {
        var session = Session(
            title: "What is the main advantage of local transcription",
            startedAt: Date(timeIntervalSince1970: 1_790_000_000.123),
            endedAt: Date(timeIntervalSince1970: 1_790_001_003.5),
            segments: [
                TranscriptSegment(text: "What is the main advantage of local transcription?", startSeconds: 0.2, endSeconds: 3.1),
                TranscriptSegment(text: "It keeps the audio here.", startSeconds: 3.4, endSeconds: 5)
            ],
            assistantTurns: [turn, AssistantTurn(detectedQuestion: "Q", answer: "A", details: "D")],
            memory: memory,
            notes: SessionNotes(summary: "A summary", keyPoints: ["One"], actionItems: ["Do it"])
        )
        session.transcriptionUsage = transcriptionUsage
        return session
    }

    /// decode(encode(x)) equals x under the default encoder (numeric dates) and under `LiveCueJSON`.
    private func assertRoundTrips<T: Codable & Equatable>(
        _ value: T,
        file: StaticString = #filePath,
        line: UInt = #line
    ) throws {
        let plain = try JSONDecoder().decode(T.self, from: JSONEncoder().encode(value))
        XCTAssertEqual(plain, value, "default encoder", file: file, line: line)
        let stored = try LiveCueJSON.decode(T.self, from: LiveCueJSON.encode(value))
        XCTAssertEqual(stored, value, "LiveCueJSON", file: file, line: line)
        // A second trip through the text changes nothing either.
        let again = try LiveCueJSON.decode(T.self, from: LiveCueJSON.encode(stored))
        XCTAssertEqual(again, stored, "LiveCueJSON, second trip", file: file, line: line)
    }

    /// The member names of the JSON object that `LiveCueJSON` writes for a value, sorted.
    private func keys<T: Encodable>(of value: T) throws -> [String] {
        let object = try XCTUnwrap(JSONValue.parse(LiveCueJSON.encode(value))?.objectValue)
        return object.keys.sorted()
    }
}
