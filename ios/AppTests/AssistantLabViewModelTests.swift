import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Comparisons limit and order; price and effort labels.
final class AssistantLabViewModelTests: XCTestCase {
    /// Not a key of any service.
    private let syntheticKey = "synthetic-unit-key"
    private let firstInstant = Date(timeIntervalSince1970: 1_700_000_000)

    @MainActor
    private func makeModel(_ owners: TestOwners) -> AssistantLabViewModel {
        AssistantLabViewModel(
            assistant: owners.assistant,
            relay: owners.relay,
            activity: owners.activity,
            history: owners.history,
            session: owners.session
        )
    }

    /// A turn made `offset` seconds after the first instant; timed unless `timed` is false.
    private func turn(
        _ name: String,
        offset: TimeInterval,
        timed: Bool = true,
        provider: AssistantProvider = .codex,
        reusedText: Bool = false,
        question: String = "What did design say they need?"
    ) -> AssistantTurn {
        let configuration = AssistantConfiguration(
            model: provider == .codex ? "gpt-5.6-sol" : "test/direct",
            reasoningEffort: provider == .codex ? "low" : "default",
            provider: provider
        )
        let performance = AssistPerformance(
            configuration: configuration,
            speechModel: "meta",
            reusedText: reusedText,
            transcriptionMs: 0,
            contextMs: 31,
            roundTripMs: 2_200,
            totalMs: 2_314,
            transcriptCharacters: 40
        )
        return AssistantTurn(
            createdAt: firstInstant.addingTimeInterval(offset),
            detectedQuestion: question,
            answer: name,
            details: "",
            performance: timed ? performance : nil
        )
    }

    @MainActor
    private func store(_ turns: [AssistantTurn], startedAfter offset: TimeInterval, in owners: TestOwners) async throws {
        let session = Session(
            startedAt: firstInstant.addingTimeInterval(offset),
            endedAt: firstInstant.addingTimeInterval(offset + 60),
            assistantTurns: turns
        )
        try await owners.sessions.save(session, savedAt: firstInstant.addingTimeInterval(offset + 60))
    }

    // MARK: - Labels

    @MainActor
    func testEffortLabels() async {
        XCTAssertEqual(AssistantLabViewModel.effortLabel("none"), "None (no reasoning)")
        XCTAssertEqual(AssistantLabViewModel.effortLabel("low"), "Low")
        XCTAssertEqual(AssistantLabViewModel.effortLabel("medium"), "Medium")
        XCTAssertEqual(AssistantLabViewModel.effortLabel("high"), "High")
        XCTAssertEqual(AssistantLabViewModel.effortLabel("xhigh"), "Xhigh")
        XCTAssertEqual(AssistantLabViewModel.effortLabel("default"), "Default")
        XCTAssertEqual(AssistantLabViewModel.effortLabel(""), "")
    }

    @MainActor
    func testPricesArePerMillionTokens() async {
        XCTAssertEqual(
            AssistantLabViewModel.priceText(prompt: "0.000003", completion: "0.000015"),
            "Input $3.000 / 1M · output $15.000 / 1M"
        )
        XCTAssertEqual(
            AssistantLabViewModel.priceText(prompt: "0", completion: "0.0000001"),
            "Input $0.000 / 1M · output $0.100 / 1M"
        )
        XCTAssertEqual(AssistantLabViewModel.priceText(prompt: nil, completion: nil), "Input Unknown · output Unknown")
        XCTAssertEqual(
            AssistantLabViewModel.priceText(prompt: nil, completion: "0.000015"),
            "Input Unknown · output $15.000 / 1M"
        )
        // Not a number, negative, not finite.
        XCTAssertEqual(AssistantLabViewModel.priceText(prompt: "free", completion: "-0.000001"), "Input Unknown · output Unknown")
        XCTAssertEqual(AssistantLabViewModel.priceText(prompt: "nan", completion: "inf"), "Input Unknown · output Unknown")
        XCTAssertEqual(AssistantLabViewModel.priceText(prompt: "", completion: " "), "Input Unknown · output Unknown")
    }

    // MARK: - Recent comparisons (AC-IOS-126)

    @MainActor
    func testComparisonsAreTheFiftyNewestTimedAnswers() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        XCTAssertTrue(model.comparisons.isEmpty)

        // Sixty timed answers in three sessions, stored out of order, and some answers without timing.
        var older: [AssistantTurn] = []
        var middle: [AssistantTurn] = []
        var newer: [AssistantTurn] = []
        for index in 0..<20 {
            older.append(turn("answer-\(index)", offset: TimeInterval(index)))
            middle.append(turn("answer-\(index + 20)", offset: TimeInterval(index + 20)))
            newer.append(turn("answer-\(index + 40)", offset: TimeInterval(index + 40)))
        }
        middle.append(turn("untimed-a", offset: 500, timed: false))
        newer.insert(turn("untimed-b", offset: 600, timed: false), at: 0)
        try await store(middle, startedAfter: 20, in: owners)
        try await store(newer, startedAfter: 40, in: owners)
        try await store(older, startedAfter: 0, in: owners)
        await owners.history.reload()

        let rows = model.comparisons
        XCTAssertEqual(rows.count, 50)
        let all = older + middle + newer
        let answers = rows.map { row in all.first { $0.id == row.id }?.answer ?? "" }
        let expected = (10..<60).reversed().map { "answer-\($0)" }
        XCTAssertEqual(answers, expected, "Newest first, across every session, and only the fifty newest.")
        XCTAssertEqual(Set(rows.map(\.id)).count, 50)
    }

    @MainActor
    func testAComparisonRowSaysWhatWasCompared() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        let fresh = turn("fresh", offset: 1)
        let retried = turn("retried", offset: 2, reusedText: true)
        let direct = turn("direct", offset: 3, provider: .openrouter, question: AssistantTurn.latestConversation)
        try await store([fresh, retried, direct], startedAfter: 0, in: owners)
        await owners.history.reload()

        let rows = model.comparisons
        XCTAssertEqual(rows.map(\.id), [direct.id, retried.id, fresh.id])

        XCTAssertEqual(
            rows[2],
            ComparisonRow(
                id: fresh.id,
                model: "gpt-5.6-sol",
                detail: "meta · low · fresh transcript",
                timing: "2.31 s total · PC · Codex",
                question: "What did design say they need?"
            )
        )
        XCTAssertEqual(rows[1].detail, "meta · low · same text")
        XCTAssertEqual(rows[0].model, "test/direct")
        XCTAssertEqual(rows[0].detail, "meta · default · fresh transcript")
        XCTAssertEqual(rows[0].timing, "2.31 s total · OpenRouter · direct")
        XCTAssertEqual(rows[0].question, "", "The sentinel of OpenRouter answers is not a question.")

        // Each row opens its run details.
        XCTAssertEqual(model.turn(id: retried.id), retried)
        XCTAssertNil(model.turn(id: UUID()))
    }

    @MainActor
    func testTheModelThatRanIsNamedWhenTheServiceSaidSo() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        var answered = turn("answered", offset: 1)
        answered.performance?.execution = RelayExecution(
            model: "gpt-6-astra",
            reasoningEffort: "medium",
            codexMs: 600,
            relayTotalMs: 620,
            requestReadMs: 2,
            relayOverheadMs: 20
        )
        try await store([answered], startedAfter: 0, in: owners)
        await owners.history.reload()

        XCTAssertEqual(model.comparisons.first?.model, "gpt-6-astra")
        XCTAssertEqual(model.comparisons.first?.detail, "meta · medium · fresh transcript")
    }

    @MainActor
    func testTheActiveSessionsAnswersAreCompared() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        try await store([turn("stored", offset: 1)], startedAfter: 0, in: owners)
        await owners.history.reload()
        await owners.offerCodexCatalogue()
        _ = try await owners.startListening()
        try await owners.say("What is the capital of France?")

        await owners.session.assist()

        let active = try XCTUnwrap(owners.session.session?.assistantTurns.last)
        let rows = model.comparisons
        XCTAssertEqual(rows.count, 2)
        XCTAssertEqual(rows.first?.id, active.id, "The answer of the running session is the newest.")
        XCTAssertEqual(rows.first?.question, "Fake detected question")
        XCTAssertEqual(model.turn(id: active.id), active)
    }

    // MARK: - Codex selection (AC-IOS-115 to 117)

    @MainActor
    func testCodexSelection() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        XCTAssertEqual(model.provider, .codex)
        XCTAssertEqual(model.selectedModelID, "gpt-5.6-sol")
        XCTAssertEqual(model.reasoningCaption, "Reasoning: low")
        XCTAssertEqual(model.refreshTitle, "Refresh PC model list")
        XCTAssertTrue(model.explanation.hasPrefix("Selection applies to your next Assist and session summary."))
        XCTAssertFalse(model.showsPairFirst)
        XCTAssertFalse(model.keyPanelFirst)

        // Nothing loaded yet: no rows and no Reasoning row.
        XCTAssertTrue(model.codexModels.isEmpty)
        XCTAssertFalse(model.showsReasoningRow)
        XCTAssertTrue(model.reasoningEfforts.isEmpty)

        owners.assistant.adoptCatalogue(FixtureValues.codexCatalogue)
        XCTAssertEqual(model.codexModels.map(\.id), ["gpt-6-astra", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-luna", "gpt-5.3-codex-spark"])
        XCTAssertTrue(model.showsReasoningRow)
        XCTAssertEqual(model.reasoningValue, "Low")
        XCTAssertEqual(model.reasoningEfforts, ["none", "low", "medium", "high"])
        XCTAssertTrue(model.filteredOpenRouterModels.isEmpty, "The OpenRouter picker has nothing to list for Codex.")

        // "None (no reasoning)" results in "Reasoning: none" (AC-IOS-116).
        model.selectEffort("none")
        XCTAssertEqual(model.reasoningCaption, "Reasoning: none")
        XCTAssertEqual(model.reasoningValue, "None")

        // Another model keeps the effort when it lists it, and takes its first one otherwise (AC-IOS-117).
        model.selectCodexModel(FixtureValues.codexCatalogue[3])
        XCTAssertEqual(model.selectedModelID, "gpt-5.6-luna")
        XCTAssertEqual(model.reasoningCaption, "Reasoning: none")
        model.selectCodexModel(FixtureValues.codexCatalogue[0])
        XCTAssertEqual(model.selectedModelID, "gpt-6-astra")
        XCTAssertEqual(model.reasoningCaption, "Reasoning: low")
        XCTAssertEqual(model.reasoningEfforts, ["low", "medium", "high"])
    }

    @MainActor
    func testCodexWithoutAPairingAsksToPairFirst() async {
        XCTAssertTrue(makeModel(TestOwners(paired: false)).showsPairFirst)
        XCTAssertFalse(makeModel(TestOwners(paired: false, answers: .openrouter)).showsPairFirst)
    }

    @MainActor
    func testTheCatalogueLoadsWhenTheScreenAppears() async {
        // AC-IOS-114
        let owners = TestOwners()
        let model = makeModel(owners)
        await owners.offerCodexCatalogue()

        await model.appeared()

        XCTAssertEqual(model.codexModels.count, 5)
        XCTAssertFalse(model.isLoadingCatalogue)
        XCTAssertEqual(
            model.catalogueMessage,
            InlineMessage("From this PC's Codex catalog. Access still depends on your subscription.")
        )

        // A failed refresh says so and keeps the rows.
        await owners.codex.setCatalogue(.failure(RelayFailure.local(.unreachable)))
        await model.refresh()
        XCTAssertEqual(model.catalogueMessage?.isError, true)
        XCTAssertEqual(model.codexModels.count, 5)
    }

    // MARK: - OpenRouter (AC-IOS-118 to 122)

    @MainActor
    func testOpenRouterSelection() async {
        let owners = TestOwners(answers: .openrouter)
        let model = makeModel(owners)
        XCTAssertEqual(model.provider, .openrouter)
        XCTAssertEqual(model.selectedModelID, "")
        XCTAssertNil(model.reasoningCaption)
        XCTAssertTrue(model.codexModels.isEmpty)
        XCTAssertFalse(model.showsReasoningRow)
        XCTAssertEqual(model.openRouterModelRowTitle, "Choose a model")
        XCTAssertEqual(model.refreshTitle, "Refresh OpenRouter models")
        XCTAssertEqual(
            model.explanation,
            "Answers bypass the PC and Codex CLI. Models may differ in availability and price. There are no automatic paid retries or fallback models."
        )

        owners.assistant.adoptCatalogue(FixtureValues.openRouterCatalogue)
        model.selectOpenRouterModel(FixtureValues.openRouterCatalogue[0])
        XCTAssertEqual(model.selectedModelID, "test/direct")
        XCTAssertEqual(model.openRouterModelRowTitle, "Change model")
        XCTAssertEqual(owners.assistant.configuration.reasoningEffort, "default")
    }

    @MainActor
    func testTheKeyPanelComesFirstUntilAKeyIsSaved() async {
        let owners = TestOwners(answers: .openrouter)
        let model = makeModel(owners)
        XCTAssertFalse(model.hasKey)
        XCTAssertTrue(model.keyPanelFirst)
        XCTAssertEqual(model.keyStateChip, StatusChipModel("No key saved", tone: .neutral, mark: .ring))
        XCTAssertNil(model.keyMessage)

        // A key with a space is refused and stays in the field.
        model.keyDraft = "not a key"
        model.saveKey()
        XCTAssertFalse(model.hasKey)
        XCTAssertEqual(model.keyDraft, "not a key")
        XCTAssertEqual(model.keyMessage?.isError, true)

        model.keyDraft = syntheticKey
        model.saveKey()
        XCTAssertTrue(model.hasKey)
        XCTAssertEqual(model.keyDraft, "", "The field is emptied after a save.")
        XCTAssertFalse(model.keyPanelFirst)
        XCTAssertEqual(model.keyStateChip, StatusChipModel("Key saved", tone: .success, mark: .check))
        XCTAssertEqual(model.keyMessage, InlineMessage("Key saved securely on this iPhone."))

        await model.verifyKey()
        XCTAssertFalse(model.isVerifyingKey)
        XCTAssertEqual(model.keyMessage, InlineMessage("Key verified. No inference request was made."))
        let verified = await owners.openRouter.verifiedKeys
        XCTAssertEqual(verified, [syntheticKey])

        model.removeKey()
        XCTAssertFalse(model.hasKey)
        XCTAssertTrue(model.keyPanelFirst)
        XCTAssertEqual(model.keyStateChip.text, "No key saved")
        XCTAssertEqual(model.keyMessage, InlineMessage("Key removed."))

        XCTAssertFalse(makeModel(TestOwners()).keyPanelFirst, "Codex has no key panel.")
    }

    @MainActor
    func testTheModelSearchMatchesNameOrIdWithoutRegardToCase() async {
        // AC-IOS-120
        let owners = TestOwners(answers: .openrouter)
        let model = makeModel(owners)
        let catalogue = [
            AssistantModelOption(id: "test/direct", name: "Test Direct", reasoningEfforts: ["default"]),
            AssistantModelOption(id: "vendor/spark-mini", name: "Spark Mini", reasoningEfforts: ["default"]),
            AssistantModelOption(id: "other/large", name: "Large Reasoner", reasoningEfforts: ["default"])
        ]
        owners.assistant.adoptCatalogue(catalogue)

        XCTAssertEqual(model.filteredOpenRouterModels, catalogue)
        model.searchText = "SPARK"
        XCTAssertEqual(model.filteredOpenRouterModels.map(\.id), ["vendor/spark-mini"])
        model.searchText = "reasoner"
        XCTAssertEqual(model.filteredOpenRouterModels.map(\.id), ["other/large"], "The name matches.")
        model.searchText = "VENDOR/"
        XCTAssertEqual(model.filteredOpenRouterModels.map(\.id), ["vendor/spark-mini"], "The id matches.")
        model.searchText = "  direct  "
        XCTAssertEqual(model.filteredOpenRouterModels.map(\.id), ["test/direct"])
        model.searchText = "nothing like this"
        XCTAssertTrue(model.filteredOpenRouterModels.isEmpty)
        model.searchText = "   "
        XCTAssertEqual(model.filteredOpenRouterModels.count, 3)
    }

    // MARK: - Switching the service

    @MainActor
    func testChoosingAServiceSwitchesAndReloadsTheCatalogue() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        await owners.openRouter.setCatalogue(.success(FixtureValues.openRouterCatalogue))

        model.selectProvider(.openrouter)

        XCTAssertEqual(model.provider, .openrouter, "The switch happens at once.")
        await owners.waitUntil("the OpenRouter catalogue loaded") { !model.filteredOpenRouterModels.isEmpty }
        XCTAssertEqual(model.filteredOpenRouterModels, FixtureValues.openRouterCatalogue)
        XCTAssertEqual(model.catalogueMessage?.isError, false)

        // The row of the selected service does nothing.
        model.selectProvider(.openrouter)
        let calls = await owners.openRouter.catalogueCalls
        XCTAssertEqual(calls, 1)

        // Each service has its own selection back (AC-IOS-124).
        model.selectProvider(.codex)
        XCTAssertEqual(model.provider, .codex)
        XCTAssertEqual(model.selectedModelID, "gpt-5.6-sol")
        await owners.codex.waitForCatalogueCalls(count: 1)
    }

    @MainActor
    func testNothingChangesWhileAnAssistRuns() async {
        // AC-IOS-125
        let owners = TestOwners()
        let model = makeModel(owners)
        XCTAssertFalse(model.isLocked)

        owners.activity.isAssisting = true
        XCTAssertTrue(model.isLocked)
        model.selectProvider(.openrouter)
        XCTAssertEqual(model.provider, .codex, "The service cannot change while an answer is being made.")
        let calls = await owners.openRouter.catalogueCalls
        XCTAssertEqual(calls, 0)

        owners.activity.isAssisting = false
        XCTAssertFalse(model.isLocked)
    }
}
