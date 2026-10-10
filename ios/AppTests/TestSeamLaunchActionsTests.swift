import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Each launch action of the test seam.
final class TestSeamLaunchActionsTests: XCTestCase {
    /// The three stores a launch acts on, as fakes, with values a launch would find from an earlier one.
    @MainActor private final class Stores {
        let preferences = FakePreferences()
        let secrets = FakeSecrets([.metaAPIKey: "an-earlier-meta-key", .openRouterAPIKey: "an-earlier-openrouter-key"])
        let pairingStore = FakePairingStore()
        let earlierSelection = AssistantConfiguration(model: "test/direct", reasoningEffort: "default", provider: .openrouter)

        init() {
            preferences.speechProvider = .qwen3
            preferences.assistantInstruction = "Kept between launches."
            preferences.assistantConfiguration = earlierSelection
            preferences.remember(earlierSelection)
        }

        func launch(_ arguments: [String]) {
            TestSeamLaunchActions.apply(
                TestMode(arguments: arguments),
                preferences: preferences,
                secrets: secrets,
                pairingStore: pairingStore
            )
        }
    }

    // MARK: - The base launch

    @MainActor
    func testABaseLaunchSelectsMetaResetsTheSelectionAndSavesThePlaceholderKey() async {
        let stores = Stores()
        stores.launch([])

        XCTAssertEqual(stores.preferences.speechProvider, .meta)
        XCTAssertEqual(stores.preferences.resetCount, 1)
        XCTAssertEqual(stores.preferences.assistantConfiguration, AssistantConfiguration())
        XCTAssertNil(stores.preferences.rememberedConfiguration(for: .openrouter))
        XCTAssertEqual(stores.secrets.read(.metaAPIKey), "fixture-meta-key")
        XCTAssertEqual(stores.secrets.read(.metaAPIKey), FixtureValues.metaKey)
        XCTAssertEqual(stores.secrets.read(.openRouterAPIKey), "an-earlier-openrouter-key", "The OpenRouter key is left alone.")
        XCTAssertTrue(stores.pairingStore.saved.isEmpty)
        XCTAssertEqual(stores.pairingStore.removeKeychainCopyCalls, 0)
        XCTAssertEqual(stores.preferences.assistantInstruction, "Kept between launches.", "The instruction is not an action of the seam.")
    }

    @MainActor
    func testArgumentsThatOnlyChangeFixturesActLikeABaseLaunch() async {
        let stores = Stores()
        stores.launch(["-pc-offline", "-pc-catalog-slow", "-pc-model-slow-start", "-pc-model-start-error", "-assist-fails-once", "-pairing-persistence-test"])

        XCTAssertEqual(stores.preferences.speechProvider, .meta)
        XCTAssertEqual(stores.preferences.resetCount, 1)
        XCTAssertEqual(stores.secrets.read(.metaAPIKey), FixtureValues.metaKey)
        XCTAssertEqual(stores.secrets.read(.openRouterAPIKey), "an-earlier-openrouter-key")
        XCTAssertTrue(stores.pairingStore.saved.isEmpty)
        XCTAssertEqual(stores.pairingStore.removeKeychainCopyCalls, 0)
    }

    // MARK: - One action per argument

    @MainActor
    func testOnDeviceReadySelectsParakeet() async {
        let stores = Stores()
        stores.launch(["-on-device-ready"])
        XCTAssertEqual(stores.preferences.speechProvider, .parakeet)
        XCTAssertEqual(stores.preferences.resetCount, 1)
        XCTAssertEqual(stores.secrets.read(.metaAPIKey), FixtureValues.metaKey)
    }

    @MainActor
    func testOpenRouterPersistenceKeepsTheSavedSelection() async {
        let stores = Stores()
        stores.launch(["-openrouter-persistence-test"])

        XCTAssertEqual(stores.preferences.resetCount, 0)
        XCTAssertEqual(stores.preferences.assistantConfiguration, stores.earlierSelection)
        XCTAssertEqual(stores.preferences.rememberedConfiguration(for: .openrouter), stores.earlierSelection)
        XCTAssertEqual(stores.preferences.speechProvider, .meta, "The provider is still forced.")
    }

    @MainActor
    func testMetaKeyPersistenceLeavesTheKeyAsItWas() async {
        let stores = Stores()
        stores.launch(["-meta-key-persistence"])
        XCTAssertEqual(stores.secrets.read(.metaAPIKey), "an-earlier-meta-key")

        // And an absent key stays absent.
        let empty = Stores()
        try? empty.secrets.remove(.metaAPIKey)
        empty.launch(["-meta-key-persistence"])
        XCTAssertNil(empty.secrets.read(.metaAPIKey))
    }

    @MainActor
    func testMetaNoKeyRemovesTheKey() async {
        let stores = Stores()
        stores.launch(["-meta-no-key"])
        XCTAssertNil(stores.secrets.read(.metaAPIKey), "The placeholder is saved first and removed after.")
        XCTAssertEqual(stores.secrets.read(.openRouterAPIKey), "an-earlier-openrouter-key")

        // Also when the key of an earlier launch was to be kept.
        let kept = Stores()
        kept.launch(["-meta-key-persistence", "-meta-no-key"])
        XCTAssertNil(kept.secrets.read(.metaAPIKey))
    }

    @MainActor
    func testOpenRouterUIResetRemovesTheOpenRouterKey() async {
        let stores = Stores()
        stores.launch(["-openrouter-ui-reset"])
        XCTAssertNil(stores.secrets.read(.openRouterAPIKey))
        XCTAssertEqual(stores.secrets.read(.metaAPIKey), FixtureValues.metaKey)
    }

    @MainActor
    func testSavePairingFixtureSavesTheSyntheticPairing() async {
        let stores = Stores()
        stores.launch(["-pairing-persistence-test", "-save-pairing-fixture"])

        XCTAssertEqual(stores.pairingStore.saved, [FixtureValues.pairing])
        XCTAssertEqual(stores.pairingStore.load(), FixtureValues.pairing)
        XCTAssertEqual(stores.pairingStore.load()?.endpoint, "https://pc.example.test:11443/")
        XCTAssertEqual(stores.pairingStore.removeKeychainCopyCalls, 0)
    }

    @MainActor
    func testDropTestKeychainRemovesTheKeychainCopy() async {
        let stores = Stores()
        stores.launch(["-pairing-persistence-test", "-drop-test-keychain"])

        XCTAssertEqual(stores.pairingStore.removeKeychainCopyCalls, 1)
        XCTAssertTrue(stores.pairingStore.saved.isEmpty)
    }

    // MARK: - Together

    @MainActor
    func testEveryActionRunsWhenEveryArgumentIsGiven() async {
        let stores = Stores()
        stores.launch([
            "-on-device-ready", "-meta-no-key", "-meta-key-persistence", "-openrouter-ui-reset",
            "-openrouter-persistence-test", "-pc-offline", "-pc-catalog-slow", "-pc-model-slow-start",
            "-pc-model-start-error", "-assist-fails-once", "-pairing-persistence-test",
            "-save-pairing-fixture", "-drop-test-keychain"
        ])

        XCTAssertEqual(stores.preferences.speechProvider, .parakeet)
        XCTAssertEqual(stores.preferences.resetCount, 0)
        XCTAssertEqual(stores.preferences.assistantConfiguration, stores.earlierSelection)
        XCTAssertNil(stores.secrets.read(.metaAPIKey))
        XCTAssertNil(stores.secrets.read(.openRouterAPIKey))
        XCTAssertEqual(stores.pairingStore.saved, [FixtureValues.pairing])
        XCTAssertEqual(stores.pairingStore.removeKeychainCopyCalls, 1)
    }

    @MainActor
    func testAStoreThatRefusesDoesNotStopTheOtherActions() async {
        let stores = Stores()
        stores.secrets.failStatus = -34018
        stores.pairingStore.saveFails = true

        stores.launch(["-on-device-ready", "-meta-no-key", "-openrouter-ui-reset", "-save-pairing-fixture", "-drop-test-keychain"])

        XCTAssertEqual(stores.preferences.speechProvider, .parakeet)
        XCTAssertEqual(stores.preferences.resetCount, 1)
        XCTAssertEqual(stores.secrets.read(.metaAPIKey), "an-earlier-meta-key", "The refused store kept what it had.")
        XCTAssertTrue(stores.pairingStore.saved.isEmpty)
        XCTAssertEqual(stores.pairingStore.removeKeychainCopyCalls, 1, "The last action still ran.")
    }

    // MARK: - With the real stores of fixture mode

    /// The pairing path of the UI test that relaunches: the first launch saves the fixture pairing, the
    /// second drops the Keychain copy, and the pairing is still there.
    @MainActor
    func testThePairingSurvivesARelaunchThatDropsTheKeychainCopy() async throws {
        let keychain = KeychainStore(service: "com.mahmudkarim.LiveCueRemake.unit-tests.launch-actions")
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("TestSeamLaunchActionsTests-\(UUID().uuidString)", isDirectory: true)
        let names = StorageNames.test
        try keychain.delete(account: names.pairingAccount)
        defer {
            try? keychain.delete(account: names.pairingAccount)
            try? FileManager.default.removeItem(at: directory)
        }
        let preferences = FakePreferences()
        let secrets = FakeSecrets()

        let firstLaunch = FilePairingStore(keychain: keychain, names: names, directory: directory)
        TestSeamLaunchActions.apply(
            TestMode(arguments: ["-pairing-persistence-test", "-save-pairing-fixture"]),
            preferences: preferences,
            secrets: secrets,
            pairingStore: firstLaunch
        )
        XCTAssertEqual(firstLaunch.load(), FixtureValues.pairing)
        XCTAssertEqual(keychain.get(account: names.pairingAccount), FixtureValues.pairing.storedText)

        let secondLaunch = FilePairingStore(keychain: keychain, names: names, directory: directory)
        TestSeamLaunchActions.apply(
            TestMode(arguments: ["-pairing-persistence-test", "-drop-test-keychain"]),
            preferences: preferences,
            secrets: secrets,
            pairingStore: secondLaunch
        )
        XCTAssertNil(keychain.get(account: names.pairingAccount))
        XCTAssertEqual(secondLaunch.load(), FixtureValues.pairing, "The pairing is restored from the file.")
        XCTAssertEqual(secondLaunch.load()?.endpoint, "https://pc.example.test:11443/")
    }
}
