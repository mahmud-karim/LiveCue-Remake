import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Apple speech (D-28) in the model manager, the readiness and the settings: unavailable on iOS 18 to 25,
/// ready without a manual step once the system's language asset is installed, and the download with its
/// progress and failure states when it is not. Every test uses a fake system recogniser.
final class SystemSpeechModelTests: XCTestCase {
    /// An error with a text of its own, as the system would throw.
    private struct DownloadFailure: LocalizedError {
        var errorDescription: String? {
            "The speech asset could not be downloaded."
        }
    }

    @MainActor
    private func makeModels(_ engine: (any SystemSpeechRecognizing)?, activity given: SessionActivity? = nil) -> OnDeviceModelManager {
        let activity = given ?? SessionActivity()
        let loader = FakeEngineLoader(voz: FakeBatchEngine(), parakeet: FakeStreamingEngine())
        guard let engine else {
            return OnDeviceModelManager(loader: loader, activity: activity, clock: ManualClock())
        }
        return OnDeviceModelManager(loader: loader, activity: activity, clock: ManualClock(), systemSpeech: engine)
    }

    // MARK: - iOS 18 to 25

    @MainActor
    func testOnAnOlderSystemAppleIsUnavailableWithItsReason() async {
        let models = makeModels(nil)

        XCTAssertEqual(models.systemSpeechAvailability, .unavailable(reason: "Requires iOS 26 or later"))
        XCTAssertEqual(models.systemSpeechNote, "Requires iOS 26 or later")
        XCTAssertFalse(models.isLoaded(for: .apple))
        XCTAssertNil(models.loadedModel(for: .apple))
        XCTAssertNil(models.systemSpeechEngine)

        // Asking changes nothing, and nothing can be downloaded.
        await models.refreshSystemSpeech()
        XCTAssertEqual(models.systemSpeechAvailability, .unavailable(reason: "Requires iOS 26 or later"))
        XCTAssertEqual(models.statusLine, "Requires iOS 26 or later")
        do {
            try await models.prepare(.apple)
            XCTFail("The preparation should have failed.")
        } catch {
            XCTAssertEqual(error.localizedDescription, "Requires iOS 26 or later")
        }
        XCTAssertEqual(models.preparation?.failed, true)
        XCTAssertFalse(models.isPreparing)
        XCTAssertFalse(models.isLoaded(for: .apple))
    }

    @MainActor
    func testTheRecogniserOfAnOlderSystemDoesNothing() async {
        let engine = UnavailableSystemSpeech()

        XCTAssertEqual(engine.knownAvailability, .unavailable(reason: "Requires iOS 26 or later"))
        let asked = await engine.availability()
        XCTAssertEqual(asked, .unavailable(reason: "Requires iOS 26 or later"))
        do {
            _ = try await engine.begin()
            XCTFail("There is nothing to begin.")
        } catch {
            XCTAssertEqual(error as? SystemSpeechFailure, SystemSpeechFailure("Requires iOS 26 or later"))
        }
        try? await engine.process(samples: [0, 0])
        try? await engine.finish()
        await engine.cancel()
    }

    @MainActor
    func testSettingsRefuseAppleOnAnOlderSystem() async {
        let owners = TestOwners()

        XCTAssertEqual(owners.readiness.unavailableReason(for: .apple), "Requires iOS 26 or later")
        for provider in SpeechProvider.allCases where provider != .apple {
            XCTAssertNil(owners.readiness.unavailableReason(for: provider), provider.rawValue)
        }
        owners.speech.select(.apple)
        XCTAssertEqual(owners.speech.provider, .meta)
        XCTAssertEqual(owners.preferences.speechProvider, .meta)

        // A stored choice from elsewhere is kept, and is simply not ready.
        let stored = TestOwners(provider: .apple)
        XCTAssertEqual(stored.speech.provider, .apple)
        XCTAssertFalse(stored.readiness.speechReady)
        XCTAssertFalse(stored.readiness.systemReady)
        stored.speech.select(.meta)
        XCTAssertEqual(stored.speech.provider, .meta, "It can be left.")
    }

    // MARK: - iOS 26: asking the system

    @MainActor
    func testNothingIsKnownUntilTheSystemWasAsked() async {
        let engine = FakeSystemSpeech(answer: .installed)
        let models = makeModels(engine)

        XCTAssertEqual(models.systemSpeechAvailability, .unknown)
        XCTAssertEqual(models.systemSpeechNote, "Checking Apple speech…")
        XCTAssertFalse(models.isLoaded(for: .apple))
        XCTAssertNil(models.systemSpeechEngine)
        let asked = await engine.availabilityCount
        XCTAssertEqual(asked, 0, "The system is not asked at launch unless Apple is the provider.")
    }

    @MainActor
    func testAnInstalledAssetMakesAppleReadyWithoutAManualStep() async {
        let engine = FakeSystemSpeech(answer: .installed)
        let models = makeModels(engine)

        await models.refreshSystemSpeech()

        XCTAssertEqual(models.systemSpeechAvailability, .installed)
        XCTAssertNil(models.systemSpeechNote)
        XCTAssertTrue(models.isLoaded(for: .apple))
        XCTAssertEqual(models.loadedModel(for: .apple), TranscriptionModel.apple)
        XCTAssertNotNil(models.systemSpeechEngine)
        XCTAssertEqual(models.statusLine, "Ready")
        XCTAssertNil(models.preparation)
        XCTAssertNil(models.loadedVariant, "Nothing is loaded into the app.")
        // The other on-device providers are not touched by it.
        XCTAssertFalse(models.isLoaded(for: .voz))
        XCTAssertFalse(models.isLoaded(for: .parakeet))
        let installs = await engine.installCount
        XCTAssertEqual(installs, 0, "Asking downloads nothing.")
    }

    @MainActor
    func testReadinessFollowsTheAsset() async {
        let engine = FakeSystemSpeech(answer: .needsDownload)
        let owners = TestOwners(provider: .apple, answers: .openrouter, systemSpeech: engine)
        XCTAssertFalse(owners.readiness.needsPC)
        XCTAssertFalse(owners.readiness.speechReady)
        XCTAssertNil(owners.readiness.unavailableReason(for: .apple))

        await owners.models.refreshSystemSpeech()
        XCTAssertFalse(owners.readiness.speechReady, "The asset is not on the phone yet.")
        XCTAssertEqual(owners.models.statusLine, "Prepare a model")

        await engine.setAnswer(.installed)
        await owners.models.refreshSystemSpeech()
        XCTAssertTrue(owners.readiness.speechReady)

        // The system took the asset away again.
        await engine.setAnswer(.needsDownload)
        await owners.models.refreshSystemSpeech()
        XCTAssertFalse(owners.readiness.speechReady)

        // An iPhone the recogniser does not support: the reason reaches the settings.
        await engine.setAnswer(.unavailable(reason: "Not supported on this iPhone"))
        await owners.models.refreshSystemSpeech()
        XCTAssertFalse(owners.readiness.speechReady)
        XCTAssertEqual(owners.readiness.unavailableReason(for: .apple), "Not supported on this iPhone")
        XCTAssertEqual(owners.models.statusLine, "Not supported on this iPhone")
    }

    @MainActor
    func testChoosingAppleAsksTheSystemWhenNothingIsKnown() async {
        let engine = FakeSystemSpeech(answer: .installed)
        let owners = TestOwners(systemSpeech: engine)

        owners.speech.select(.apple)
        XCTAssertEqual(owners.speech.provider, .apple)
        XCTAssertEqual(owners.models.statusLine, "Checking Apple speech…")
        await SpeechTest.waitUntil { owners.models.systemSpeechAvailability == .installed }
        XCTAssertEqual(owners.models.statusLine, "Ready")
        XCTAssertTrue(owners.readiness.speechReady)

        // Leaving releases nothing of the system's: coming back is ready at once.
        owners.speech.select(.meta)
        XCTAssertEqual(owners.models.statusLine, "Prepare a model")
        owners.speech.select(.apple)
        XCTAssertEqual(owners.models.statusLine, "Ready")
        XCTAssertTrue(owners.readiness.speechReady)
        let asked = await engine.availabilityCount
        XCTAssertEqual(asked, 1)
    }

    @MainActor
    func testTheSystemIsNotAskedDuringASession() async {
        let engine = FakeSystemSpeech(answer: .installed)
        let activity = SessionActivity()
        let models = makeModels(engine, activity: activity)
        activity.isActive = true

        await models.refreshSystemSpeech()

        XCTAssertEqual(models.systemSpeechAvailability, .unknown)
        let asked = await engine.availabilityCount
        XCTAssertEqual(asked, 0)
    }

    // MARK: - iOS 26: the download

    @MainActor
    func testTheDownloadShowsItsProgressAndEndsReady() async throws {
        let engine = FakeSystemSpeech(answer: .needsDownload)
        let gate = FakeGate()
        await engine.setInstall(
            steps: [ModelPreparationProgress(fraction: 0.4, phase: .downloading, counter: .none)],
            gate: gate
        )
        let models = makeModels(engine)
        await models.refreshSystemSpeech()
        XCTAssertFalse(models.isLoaded(for: .apple))

        let preparing = Task { try await models.prepare(.apple) }
        await SpeechTest.waitUntil { models.preparation?.fraction == 0.4 }

        XCTAssertTrue(models.isPreparing)
        XCTAssertEqual(models.preparation?.variant, "apple")
        XCTAssertNil(models.preparation?.counterText, "The system reports no byte or file count.")
        XCTAssertEqual(models.preparation?.failed, false)
        XCTAssertEqual(models.statusLine, "Downloading the English speech asset…")
        XCTAssertFalse(models.isLoaded(for: .apple))

        await gate.open()
        try await preparing.value

        XCTAssertFalse(models.isPreparing)
        XCTAssertNil(models.preparation)
        XCTAssertEqual(models.statusLine, "Ready")
        XCTAssertTrue(models.isLoaded(for: .apple))
        XCTAssertEqual(models.systemSpeechAvailability, .installed)
        let installs = await engine.installCount
        XCTAssertEqual(installs, 1)
    }

    @MainActor
    func testAFailedDownloadKeepsItsBlockAndRethrows() async {
        let engine = FakeSystemSpeech(answer: .needsDownload)
        await engine.setInstall(failure: DownloadFailure())
        let models = makeModels(engine)

        do {
            try await models.prepare(.apple)
            XCTFail("The preparation should have failed.")
        } catch {
            XCTAssertEqual(error.localizedDescription, "The speech asset could not be downloaded.")
        }

        XCTAssertFalse(models.isPreparing)
        XCTAssertEqual(models.preparation?.variant, "apple")
        XCTAssertEqual(models.preparation?.failed, true)
        XCTAssertNotNil(models.preparation?.finishedAt)
        XCTAssertEqual(models.statusLine, "Preparation failed")
        XCTAssertFalse(models.isLoaded(for: .apple))
        XCTAssertEqual(models.systemSpeechAvailability, .needsDownload)

        // Trying again works once the system delivers.
        await engine.setInstall()
        try? await models.prepare(.apple)
        XCTAssertTrue(models.isLoaded(for: .apple))
        XCTAssertNil(models.preparation)
        XCTAssertEqual(models.statusLine, "Ready")
    }

    @MainActor
    func testADownloadThatLeavesTheAssetMissingIsAFailure() async {
        let engine = FakeSystemSpeech(answer: .needsDownload)
        await engine.setInstall(answerAfterwards: nil)
        let models = makeModels(engine)

        do {
            try await models.prepare(.apple)
            XCTFail("The preparation should have failed.")
        } catch {
            XCTAssertEqual(error.localizedDescription, "The speech asset is not installed yet. Try again in a moment.")
        }
        XCTAssertEqual(models.preparation?.failed, true)
        XCTAssertEqual(models.statusLine, "Preparation failed")
        XCTAssertFalse(models.isLoaded(for: .apple))
    }

    @MainActor
    func testTheProviderIsLockedWhileTheAssetDownloads() async throws {
        let engine = FakeSystemSpeech(answer: .needsDownload)
        let gate = FakeGate()
        await engine.setInstall(gate: gate)
        let owners = TestOwners(provider: .apple, systemSpeech: engine)

        let preparing = Task { try await owners.models.prepare(.apple) }
        await SpeechTest.waitUntil { owners.models.isPreparing }
        XCTAssertEqual(owners.readiness.providerLockReason, .preparingModel)
        owners.speech.select(.meta)
        XCTAssertEqual(owners.speech.provider, .apple, "The choice is locked while the asset downloads.")

        await gate.open()
        try await preparing.value
        XCTAssertNil(owners.readiness.providerLockReason)
        XCTAssertTrue(owners.readiness.speechReady)
    }

    @MainActor
    func testPreparingAppleLeavesALoadedRecogniserOfAnotherProviderAlone() async throws {
        let engine = FakeSystemSpeech(answer: .installed)
        let models = makeModels(engine)
        let parakeet = FakeStreamingEngine()
        models.installPreloaded(variant: "parakeet", voz: nil, parakeet: parakeet)

        try await models.prepare(.apple)

        XCTAssertTrue(models.isLoaded(for: .apple))
        XCTAssertTrue(models.isLoaded(for: .parakeet), "The system's recogniser takes no memory of the app.")
        let cleanups = await parakeet.cleanupCount
        XCTAssertEqual(cleanups, 0)
    }

    // MARK: - The mark of an unanswered question

    func testTheMarkIsSeenOnceByTheLaunchAfterAnUnansweredQuestion() throws {
        let suite = "SystemSpeechModelTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let mark = SpeechProbeMark(defaults: defaults)

        XCTAssertFalse(mark.takeUnanswered(), "Nothing was asked yet.")

        // Asked and answered: the next launch finds nothing.
        mark.asking()
        mark.answered()
        XCTAssertFalse(mark.takeUnanswered())

        // Asked, and the process ended before the answer: the next launch sees it once.
        mark.asking()
        XCTAssertTrue(defaults.bool(forKey: SpeechProbeMark.key))
        XCTAssertTrue(SpeechProbeMark(defaults: defaults).takeUnanswered())
        XCTAssertFalse(SpeechProbeMark(defaults: defaults).takeUnanswered(), "The launch after that asks again.")
    }
}
