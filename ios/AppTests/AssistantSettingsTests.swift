import LiveCueCore
import Observation
import XCTest
@testable import LiveCueRemake

/// Answer selection, catalogue loading and the OpenRouter key.
final class AssistantSettingsTests: XCTestCase {
    private let codexLoadedMessage = "From this PC's Codex catalog. Access still depends on your subscription."
    private let openRouterLoadedMessage = "Direct from OpenRouter. Choose a model; API charges are separate from your Codex subscription."
    private let catalogueFailurePrefix = "Could not load models. Restart the updated PC app and check the connection. "
    private let malformedKeyMessage = "This key contains spaces or line breaks. Paste it again exactly as issued."
    /// Not a key of any service.
    private let syntheticKey = "synthetic-unit-key"

    /// An answer service whose catalogue waits until the test lets it go.
    private actor GatedCatalogueService: AnswerService {
        nonisolated let provider: AssistantProvider

        nonisolated var supportsSummary: Bool {
            provider == .codex
        }

        /// Opens when a load has begun.
        let entered = FakeGate()
        /// The loads wait for this one.
        let release = FakeGate()
        private let result: Result<[AssistantModelOption], any Error>
        private(set) var calls = 0

        init(provider: AssistantProvider, result: Result<[AssistantModelOption], any Error>) {
            self.provider = provider
            self.result = result
        }

        func loadCatalogue() async throws -> [AssistantModelOption] {
            calls += 1
            await entered.open()
            await release.wait()
            return try result.get()
        }

        func answer(
            _ request: AssistRequest,
            onProgress: @escaping @Sendable (String) async -> Void
        ) async throws -> AssistResponse {
            throw OpenRouterError.unavailable
        }

        func summarize(_ request: SummaryRequest) async throws -> SummaryResponse {
            throw OpenRouterError.unavailable
        }

        func cancel(requestId: UUID) async {}
    }

    private struct Services: AnswerServiceProviding {
        let codex: any AnswerService
        let openRouter: any AnswerService

        func service(for provider: AssistantProvider) -> any AnswerService {
            switch provider {
            case .codex: return codex
            case .openrouter: return openRouter
            }
        }
    }

    /// `AssistantSettings` on fakes, built by itself so that a test chooses what was stored before.
    @MainActor private final class Bench {
        let preferences: FakePreferences
        let secrets: FakeSecrets
        let pairingStore: FakePairingStore
        let codex: FakeAnswerService
        let openRouter: FakeAnswerService
        let activity: SessionActivity
        let assistant: AssistantSettings
        private(set) var providerChanges = 0

        init(
            paired: Bool = true,
            openRouterKey: String? = nil,
            stored: AssistantConfiguration = AssistantConfiguration(),
            instruction: String = "",
            codexService: (any AnswerService)? = nil
        ) {
            let preferences = FakePreferences()
            preferences.assistantConfiguration = stored
            preferences.assistantInstruction = instruction

            var keys: [SecretKind: String] = [:]
            if let openRouterKey {
                keys[.openRouterAPIKey] = openRouterKey
            }
            let secrets = FakeSecrets(keys)
            let pairingStore = FakePairingStore(paired ? FixtureValues.pairing : nil)
            let codex = FakeAnswerService(provider: .codex)
            let openRouter = FakeAnswerService(provider: .openrouter)
            let activity = SessionActivity()

            let services: Services
            if let codexService {
                services = Services(codex: codexService, openRouter: openRouter)
            } else {
                services = Services(codex: codex, openRouter: openRouter)
            }
            let assistant = AssistantSettings(
                preferences: preferences,
                secrets: secrets,
                pairingStore: pairingStore,
                answers: services,
                keyVerifier: openRouter,
                activity: activity
            )

            self.preferences = preferences
            self.secrets = secrets
            self.pairingStore = pairingStore
            self.codex = codex
            self.openRouter = openRouter
            self.activity = activity
            self.assistant = assistant

            assistant.onProviderChanged = { [weak self] in
                self?.providerChanges += 1
            }
        }
    }

    private func option(_ id: String, efforts: [String]) -> AssistantModelOption {
        AssistantModelOption(id: id, name: id, reasoningEfforts: efforts)
    }

    // MARK: - What is stored

    @MainActor
    func testTheStoredSelectionInstructionAndKeyAreReadWhenItIsBuilt() async {
        let fresh = Bench()
        XCTAssertEqual(fresh.assistant.configuration, AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "low", provider: .codex))
        XCTAssertEqual(fresh.assistant.instruction, "")
        XCTAssertFalse(fresh.assistant.hasOpenRouterKey)
        XCTAssertTrue(fresh.assistant.catalogue.isEmpty)
        XCTAssertNil(fresh.assistant.catalogueMessage)
        XCTAssertNil(fresh.assistant.openRouterKeyMessage)
        XCTAssertFalse(fresh.assistant.isLoadingCatalogue)
        XCTAssertFalse(fresh.assistant.isVerifyingOpenRouterKey)

        let stored = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)
        let used = Bench(openRouterKey: syntheticKey, stored: stored, instruction: "Answer briefly")
        XCTAssertEqual(used.assistant.configuration, stored)
        XCTAssertEqual(used.assistant.instruction, "Answer briefly")
        XCTAssertTrue(used.assistant.hasOpenRouterKey)
    }

    @MainActor
    func testTheInstructionIsSavedOnEveryChangeAndIsObserved() async {
        let bench = Bench(instruction: "Old")
        let observed = expectation(description: "A change of the instruction reaches an observer.")
        withObservationTracking {
            _ = bench.assistant.instruction
        } onChange: {
            observed.fulfill()
        }

        bench.assistant.instruction = "Answer briefly"
        await fulfillment(of: [observed], timeout: 5)
        XCTAssertEqual(bench.assistant.instruction, "Answer briefly")
        XCTAssertEqual(bench.preferences.assistantInstruction, "Answer briefly")

        bench.assistant.instruction = "Answer briefly."
        XCTAssertEqual(bench.preferences.assistantInstruction, "Answer briefly.")
        bench.assistant.instruction = ""
        XCTAssertEqual(bench.assistant.instruction, "")
        XCTAssertEqual(bench.preferences.assistantInstruction, "")
    }

    // MARK: - Switching the answer service (AC-IOS-124)

    @MainActor
    func testSwitchingSavesAndRestoresTheSelectionOfEachService() async {
        let bench = Bench()
        let assistant = bench.assistant
        await bench.codex.setCatalogue(.success(FixtureValues.codexCatalogue))
        await assistant.loadCatalogue()
        XCTAssertFalse(assistant.catalogue.isEmpty)
        XCTAssertNotNil(assistant.catalogueMessage)

        assistant.selectCodexModel(option("gpt-6-luna", efforts: ["low", "medium", "high"]))
        assistant.selectReasoningEffort("high")
        let codexChoice = AssistantConfiguration(model: "gpt-6-luna", reasoningEffort: "high", provider: .codex)
        XCTAssertEqual(assistant.configuration, codexChoice)

        // To OpenRouter with nothing remembered: no model, effort "default".
        assistant.switchProvider(.openrouter)
        let openRouterDefault = AssistantConfiguration(model: "", reasoningEffort: "default", provider: .openrouter)
        XCTAssertEqual(assistant.configuration, openRouterDefault)
        XCTAssertEqual(bench.preferences.assistantConfiguration, openRouterDefault)
        XCTAssertEqual(bench.preferences.remembered[.openrouter], openRouterDefault)
        XCTAssertEqual(bench.preferences.remembered[.codex], codexChoice, "The outgoing service keeps its own selection.")
        XCTAssertTrue(assistant.catalogue.isEmpty, "The catalogue belonged to the other service.")
        XCTAssertNil(assistant.catalogueMessage)
        XCTAssertEqual(bench.providerChanges, 1)

        assistant.selectOpenRouterModel(option("test/direct", efforts: ["default"]))
        let openRouterChoice = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)
        XCTAssertEqual(assistant.configuration, openRouterChoice)

        // Back and forth: each service gets its own model and effort back.
        assistant.switchProvider(.codex)
        XCTAssertEqual(assistant.configuration, codexChoice)
        XCTAssertEqual(bench.preferences.assistantConfiguration, codexChoice)
        XCTAssertEqual(bench.preferences.remembered[.openrouter], openRouterChoice)

        assistant.switchProvider(.openrouter)
        XCTAssertEqual(assistant.configuration, openRouterChoice)
        XCTAssertEqual(bench.preferences.assistantConfiguration, openRouterChoice)
        XCTAssertEqual(bench.preferences.remembered[.codex], codexChoice)
        XCTAssertEqual(bench.providerChanges, 3)

        // The service that is already selected: nothing happens.
        assistant.adoptCatalogue(FixtureValues.openRouterCatalogue)
        assistant.switchProvider(.openrouter)
        XCTAssertEqual(assistant.configuration, openRouterChoice)
        XCTAssertEqual(assistant.catalogue, FixtureValues.openRouterCatalogue)
        XCTAssertEqual(bench.providerChanges, 3)
    }

    @MainActor
    func testSwitchingToCodexWithNothingRememberedGivesTheFirstRunDefault() async {
        let stored = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)
        let bench = Bench(stored: stored)

        bench.assistant.switchProvider(.codex)

        let firstRun = AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "low", provider: .codex)
        XCTAssertEqual(bench.assistant.configuration, firstRun)
        XCTAssertEqual(bench.preferences.assistantConfiguration, firstRun)
        XCTAssertEqual(bench.preferences.remembered[.codex], firstRun)
        XCTAssertEqual(bench.preferences.remembered[.openrouter], stored)
    }

    @MainActor
    func testSwitchingIsRefusedWhileAssisting() async {
        let bench = Bench()
        bench.assistant.adoptCatalogue(FixtureValues.codexCatalogue)
        bench.activity.isAssisting = true

        bench.assistant.switchProvider(.openrouter)

        XCTAssertEqual(bench.assistant.configuration, AssistantConfiguration())
        XCTAssertEqual(bench.assistant.catalogue, FixtureValues.codexCatalogue)
        XCTAssertTrue(bench.preferences.remembered.isEmpty)
        XCTAssertEqual(bench.providerChanges, 0)

        bench.activity.isAssisting = false
        bench.assistant.switchProvider(.openrouter)
        XCTAssertEqual(bench.assistant.configuration.provider, .openrouter)
        XCTAssertEqual(bench.providerChanges, 1)
    }

    // MARK: - Model and effort (AC-IOS-117)

    @MainActor
    func testChoosingACodexModelKeepsTheEffortWhenTheModelListsIt() async {
        let bench = Bench()
        let assistant = bench.assistant
        let sol = option("gpt-5.6-sol", efforts: ["none", "low", "medium", "high"])
        let astra = option("gpt-6-astra", efforts: ["low", "medium", "high"])

        // "low" is listed by both.
        assistant.selectCodexModel(astra)
        XCTAssertEqual(assistant.configuration, AssistantConfiguration(model: "gpt-6-astra", reasoningEffort: "low", provider: .codex))
        assistant.selectReasoningEffort("high")
        assistant.selectCodexModel(sol)
        XCTAssertEqual(assistant.configuration, AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "high", provider: .codex))

        // "none" is not listed by Astra: its first effort is used.
        assistant.selectReasoningEffort("none")
        XCTAssertEqual(assistant.configuration.reasoningEffort, "none")
        assistant.selectCodexModel(astra)
        XCTAssertEqual(assistant.configuration, AssistantConfiguration(model: "gpt-6-astra", reasoningEffort: "low", provider: .codex))

        // A model that lists another first effort, and one that lists none.
        assistant.selectReasoningEffort("high")
        assistant.selectCodexModel(option("gpt-test", efforts: ["medium"]))
        XCTAssertEqual(assistant.configuration.reasoningEffort, "medium")
        assistant.selectCodexModel(option("gpt-bare", efforts: []))
        XCTAssertEqual(assistant.configuration, AssistantConfiguration(model: "gpt-bare", reasoningEffort: "low", provider: .codex))

        // Every change was saved at once, as the current selection and under the service's own key.
        XCTAssertEqual(bench.preferences.assistantConfiguration, assistant.configuration)
        XCTAssertEqual(bench.preferences.remembered[.codex], assistant.configuration)
        XCTAssertNil(bench.preferences.remembered[.openrouter])
        XCTAssertEqual(bench.providerChanges, 0, "Choosing a model does not change the service.")
    }

    @MainActor
    func testAnOpenRouterModelHasTheDefaultEffortAndTheChoicesOfTheOtherServiceAreIgnored() async {
        let bench = Bench()
        let assistant = bench.assistant
        let codexChoice = assistant.configuration

        // Codex is selected: an OpenRouter model is not a choice.
        assistant.selectOpenRouterModel(option("test/direct", efforts: ["default"]))
        XCTAssertEqual(assistant.configuration, codexChoice)

        assistant.switchProvider(.openrouter)
        assistant.selectOpenRouterModel(option("test/direct", efforts: ["low", "high"]))
        let openRouterChoice = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)
        XCTAssertEqual(assistant.configuration, openRouterChoice)
        XCTAssertEqual(bench.preferences.assistantConfiguration, openRouterChoice)
        XCTAssertEqual(bench.preferences.remembered[.openrouter], openRouterChoice)

        // OpenRouter is selected: a Codex model and a reasoning effort are not choices.
        assistant.selectCodexModel(option("gpt-6-luna", efforts: ["low"]))
        assistant.selectReasoningEffort("high")
        XCTAssertEqual(assistant.configuration, openRouterChoice)
    }

    // MARK: - Catalogue

    @MainActor
    func testLoadingTheCodexCatalogue() async {
        let bench = Bench()
        let assistant = bench.assistant
        await bench.codex.setCatalogue(.success(FixtureValues.codexCatalogue))

        await assistant.loadCatalogue()
        XCTAssertEqual(assistant.catalogue, FixtureValues.codexCatalogue)
        XCTAssertEqual(assistant.catalogueMessage, InlineMessage(codexLoadedMessage))
        XCTAssertEqual(assistant.catalogueMessage?.isError, false)
        XCTAssertFalse(assistant.isLoadingCatalogue)

        // A failure says why. The rows that were loaded stay.
        await bench.codex.setCatalogue(.failure(RelayFailure.wire(.assistantCatalogUnavailable)))
        await assistant.loadCatalogue()
        XCTAssertEqual(
            assistant.catalogueMessage,
            InlineMessage(
                catalogueFailurePrefix + "The PC could not read Codex's model list. Keep Codex signed in, then retry. Your selected model has not changed.",
                isError: true
            )
        )
        XCTAssertEqual(assistant.catalogue, FixtureValues.codexCatalogue)
        XCTAssertFalse(assistant.isLoadingCatalogue)

        await bench.codex.setCatalogue(.failure(RelayFailure.local(.unreachable)))
        await assistant.loadCatalogue()
        XCTAssertEqual(
            assistant.catalogueMessage,
            InlineMessage(
                catalogueFailurePrefix + "Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically.",
                isError: true
            )
        )

        // A load that works again replaces the message.
        await bench.codex.setCatalogue(.success([]))
        await assistant.loadCatalogue()
        XCTAssertTrue(assistant.catalogue.isEmpty)
        XCTAssertEqual(assistant.catalogueMessage, InlineMessage(codexLoadedMessage))

        let calls = await bench.codex.catalogueCalls
        XCTAssertEqual(calls, 4)
        let openRouterCalls = await bench.openRouter.catalogueCalls
        XCTAssertEqual(openRouterCalls, 0)
        XCTAssertEqual(assistant.configuration, AssistantConfiguration(), "Loading never changes the selection.")
    }

    @MainActor
    func testTheCodexCatalogueIsNotAskedForWithoutAPairing() async {
        let bench = Bench(paired: false)

        await bench.assistant.loadCatalogue()

        XCTAssertEqual(bench.assistant.catalogueMessage, InlineMessage("Pair your PC first."))
        XCTAssertTrue(bench.assistant.catalogue.isEmpty)
        XCTAssertFalse(bench.assistant.isLoadingCatalogue)
        let calls = await bench.codex.catalogueCalls
        XCTAssertEqual(calls, 0)
    }

    @MainActor
    func testLoadingTheOpenRouterCatalogueNeedsNeitherAPairingNorAKey() async {
        let stored = AssistantConfiguration(model: "", reasoningEffort: "default", provider: .openrouter)
        let bench = Bench(paired: false, stored: stored)
        let assistant = bench.assistant
        XCTAssertFalse(assistant.hasOpenRouterKey)
        await bench.openRouter.setCatalogue(.success(FixtureValues.openRouterCatalogue))

        await assistant.loadCatalogue()
        XCTAssertEqual(assistant.catalogue, FixtureValues.openRouterCatalogue)
        XCTAssertEqual(assistant.catalogueMessage, InlineMessage(openRouterLoadedMessage))

        // A failure shows the text of that failure.
        await bench.openRouter.setCatalogue(.failure(OpenRouterError.rateLimited))
        await assistant.loadCatalogue()
        XCTAssertEqual(
            assistant.catalogueMessage,
            InlineMessage("OpenRouter rate-limited this request. Wait briefly and try again.", isError: true)
        )
        XCTAssertEqual(assistant.catalogue, FixtureValues.openRouterCatalogue)

        await bench.openRouter.setCatalogue(.failure(OpenRouterError.unavailable))
        await assistant.loadCatalogue()
        XCTAssertEqual(
            assistant.catalogueMessage,
            InlineMessage(
                "OpenRouter could not complete this request. Check your connection or try again later. No fallback service was used.",
                isError: true
            )
        )

        let codexCalls = await bench.codex.catalogueCalls
        XCTAssertEqual(codexCalls, 0)
    }

    /// AC-IOS-123: the service is switched while the catalogue of the other one is still loading.
    @MainActor
    func testAStaleCatalogueResultIsDropped() async {
        let slowCodex = GatedCatalogueService(provider: .codex, result: .success(FixtureValues.codexCatalogue))
        let bench = Bench(codexService: slowCodex)
        let assistant = bench.assistant
        await bench.openRouter.setCatalogue(.success(FixtureValues.openRouterCatalogue))

        let codexLoad = Task { await assistant.loadCatalogue() }
        await slowCodex.entered.wait()
        XCTAssertTrue(assistant.isLoadingCatalogue)

        // A second load for the same service while one is running is ignored.
        await assistant.loadCatalogue()
        var codexCalls = await slowCodex.calls
        XCTAssertEqual(codexCalls, 1)
        XCTAssertTrue(assistant.isLoadingCatalogue)

        // The new service's catalogue is there while the old load is still out.
        assistant.switchProvider(.openrouter)
        XCTAssertFalse(assistant.isLoadingCatalogue)
        await assistant.loadCatalogue()
        XCTAssertEqual(assistant.catalogue, FixtureValues.openRouterCatalogue)
        XCTAssertEqual(assistant.catalogueMessage, InlineMessage(openRouterLoadedMessage))

        await slowCodex.release.open()
        await codexLoad.value
        XCTAssertEqual(assistant.catalogue, FixtureValues.openRouterCatalogue, "The stale result is never shown.")
        XCTAssertEqual(assistant.catalogueMessage, InlineMessage(openRouterLoadedMessage))
        XCTAssertFalse(assistant.isLoadingCatalogue)
        XCTAssertEqual(assistant.configuration.provider, .openrouter)
        codexCalls = await slowCodex.calls
        XCTAssertEqual(codexCalls, 1)
    }

    @MainActor
    func testAStaleCatalogueFailureIsDropped() async {
        let failingCodex = GatedCatalogueService(provider: .codex, result: .failure(RelayFailure.local(.timeout)))
        let bench = Bench(codexService: failingCodex)
        let assistant = bench.assistant

        let codexLoad = Task { await assistant.loadCatalogue() }
        await failingCodex.entered.wait()
        assistant.switchProvider(.openrouter)

        await failingCodex.release.open()
        await codexLoad.value

        XCTAssertNil(assistant.catalogueMessage, "The failure of the other service is not shown.")
        XCTAssertTrue(assistant.catalogue.isEmpty)
        XCTAssertFalse(assistant.isLoadingCatalogue)
    }

    @MainActor
    func testALoadAfterSwitchingBackReplacesTheOneThatWasOut() async {
        let slowCodex = GatedCatalogueService(provider: .codex, result: .success(FixtureValues.codexCatalogue))
        let bench = Bench(codexService: slowCodex)
        let assistant = bench.assistant

        let first = Task { await assistant.loadCatalogue() }
        await slowCodex.entered.wait()
        assistant.switchProvider(.openrouter)
        assistant.switchProvider(.codex)
        XCTAssertFalse(assistant.isLoadingCatalogue)

        // The earlier load is stale, so this one is not "a second load while one is running".
        let second = Task { await assistant.loadCatalogue() }
        await slowCodex.release.open()
        await first.value
        await second.value

        let calls = await slowCodex.calls
        XCTAssertEqual(calls, 2)
        XCTAssertEqual(assistant.catalogue, FixtureValues.codexCatalogue)
        XCTAssertEqual(assistant.catalogueMessage, InlineMessage(codexLoadedMessage))
        XCTAssertFalse(assistant.isLoadingCatalogue)
    }

    @MainActor
    func testTheAssistPipelineCanStoreTheCatalogueItFetched() async {
        let bench = Bench()

        bench.assistant.adoptCatalogue(FixtureValues.codexCatalogue)

        XCTAssertEqual(bench.assistant.catalogue, FixtureValues.codexCatalogue)
        XCTAssertNil(bench.assistant.catalogueMessage)
        XCTAssertFalse(bench.assistant.isLoadingCatalogue)
        let calls = await bench.codex.catalogueCalls
        XCTAssertEqual(calls, 0)
    }

    // MARK: - OpenRouter key

    @MainActor
    func testSavingAnOpenRouterKey() async {
        let bench = Bench()
        let assistant = bench.assistant

        // Empty.
        XCTAssertFalse(assistant.saveOpenRouterKey(""))
        XCTAssertEqual(assistant.openRouterKeyMessage, InlineMessage("Enter a valid API key.", isError: true))

        // A space or a line break anywhere: the malformed-key text.
        for malformed in ["synthetic unit key", " " + syntheticKey, syntheticKey + "\n", "synthetic\tunit-key", " "] {
            XCTAssertFalse(assistant.saveOpenRouterKey(malformed), malformed)
            XCTAssertEqual(assistant.openRouterKeyMessage, InlineMessage(malformedKeyMessage, isError: true), malformed)
        }
        XCTAssertFalse(assistant.hasOpenRouterKey)
        XCTAssertNil(bench.secrets.read(.openRouterAPIKey))

        // The Keychain refuses: nothing is stored anywhere.
        bench.secrets.failStatus = -25308
        XCTAssertFalse(assistant.saveOpenRouterKey(syntheticKey))
        XCTAssertEqual(
            assistant.openRouterKeyMessage,
            InlineMessage("Could not save the key in Keychain (status -25308). No insecure fallback was used.", isError: true)
        )
        XCTAssertFalse(assistant.hasOpenRouterKey)
        XCTAssertNil(bench.secrets.read(.openRouterAPIKey))

        bench.secrets.failStatus = nil
        XCTAssertTrue(assistant.saveOpenRouterKey(syntheticKey))
        XCTAssertEqual(assistant.openRouterKeyMessage, InlineMessage("Key saved securely on this iPhone."))
        XCTAssertTrue(assistant.hasOpenRouterKey)
        XCTAssertEqual(bench.secrets.read(.openRouterAPIKey), syntheticKey)
        XCTAssertNil(bench.secrets.read(.metaAPIKey), "The key goes to its own Keychain item only.")

        // A refused text leaves the saved key alone.
        XCTAssertFalse(assistant.saveOpenRouterKey("another key"))
        XCTAssertTrue(assistant.hasOpenRouterKey)
        XCTAssertEqual(bench.secrets.read(.openRouterAPIKey), syntheticKey)
    }

    @MainActor
    func testVerifyingTheOpenRouterKey() async {
        let bench = Bench(openRouterKey: syntheticKey)
        let assistant = bench.assistant

        await assistant.verifyOpenRouterKey()
        XCTAssertEqual(assistant.openRouterKeyMessage, InlineMessage("Key verified. No inference request was made."))
        XCTAssertFalse(assistant.isVerifyingOpenRouterKey)

        await bench.openRouter.setVerify(.failure(OpenRouterError.invalidKey))
        await assistant.verifyOpenRouterKey()
        XCTAssertEqual(
            assistant.openRouterKeyMessage,
            InlineMessage("OpenRouter rejected this API key. Replace it in Assistant models & timing.", isError: true)
        )
        XCTAssertTrue(assistant.hasOpenRouterKey, "A rejected key stays until the user removes it.")
        XCTAssertFalse(assistant.isVerifyingOpenRouterKey)

        await bench.openRouter.setVerify(.failure(OpenRouterError.unavailable))
        await assistant.verifyOpenRouterKey()
        XCTAssertEqual(
            assistant.openRouterKeyMessage,
            InlineMessage(
                "OpenRouter could not complete this request. Check your connection or try again later. No fallback service was used.",
                isError: true
            )
        )

        let verified = await bench.openRouter.verifiedKeys
        XCTAssertEqual(verified, [syntheticKey, syntheticKey, syntheticKey])
        let answers = await bench.openRouter.answerRequests
        XCTAssertTrue(answers.isEmpty, "Verifying makes no inference request.")
    }

    @MainActor
    func testVerifyingWithoutAKeyAsksForOne() async {
        let bench = Bench()

        await bench.assistant.verifyOpenRouterKey()

        XCTAssertEqual(
            bench.assistant.openRouterKeyMessage,
            InlineMessage("Add your OpenRouter API key in Assistant models & timing first.", isError: true)
        )
        XCTAssertFalse(bench.assistant.isVerifyingOpenRouterKey)
        let verified = await bench.openRouter.verifiedKeys
        XCTAssertEqual(verified, [])
    }

    @MainActor
    func testRemovingTheOpenRouterKey() async {
        let bench = Bench(openRouterKey: syntheticKey)
        let assistant = bench.assistant

        // The Keychain refuses: the key is still there.
        bench.secrets.failStatus = -25308
        assistant.removeOpenRouterKey()
        XCTAssertEqual(
            assistant.openRouterKeyMessage,
            InlineMessage("Keychain could not remove this key (status -25308). Try again.", isError: true)
        )
        XCTAssertTrue(assistant.hasOpenRouterKey)
        XCTAssertEqual(bench.secrets.read(.openRouterAPIKey), syntheticKey)

        bench.secrets.failStatus = nil
        assistant.removeOpenRouterKey()
        XCTAssertEqual(assistant.openRouterKeyMessage, InlineMessage("Key removed."))
        XCTAssertFalse(assistant.hasOpenRouterKey)
        XCTAssertNil(bench.secrets.read(.openRouterAPIKey))
    }
}
