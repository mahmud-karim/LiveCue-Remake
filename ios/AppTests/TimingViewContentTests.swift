import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Timing rows for Codex, OpenRouter, Parakeet, retry and missing PC timings.
final class TimingViewContentTests: XCTestCase {
    private let codexFootnote = "PC timings are parts of the round trip, not extra time. Codex includes CLI startup, cloud processing and output handling—not pure inference. Transfer is an estimate including encoding/decoding. Live transcription runs before the tap, so it is not part of tap-to-answer time."
    private let openRouterFootnote = "OpenRouter round trip includes network, provider processing and response decoding—not pure inference. Cost is reported by OpenRouter after the request; missing cost is not treated as free. Speech transcription is a separate service."

    private func execution(
        model: String = "gpt-5.6-sol",
        effort: String = "low",
        usage: AssistantUsage? = nil
    ) -> RelayExecution {
        RelayExecution(
            model: model,
            reasoningEffort: effort,
            codexMs: 600,
            relayTotalMs: 620,
            requestReadMs: 2,
            relayOverheadMs: 20,
            usage: usage
        )
    }

    private func turn(
        provider: AssistantProvider = .codex,
        model: String = "gpt-5.6-sol",
        effort: String = "low",
        speechModel: String = "meta",
        reusedText: Bool = false,
        transcriptionMs: Double = 0,
        lastLiveChunkMs: Double? = nil,
        execution: RelayExecution? = nil
    ) -> AssistantTurn {
        AssistantTurn(
            detectedQuestion: "What did design say they need?",
            answer: "One more week for the onboarding flow.",
            details: "",
            performance: AssistPerformance(
                configuration: AssistantConfiguration(model: model, reasoningEffort: effort, provider: provider),
                speechModel: speechModel,
                reusedText: reusedText,
                transcriptionMs: transcriptionMs,
                contextMs: 31,
                roundTripMs: 1_447,
                totalMs: 1_482,
                lastLiveChunkMs: lastLiveChunkMs,
                transcriptCharacters: 1_482,
                execution: execution
            )
        )
    }

    private func keys(_ content: TimingViewContent) -> [String] {
        content.rows.map(\.key)
    }

    private func value(_ key: String, in content: TimingViewContent) -> String? {
        content.rows.first { $0.key == key }?.value
    }

    func testThereIsNoContentWithoutATimingRecord() {
        let untimed = AssistantTurn(detectedQuestion: "A question", answer: "An answer.", details: "")
        XCTAssertNil(TimingViewContent(turn: untimed))
    }

    func testCodexAnswerWithPCTimings() throws {
        // AC-IOS-069
        let content = try XCTUnwrap(TimingViewContent(turn: turn(execution: execution())))

        XCTAssertEqual(content.caption, "gpt-5.6-sol · low")
        XCTAssertEqual(keys(content), [
            "Tap → answer",
            "Transcription after tap",
            "Context + model check",
            "PC round trip",
            "PC: Codex CLI call",
            "PC: relay overhead",
            "Transfer / client estimate"
        ])
        XCTAssertEqual(content.rows.map(\.value), ["1.482 s", "0.000 s", "0.031 s", "1.447 s", "0.600 s", "0.020 s", "0.827 s"])
        XCTAssertTrue(content.rows.contains { $0.key.hasPrefix("PC: Codex CLI call") })
        XCTAssertNil(content.usageCaption)
        XCTAssertNil(content.warning)
        XCTAssertEqual(content.footerCaption, "meta · 1482 transcript characters")
        XCTAssertEqual(content.footnote, codexFootnote)
        XCTAssertEqual(Set(content.rows.map(\.id)).count, content.rows.count, "Every row has its own id.")
    }

    func testTheCaptionNamesWhatActuallyRan() throws {
        let ran = execution(model: "gpt-6-astra", effort: "medium")
        let content = try XCTUnwrap(TimingViewContent(turn: turn(execution: ran)))
        XCTAssertEqual(content.caption, "gpt-6-astra · medium")

        let configured = try XCTUnwrap(TimingViewContent(turn: turn(model: "gpt-5.6-luna", effort: "none")))
        XCTAssertEqual(configured.caption, "gpt-5.6-luna · none", "Without an execution record the selection is named.")
    }

    func testCodexAnswerWithoutPCTimings() throws {
        let content = try XCTUnwrap(TimingViewContent(turn: turn()))

        XCTAssertEqual(keys(content), ["Tap → answer", "Transcription after tap", "Context + model check", "PC round trip"])
        XCTAssertEqual(content.warning, "PC timing unavailable. Restart the updated desktop relay.")
        XCTAssertNil(content.usageCaption)
        XCTAssertEqual(content.footnote, codexFootnote)
    }

    func testTheTransferEstimateIsNeverNegative() throws {
        var slowRelay = execution()
        slowRelay.relayTotalMs = 5_000
        let content = try XCTUnwrap(TimingViewContent(turn: turn(execution: slowRelay)))
        XCTAssertEqual(value("Transfer / client estimate", in: content), "0.000 s")
    }

    func testOpenRouterAnswerWithCostAndUsage() throws {
        let usage = AssistantUsage(generationId: "generation", promptTokens: 1_204, completionTokens: 86, costCredits: 0.000412)
        let ran = execution(model: "openai/gpt-5.6-spark", effort: "default", usage: usage)
        let answer = turn(provider: .openrouter, model: "openai/gpt-5.6-spark", effort: "default", speechModel: "nemotron", execution: ran)
        let content = try XCTUnwrap(TimingViewContent(turn: answer))

        XCTAssertEqual(content.caption, "openai/gpt-5.6-spark · default")
        XCTAssertEqual(keys(content), [
            "Tap → answer",
            "Transcription after tap",
            "Context + model check",
            "OpenRouter round trip",
            "Reported request cost"
        ])
        XCTAssertEqual(value("OpenRouter round trip", in: content), "1.447 s")
        XCTAssertEqual(value("Reported request cost", in: content), "0.000412 credits")
        XCTAssertEqual(content.usageCaption, "1204 input · 86 output tokens")
        XCTAssertNil(content.warning, "The PC warning belongs to Codex answers.")
        XCTAssertFalse(content.rows.contains { $0.key.hasPrefix("PC:") })
        XCTAssertEqual(content.footerCaption, "nemotron · 1482 transcript characters")
        XCTAssertEqual(content.footnote, openRouterFootnote)
    }

    func testOpenRouterAnswerWithoutAReportedCost() throws {
        let noUsage = try XCTUnwrap(TimingViewContent(turn: turn(provider: .openrouter, model: "test/direct", effort: "default")))
        XCTAssertEqual(value("Reported request cost", in: noUsage), "Unavailable")
        XCTAssertNil(noUsage.usageCaption)
        XCTAssertNil(noUsage.warning)

        // Tokens without a cost: the cost is still not treated as free.
        let tokensOnly = execution(usage: AssistantUsage(promptTokens: 120, completionTokens: nil, costCredits: nil))
        let content = try XCTUnwrap(TimingViewContent(turn: turn(provider: .openrouter, execution: tokensOnly)))
        XCTAssertEqual(value("Reported request cost", in: content), "Unavailable")
        XCTAssertEqual(content.usageCaption, "120 input · 0 output tokens")
    }

    func testParakeetAnswerShowsTheLastLiveChunk() throws {
        let answer = turn(speechModel: "parakeet", lastLiveChunkMs: 38.4, execution: execution())
        let content = try XCTUnwrap(TimingViewContent(turn: answer))

        XCTAssertEqual(content.rows.last?.key, "Last live STT chunk")
        XCTAssertEqual(content.rows.last?.value, "38 ms")
        XCTAssertEqual(content.rows.last?.id, TimingViewContent.lastChunkRowID)
        XCTAssertEqual(content.footerCaption, "parakeet · 1482 transcript characters")

        let other = try XCTUnwrap(TimingViewContent(turn: turn(execution: execution())))
        XCTAssertFalse(keys(other).contains("Last live STT chunk"))
    }

    func testSameTextRetry() throws {
        let content = try XCTUnwrap(TimingViewContent(turn: turn(reusedText: true, execution: execution())))

        XCTAssertTrue(keys(content).contains("Transcription (reused)"))
        XCTAssertFalse(keys(content).contains("Transcription after tap"))
        XCTAssertEqual(content.footerCaption, "meta · 1482 transcript characters · same-text retry")
    }

    func testVozTranscriptionAfterTheTap() throws {
        let content = try XCTUnwrap(TimingViewContent(turn: turn(speechModel: "voz", transcriptionMs: 912.4, execution: execution())))
        XCTAssertEqual(value("Transcription after tap", in: content), "0.912 s")
    }

    func testNumbersUseADotAndNeverGoBelowZero() {
        XCTAssertEqual(AnswerFormat.seconds(milliseconds: 1_482, digits: 3), "1.482 s")
        XCTAssertEqual(AnswerFormat.seconds(milliseconds: 1_482, digits: 2), "1.48 s")
        XCTAssertEqual(AnswerFormat.seconds(milliseconds: -5, digits: 3), "0.000 s")
        XCTAssertEqual(AnswerFormat.seconds(milliseconds: .nan, digits: 2), "0.00 s")
        XCTAssertEqual(AnswerFormat.milliseconds(37.6), "38 ms")
        XCTAssertEqual(AnswerFormat.dollars(0.000412), "$0.000412")
        XCTAssertEqual(AnswerFormat.credits(0.0001), "0.000100 credits")
    }

    func testTheCardLineShowsTheTotalAndAReportedCost() {
        let plain = turn(execution: execution())
        XCTAssertEqual(AnswerFormat.total(of: plain), "1.48 s")
        XCTAssertEqual(AnswerFormat.totalAndCost(of: plain), "1.48 s")
        XCTAssertNil(AnswerFormat.reportedCost(of: plain))

        let paid = turn(provider: .openrouter, execution: execution(usage: AssistantUsage(costCredits: 0.000412)))
        XCTAssertEqual(AnswerFormat.totalAndCost(of: paid), "1.48 s · $0.000412")

        let untimed = AssistantTurn(detectedQuestion: "A question", answer: "An answer.", details: "")
        XCTAssertNil(AnswerFormat.total(of: untimed))
        XCTAssertNil(AnswerFormat.totalAndCost(of: untimed))
    }
}
