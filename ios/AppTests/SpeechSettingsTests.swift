import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Provider selection and the Meta key actions.
final class SpeechSettingsTests: XCTestCase {
    private let suiteName = "com.mahmudkarim.LiveCueRemake.unit-tests.speech-settings.\(UUID().uuidString)"
    private let missingKeyMessage = "Add your Meta API key in Transcription. It stays in this iPhone's Keychain."
    private let malformedKeyMessage = "This key contains spaces or line breaks. Paste it again exactly as issued."
    private let savedMessage = "Key saved securely on this iPhone. Meta connects directly; no PC needed."
    /// Not a key of any service.
    private let syntheticKey = "synthetic-unit-key"

    override func tearDown() {
        UserDefaults.standard.removePersistentDomain(forName: suiteName)
        super.tearDown()
    }

    /// The providers that `onProviderChanged` reported, and whether a change is blocked.
    @MainActor private final class Wiring {
        var reported: [SpeechProvider] = []
        var isBlocked = false
        var blockQuestions = 0
    }

    /// `SpeechSettings` on fakes, with the two closures that `AppEnvironment` installs.
    @MainActor
    private func makeSettings(
        provider: SpeechProvider = .meta,
        metaKey: String? = nil
    ) -> (speech: SpeechSettings, preferences: FakePreferences, secrets: FakeSecrets, tester: FakeMetaKeyTester, activity: SessionActivity, wiring: Wiring) {
        let preferences = FakePreferences()
        preferences.speechProvider = provider
        var keys: [SecretKind: String] = [:]
        if let metaKey {
            keys[.metaAPIKey] = metaKey
        }
        let secrets = FakeSecrets(keys)
        let tester = FakeMetaKeyTester()
        let activity = SessionActivity()
        let wiring = Wiring()
        let speech = SpeechSettings(preferences: preferences, secrets: secrets, keyTester: tester, activity: activity)
        speech.onProviderChanged = { [wiring] provider in
            wiring.reported.append(provider)
        }
        speech.changeIsBlocked = { [wiring] in
            wiring.blockQuestions += 1
            return wiring.isBlocked
        }
        return (speech, preferences, secrets, tester, activity, wiring)
    }

    // MARK: - The stored provider

    @MainActor
    func testAnUnknownStoredProviderReadsAsMeta() async throws {
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        let preferences = UserDefaultsPreferencesStore(defaults: defaults)
        let secrets = FakeSecrets()
        let tester = FakeMetaKeyTester()
        let activity = SessionActivity()

        // Nothing stored.
        XCTAssertEqual(SpeechSettings(preferences: preferences, secrets: secrets, keyTester: tester, activity: activity).provider, .meta)

        // A value this build does not know.
        defaults.set("whisper", forKey: StorageNames.speechProviderKey)
        XCTAssertEqual(SpeechSettings(preferences: preferences, secrets: secrets, keyTester: tester, activity: activity).provider, .meta)

        // A known value is restored.
        defaults.set("qwen3", forKey: StorageNames.speechProviderKey)
        XCTAssertEqual(SpeechSettings(preferences: preferences, secrets: secrets, keyTester: tester, activity: activity).provider, .qwen3)
    }

    @MainActor
    func testTheStoredProviderAndKeyAreReadWhenItIsBuilt() async {
        let without = makeSettings(provider: .parakeet)
        XCTAssertEqual(without.speech.provider, .parakeet)
        XCTAssertFalse(without.speech.hasMetaKey)
        XCTAssertNil(without.speech.metaKeyMessage)
        XCTAssertFalse(without.speech.isTestingMetaKey)

        let with = makeSettings(metaKey: syntheticKey)
        XCTAssertEqual(with.speech.provider, .meta)
        XCTAssertTrue(with.speech.hasMetaKey)
    }

    // MARK: - Choosing a provider (AC-IOS-094)

    @MainActor
    func testSelectingAProviderSavesItAndTellsTheOtherOwners() async {
        let made = makeSettings()

        for provider in [SpeechProvider.nemotron, .qwen3, .voz, .parakeet, .meta] {
            made.speech.select(provider)
            XCTAssertEqual(made.speech.provider, provider)
            XCTAssertEqual(made.preferences.speechProvider, provider)
        }
        XCTAssertEqual(made.wiring.reported, [.nemotron, .qwen3, .voz, .parakeet, .meta])

        // The provider that is already selected: nothing happens.
        made.speech.select(.meta)
        XCTAssertEqual(made.wiring.reported.count, 5)
    }

    @MainActor
    func testSelectingIsRefusedDuringASession() async {
        let made = makeSettings()
        made.activity.isActive = true

        made.speech.select(.parakeet)

        XCTAssertEqual(made.speech.provider, .meta)
        XCTAssertEqual(made.preferences.speechProvider, .meta)
        XCTAssertTrue(made.wiring.reported.isEmpty)

        made.activity.isActive = false
        made.speech.select(.parakeet)
        XCTAssertEqual(made.speech.provider, .parakeet)
        XCTAssertEqual(made.wiring.reported, [.parakeet])
    }

    @MainActor
    func testSelectingIsRefusedWhileAChangeIsBlocked() async {
        let made = makeSettings(provider: .voz)
        made.wiring.isBlocked = true

        made.speech.select(.meta)

        XCTAssertEqual(made.speech.provider, .voz)
        XCTAssertEqual(made.preferences.speechProvider, .voz)
        XCTAssertTrue(made.wiring.reported.isEmpty)
        XCTAssertEqual(made.wiring.blockQuestions, 1)

        made.wiring.isBlocked = false
        made.speech.select(.meta)
        XCTAssertEqual(made.speech.provider, .meta)
        XCTAssertEqual(made.wiring.reported, [.meta])
    }

    /// The block that `AppEnvironment` installs: an on-device model preparing, or a PC model changing.
    @MainActor
    func testSelectingIsRefusedWhileThePCModelIsChanging() async {
        let owners = TestOwners(provider: .nemotron)
        let starting = PCModelStatus(state: "starting", model: "nemotron", elapsedSeconds: 2, message: "Starting")
        await owners.gateway.setStatus(.success(starting))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(owners.readiness.providerLockReason, .pcModelChanging)

        owners.speech.select(.meta)
        XCTAssertEqual(owners.speech.provider, .nemotron)
        XCTAssertEqual(owners.preferences.speechProvider, .nemotron)

        await owners.gateway.setStatus(.success(PCModelStatus(state: "ready", model: "nemotron", message: "Ready")))
        await owners.relay.refreshPCModel()
        XCTAssertNil(owners.readiness.providerLockReason)
        owners.speech.select(.meta)
        XCTAssertEqual(owners.speech.provider, .meta)
        XCTAssertEqual(owners.preferences.speechProvider, .meta)
    }

    // MARK: - Saving the key (AC-IOS-109 as changed)

    @MainActor
    func testAnEmptyKeyGetsTheMissingKeyMessage() async {
        let made = makeSettings()

        for empty in ["", "   ", "\n", " \t\r\n "] {
            XCTAssertFalse(made.speech.saveMetaKey(empty))
            XCTAssertEqual(made.speech.metaKeyMessage, InlineMessage(missingKeyMessage, isError: true))
        }
        XCTAssertFalse(made.speech.hasMetaKey)
        XCTAssertNil(made.secrets.read(.metaAPIKey))
    }

    @MainActor
    func testAKeyThatContainsWhitespaceGetsTheMalformedKeyMessage() async {
        let made = makeSettings()

        for malformed in ["synthetic unit key", "synthetic\nunit-key", "synthetic\tunit-key", "  synthetic unit key  "] {
            XCTAssertFalse(made.speech.saveMetaKey(malformed), malformed)
            XCTAssertEqual(made.speech.metaKeyMessage, InlineMessage(malformedKeyMessage, isError: true), malformed)
        }
        XCTAssertFalse(made.speech.hasMetaKey)
        XCTAssertNil(made.secrets.read(.metaAPIKey))
    }

    @MainActor
    func testSavingAKeyTrimsItAndStoresItInTheKeychainOnly() async {
        let made = makeSettings()

        XCTAssertTrue(made.speech.saveMetaKey("  " + syntheticKey + "\n"))

        XCTAssertEqual(made.secrets.read(.metaAPIKey), syntheticKey)
        XCTAssertNil(made.secrets.read(.openRouterAPIKey))
        XCTAssertTrue(made.speech.hasMetaKey)
        XCTAssertEqual(made.speech.metaKeyMessage, InlineMessage(savedMessage))
        XCTAssertEqual(made.speech.metaKeyMessage?.isError, false)

        // A refused text leaves the saved key alone.
        XCTAssertFalse(made.speech.saveMetaKey("another key"))
        XCTAssertTrue(made.speech.hasMetaKey)
        XCTAssertEqual(made.secrets.read(.metaAPIKey), syntheticKey)
    }

    @MainActor
    func testAKeychainThatRefusesSavesNothing() async {
        let made = makeSettings()
        made.secrets.failStatus = -25308

        XCTAssertFalse(made.speech.saveMetaKey(syntheticKey))

        XCTAssertEqual(
            made.speech.metaKeyMessage,
            InlineMessage("Could not save the key in Keychain (status -25308). No insecure fallback was used.", isError: true)
        )
        XCTAssertFalse(made.speech.hasMetaKey)
        made.secrets.failStatus = nil
        XCTAssertNil(made.secrets.read(.metaAPIKey))
    }

    // MARK: - Testing and removing the key

    @MainActor
    func testTestingTheKey() async {
        let made = makeSettings(metaKey: syntheticKey)
        let speech = made.speech

        // While the test runs.
        await made.tester.setHold(true)
        let running = Task { await speech.testMetaKey() }
        // The fake has no wait of its own: look until it has the key, for at most five seconds.
        var looks = 0
        var reached = await made.tester.testedKeys
        while reached.isEmpty, looks < 5_000 {
            looks += 1
            try? await Task.sleep(nanoseconds: 1_000_000)
            reached = await made.tester.testedKeys
        }
        XCTAssertEqual(reached, [syntheticKey], "The test never reached the key tester.")
        XCTAssertTrue(speech.isTestingMetaKey)
        // A second tap while one test runs opens nothing more.
        await speech.testMetaKey()
        await made.tester.setHold(false)
        await made.tester.release()
        await running.value

        XCTAssertFalse(speech.isTestingMetaKey)
        XCTAssertEqual(speech.metaKeyMessage, InlineMessage("Meta connection verified directly. No microphone audio was sent."))
        var tested = await made.tester.testedKeys
        XCTAssertEqual(tested, [syntheticKey])

        // Each failure shows its own text.
        let failures: [(MetaSpeechError, String)] = [
            (.authentication, "Meta did not accept the connection. Check the API key, account access, and billing in Transcription."),
            (.connection, "Could not connect directly to Meta. Check this iPhone's internet connection and retry. No PC is required for Meta transcription."),
            (.provider, "Meta transcription stopped. Check API access, billing, or rate limits before retrying.")
        ]
        for (failure, text) in failures {
            await made.tester.setResult(.failure(failure))
            await speech.testMetaKey()
            XCTAssertEqual(speech.metaKeyMessage, InlineMessage(text, isError: true))
            XCTAssertFalse(speech.isTestingMetaKey)
            XCTAssertTrue(speech.hasMetaKey, "A key that failed the test stays until the user removes it.")
        }
        tested = await made.tester.testedKeys
        XCTAssertEqual(tested.count, 4)
    }

    @MainActor
    func testTestingWithoutAKeyAsksForOne() async {
        let made = makeSettings()

        await made.speech.testMetaKey()

        XCTAssertEqual(made.speech.metaKeyMessage, InlineMessage(missingKeyMessage, isError: true))
        XCTAssertFalse(made.speech.isTestingMetaKey)
        let tested = await made.tester.testedKeys
        XCTAssertEqual(tested, [])
    }

    @MainActor
    func testRemovingTheKey() async {
        let made = makeSettings(metaKey: syntheticKey)

        // The Keychain refuses: the key is still there.
        made.secrets.failStatus = -25308
        made.speech.removeMetaKey()
        XCTAssertEqual(
            made.speech.metaKeyMessage,
            InlineMessage("Keychain could not remove this key (status -25308). Try again.", isError: true)
        )
        XCTAssertTrue(made.speech.hasMetaKey)
        XCTAssertEqual(made.secrets.read(.metaAPIKey), syntheticKey)

        made.secrets.failStatus = nil
        made.speech.removeMetaKey()
        XCTAssertEqual(made.speech.metaKeyMessage, InlineMessage("Key removed."))
        XCTAssertFalse(made.speech.hasMetaKey)
        XCTAssertNil(made.secrets.read(.metaAPIKey))
    }

    // MARK: - During a session (AC-IOS-113)

    @MainActor
    func testTheKeyActionsAreRefusedDuringASession() async {
        let made = makeSettings(metaKey: syntheticKey)
        made.activity.isActive = true

        XCTAssertFalse(made.speech.saveMetaKey("another-synthetic-key"))
        await made.speech.testMetaKey()
        made.speech.removeMetaKey()

        XCTAssertEqual(made.secrets.read(.metaAPIKey), syntheticKey, "The key was neither replaced nor removed.")
        XCTAssertTrue(made.speech.hasMetaKey)
        XCTAssertNil(made.speech.metaKeyMessage)
        XCTAssertFalse(made.speech.isTestingMetaKey)
        let tested = await made.tester.testedKeys
        XCTAssertEqual(tested, [], "No connection was opened.")

        // After the session all three work again.
        made.activity.isActive = false
        XCTAssertTrue(made.speech.saveMetaKey("another-synthetic-key"))
        await made.speech.testMetaKey()
        made.speech.removeMetaKey()
        XCTAssertNil(made.secrets.read(.metaAPIKey))
        let testedAfterwards = await made.tester.testedKeys
        XCTAssertEqual(testedAfterwards, ["another-synthetic-key"])
    }
}
