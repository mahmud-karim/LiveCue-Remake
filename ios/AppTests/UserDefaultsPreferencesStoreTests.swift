import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Preference keys and their defaults (architecture 4.10).
///
/// Every test has a defaults suite of its own, so nothing here reads or writes the app's preferences.
final class UserDefaultsPreferencesStoreTests: XCTestCase {
    private let suiteName = "com.mahmudkarim.LiveCueRemake.unit-tests.preferences.\(UUID().uuidString)"

    override func tearDown() {
        UserDefaults.standard.removePersistentDomain(forName: suiteName)
        super.tearDown()
    }

    private func makeDefaults() throws -> UserDefaults {
        try XCTUnwrap(UserDefaults(suiteName: suiteName))
    }

    // MARK: - Defaults

    @MainActor
    func testAnEmptyStoreReadsTheDefaults() async throws {
        let store = UserDefaultsPreferencesStore(defaults: try makeDefaults())

        XCTAssertEqual(store.assistantInstruction, "")
        XCTAssertEqual(store.speechProvider, .meta)
        XCTAssertEqual(store.assistantConfiguration, AssistantConfiguration(model: "gpt-5.6-sol", reasoningEffort: "low", provider: .codex))
        XCTAssertNil(store.rememberedConfiguration(for: .codex))
        XCTAssertNil(store.rememberedConfiguration(for: .openrouter))
    }

    @MainActor
    func testReadingWritesNothing() async throws {
        let defaults = try makeDefaults()
        let store = UserDefaultsPreferencesStore(defaults: defaults)
        _ = store.assistantInstruction
        _ = store.speechProvider
        _ = store.assistantConfiguration

        for key in ["assistantInstruction", "speechProvider", "assistantConfiguration"] {
            XCTAssertNil(defaults.object(forKey: key), key)
        }
    }

    // MARK: - Keys

    @MainActor
    func testTheKeyNamesAreThoseOfTheDesign() async {
        XCTAssertEqual(StorageNames.instructionKey, "assistantInstruction")
        XCTAssertEqual(StorageNames.speechProviderKey, "speechProvider")
        XCTAssertEqual(StorageNames.configurationKey, "assistantConfiguration")
        XCTAssertEqual(UserDefaultsPreferencesStore.configurationKey(for: .codex), "assistantConfiguration.codex")
        XCTAssertEqual(UserDefaultsPreferencesStore.configurationKey(for: .openrouter), "assistantConfiguration.openrouter")
    }

    @MainActor
    func testTheInstructionIsStoredAsTextUnderItsKey() async throws {
        let defaults = try makeDefaults()
        let store = UserDefaultsPreferencesStore(defaults: defaults)

        store.assistantInstruction = "Answer in one sentence."
        XCTAssertEqual(defaults.string(forKey: "assistantInstruction"), "Answer in one sentence.")
        XCTAssertEqual(store.assistantInstruction, "Answer in one sentence.")

        store.assistantInstruction = ""
        XCTAssertEqual(store.assistantInstruction, "")
    }

    @MainActor
    func testTheProviderIsStoredAsItsRawValueUnderItsKey() async throws {
        let defaults = try makeDefaults()
        let store = UserDefaultsPreferencesStore(defaults: defaults)

        for provider in SpeechProvider.allCases {
            store.speechProvider = provider
            XCTAssertEqual(defaults.string(forKey: "speechProvider"), provider.rawValue)
            XCTAssertEqual(store.speechProvider, provider)
        }
        XCTAssertEqual(SpeechProvider.allCases.map(\.rawValue), ["meta", "nemotron", "qwen3", "voz", "parakeet", "apple", "fluid"])
    }

    @MainActor
    func testAnUnknownStoredProviderReadsAsMeta() async throws {
        let defaults = try makeDefaults()
        let store = UserDefaultsPreferencesStore(defaults: defaults)

        defaults.set("whisper", forKey: "speechProvider")
        XCTAssertEqual(store.speechProvider, .meta)
        defaults.set("", forKey: "speechProvider")
        XCTAssertEqual(store.speechProvider, .meta)
        defaults.set(Data([0x01]), forKey: "speechProvider")
        XCTAssertEqual(store.speechProvider, .meta)
    }

    @MainActor
    func testTheSelectionIsStoredAsJSONWithItsThreeMembers() async throws {
        let defaults = try makeDefaults()
        let store = UserDefaultsPreferencesStore(defaults: defaults)

        store.assistantConfiguration = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)

        let stored = try XCTUnwrap(defaults.data(forKey: "assistantConfiguration"))
        let json = try XCTUnwrap(JSONValue.parse(stored))
        XCTAssertEqual(json["model"]?.stringValue, "test/direct")
        XCTAssertEqual(json["reasoningEffort"]?.stringValue, "default")
        XCTAssertEqual(json["provider"]?.stringValue, "openrouter", "The provider is always written.")
        XCTAssertEqual(json.objectValue?.count, 3)
        XCTAssertEqual(
            store.assistantConfiguration,
            AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)
        )
        XCTAssertNil(defaults.object(forKey: "assistantConfiguration.openrouter"), "The selection is not the remembered one.")
    }

    @MainActor
    func testAStoredSelectionWithoutAProviderIsCodex() async throws {
        let defaults = try makeDefaults()
        defaults.set(Data("{\"model\":\"gpt-6-luna\",\"reasoningEffort\":\"high\"}".utf8), forKey: "assistantConfiguration")

        let store = UserDefaultsPreferencesStore(defaults: defaults)
        XCTAssertEqual(store.assistantConfiguration, AssistantConfiguration(model: "gpt-6-luna", reasoningEffort: "high", provider: .codex))
    }

    @MainActor
    func testAStoredSelectionThatDoesNotDecodeReadsAsTheDefault() async throws {
        let defaults = try makeDefaults()
        let store = UserDefaultsPreferencesStore(defaults: defaults)

        defaults.set(Data("not json".utf8), forKey: "assistantConfiguration")
        defaults.set(Data("{\"model\":1}".utf8), forKey: "assistantConfiguration.codex")
        defaults.set("text, not data", forKey: "assistantConfiguration.openrouter")

        XCTAssertEqual(store.assistantConfiguration, AssistantConfiguration())
        XCTAssertNil(store.rememberedConfiguration(for: .codex))
        XCTAssertNil(store.rememberedConfiguration(for: .openrouter))
    }

    // MARK: - Remembered selections

    @MainActor
    func testRememberWritesTheKeyOfTheConfigurationsOwnProvider() async throws {
        let defaults = try makeDefaults()
        let store = UserDefaultsPreferencesStore(defaults: defaults)
        let openRouter = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)
        let codex = AssistantConfiguration(model: "gpt-6-astra", reasoningEffort: "medium", provider: .codex)

        store.remember(openRouter)
        XCTAssertNotNil(defaults.data(forKey: "assistantConfiguration.openrouter"))
        XCTAssertNil(defaults.object(forKey: "assistantConfiguration.codex"))
        XCTAssertNil(defaults.object(forKey: "assistantConfiguration"), "Remembering does not change the selection.")
        XCTAssertEqual(store.rememberedConfiguration(for: .openrouter), openRouter)
        XCTAssertNil(store.rememberedConfiguration(for: .codex))
        XCTAssertEqual(store.assistantConfiguration, AssistantConfiguration())

        store.remember(codex)
        XCTAssertEqual(store.rememberedConfiguration(for: .codex), codex)
        XCTAssertEqual(store.rememberedConfiguration(for: .openrouter), openRouter)

        let replaced = AssistantConfiguration(model: "gpt-5.6-luna", reasoningEffort: "none", provider: .codex)
        store.remember(replaced)
        XCTAssertEqual(store.rememberedConfiguration(for: .codex), replaced)
    }

    @MainActor
    func testResetRemovesTheThreeConfigurationKeysAndNothingElse() async throws {
        let defaults = try makeDefaults()
        let store = UserDefaultsPreferencesStore(defaults: defaults)
        store.assistantInstruction = "Keep it short."
        store.speechProvider = .voz
        store.assistantConfiguration = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)
        store.remember(AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter))
        store.remember(AssistantConfiguration(model: "gpt-6-astra", reasoningEffort: "medium", provider: .codex))

        store.resetAssistantConfiguration()

        for key in ["assistantConfiguration", "assistantConfiguration.codex", "assistantConfiguration.openrouter"] {
            XCTAssertNil(defaults.object(forKey: key), key)
        }
        XCTAssertEqual(store.assistantConfiguration, AssistantConfiguration())
        XCTAssertNil(store.rememberedConfiguration(for: .codex))
        XCTAssertNil(store.rememberedConfiguration(for: .openrouter))
        XCTAssertEqual(store.assistantInstruction, "Keep it short.")
        XCTAssertEqual(store.speechProvider, .voz)
    }

    // MARK: - Persistence

    @MainActor
    func testAnotherStoreOnTheSameDefaultsReadsWhatWasWritten() async throws {
        let first = UserDefaultsPreferencesStore(defaults: try makeDefaults())
        let selection = AssistantConfiguration(model: "gpt-6-luna", reasoningEffort: "high", provider: .codex)
        first.assistantInstruction = "Prefer bullet points."
        first.speechProvider = .parakeet
        first.assistantConfiguration = selection
        first.remember(selection)

        let second = UserDefaultsPreferencesStore(defaults: try makeDefaults())
        XCTAssertEqual(second.assistantInstruction, "Prefer bullet points.")
        XCTAssertEqual(second.speechProvider, .parakeet)
        XCTAssertEqual(second.assistantConfiguration, selection)
        XCTAssertEqual(second.rememberedConfiguration(for: .codex), selection)
    }
}
