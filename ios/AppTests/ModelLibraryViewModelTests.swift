import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The Model Library entry and its progress block, which belongs to its model only (IOS-B13,
/// AC-IOS-145 to 148).
final class ModelLibraryViewModelTests: XCTestCase {
    /// The owners, with a model manager whose loader the test scripts.
    @MainActor
    private final class Rig {
        let owners: TestOwners
        let models: OnDeviceModelManager
        let model: ModelLibraryViewModel

        init(
            provider: SpeechProvider,
            steps: [ModelPreparationProgress] = [],
            failure: (any Error)? = nil,
            gate: FakeGate? = nil
        ) {
            let owners = TestOwners(provider: provider)
            let models = OnDeviceModelManager(
                loader: FakeEngineLoader(
                    voz: owners.vozEngine,
                    parakeet: owners.parakeetEngine,
                    steps: steps,
                    failure: failure,
                    gate: gate
                ),
                activity: owners.activity,
                clock: owners.clock
            )
            self.owners = owners
            self.models = models
            self.model = ModelLibraryViewModel(speech: owners.speech, models: models, app: owners.app, clock: owners.clock)
        }
    }

    /// An error with a text of its own, as a speech package would throw.
    private struct LoaderFailure: LocalizedError {
        var errorDescription: String? {
            "The model files could not be downloaded."
        }
    }

    private let halfway = ModelPreparationProgress(
        fraction: 0.5,
        phase: .downloading,
        counter: .files(completed: 3, total: 6)
    )

    // MARK: - The entry

    @MainActor
    func testTheEntryOfEachOnDeviceProvider() async {
        let voz = Rig(provider: .voz).model
        XCTAssertEqual(voz.model, TranscriptionModel.voz)
        XCTAssertEqual(voz.model?.displayName, "Voz on Assist")
        XCTAssertEqual(voz.descriptionText, "~467 MB · Transcribes when you tap Assist")
        XCTAssertEqual(voz.packageLabel, "Desert Ant 3.1.0")
        XCTAssertTrue(voz.showsVozUsageSentence)

        let parakeet = Rig(provider: .parakeet).model
        XCTAssertEqual(parakeet.model, TranscriptionModel.parakeet)
        XCTAssertEqual(parakeet.model?.displayName, "Live Parakeet")
        XCTAssertEqual(parakeet.descriptionText, "~230 MB · English live captions · 320 ms mode")
        XCTAssertEqual(parakeet.packageLabel, "FluidAudio 0.15.6")
        XCTAssertFalse(parakeet.showsVozUsageSentence)
    }

    @MainActor
    func testTheEntryOfAppleNamesNoSizeAndHasNothingToUnload() async throws {
        let engine = FakeSystemSpeech(answer: .needsDownload)
        let owners = TestOwners(provider: .apple, systemSpeech: engine)
        let model = ModelLibraryViewModel(speech: owners.speech, models: owners.models, app: owners.app, clock: owners.clock)

        XCTAssertEqual(model.model, TranscriptionModel.apple)
        XCTAssertEqual(model.model?.displayName, "Apple live captions")
        XCTAssertEqual(model.descriptionText, "English live captions · built into iOS")
        XCTAssertEqual(model.packageLabel, "Built into iOS")
        XCTAssertFalse(model.showsVozUsageSentence)
        XCTAssertFalse(model.isLoaded)
        XCTAssertEqual(model.mainButtonTitle, "Download & use")
        XCTAssertFalse(model.showsUnload)

        // "Download & use": the system installs its asset; nothing is loaded into the app.
        await model.prepare()
        XCTAssertTrue(model.isLoaded)
        XCTAssertNil(model.block)
        XCTAssertEqual(model.statusLine, "Ready")
        XCTAssertEqual(model.mainButtonTitle, "Check again")
        XCTAssertFalse(model.showsUnload, "The recogniser belongs to the system.")
        XCTAssertNil(owners.app.alertMessage)
        let installs = await engine.installCount
        XCTAssertEqual(installs, 1)
    }

    @MainActor
    func testAFailedAppleDownloadShowsTheBlockAndTheAlert() async {
        let engine = FakeSystemSpeech(answer: .needsDownload)
        await engine.setInstall(steps: [halfway], failure: LoaderFailure())
        let owners = TestOwners(provider: .apple, systemSpeech: engine)
        let model = ModelLibraryViewModel(speech: owners.speech, models: owners.models, app: owners.app, clock: owners.clock)

        await model.prepare()

        XCTAssertFalse(model.isLoaded)
        XCTAssertEqual(model.block?.failed, true)
        XCTAssertEqual(model.statusLine, "Preparation failed")
        XCTAssertEqual(owners.app.alertMessage, "The model files could not be downloaded.")
        XCTAssertEqual(model.mainButtonTitle, "Download & use")
    }

    @MainActor
    func testAStreamingProviderHasNoEntry() async {
        for provider in [SpeechProvider.meta, .nemotron, .qwen3] {
            let model = Rig(provider: provider).model

            XCTAssertNil(model.model)
            XCTAssertEqual(model.descriptionText, "")
            XCTAssertEqual(model.packageLabel, "")
            XCTAssertNil(model.block)
            XCTAssertFalse(model.isLoaded)

            // Nothing can be prepared for it.
            await model.prepare()
            XCTAssertFalse(model.isPreparing)
            XCTAssertNil(model.block)
        }
    }

    @MainActor
    func testBeforeAnythingWasPreparedThereIsNoBlock() async {
        let model = Rig(provider: .parakeet).model

        XCTAssertFalse(model.isLoaded)
        XCTAssertFalse(model.isPreparing)
        XCTAssertEqual(model.mainButtonTitle, "Download & use")
        XCTAssertEqual(model.statusLine, "Prepare a model")
        XCTAssertNil(model.block)
    }

    // MARK: - Preparing (AC-IOS-145, 146)

    @MainActor
    func testPreparingShowsTheBlockAndItGoesWhenTheModelIsReady() async throws {
        let gate = FakeGate()
        let rig = Rig(provider: .parakeet, steps: [halfway], gate: gate)
        let model = rig.model

        let preparing = Task { await model.prepare() }
        await SpeechTest.waitUntil { model.block?.percentText == "50% setup" }

        XCTAssertTrue(model.isPreparing)
        XCTAssertFalse(model.isLoaded)
        let block = try XCTUnwrap(model.block)
        XCTAssertEqual(block.fraction, 0.5)
        XCTAssertEqual(block.elapsedText, "00:00 elapsed")
        XCTAssertEqual(block.statusLine, "Downloading Parakeet…")
        XCTAssertEqual(block.counterText, "3 / 6 files")
        XCTAssertFalse(block.failed)
        XCTAssertEqual(model.statusLine, "Downloading Parakeet…")

        // The elapsed time runs while the preparation does.
        await rig.owners.clock.advance(by: 72)
        XCTAssertEqual(model.block?.elapsedText, "01:12 elapsed")

        await gate.open()
        await preparing.value

        // Ready: the block is gone and the button reads "Reload".
        XCTAssertNil(model.block)
        XCTAssertFalse(model.isPreparing)
        XCTAssertTrue(model.isLoaded)
        XCTAssertEqual(model.mainButtonTitle, "Reload")
        XCTAssertEqual(model.statusLine, "Ready")
        XCTAssertNil(rig.owners.app.alertMessage)
    }

    @MainActor
    func testTheElapsedRefreshRunsOnlyWhileTheScreenIsVisible() async {
        let gate = FakeGate()
        let rig = Rig(provider: .voz, gate: gate)
        let model = rig.model
        let preparing = Task { await model.prepare() }
        await SpeechTest.waitUntil { model.isPreparing }

        model.appeared()
        model.appeared()
        await rig.owners.clock.waitForSleepers(count: 1)
        XCTAssertEqual(rig.owners.clock.sleeperCount, 1, "One refresh, however often the screen appears.")

        // Each second asks the views to read the elapsed time again.
        await rig.owners.clock.advance(by: 1)
        await rig.owners.clock.waitForSleepers(count: 1)
        XCTAssertEqual(model.block?.elapsedText, "00:01 elapsed")

        model.disappeared()
        await SpeechTest.eventually { rig.owners.clock.sleeperCount == 0 }

        await gate.open()
        await preparing.value
    }

    // MARK: - Failure (AC-IOS-148)

    @MainActor
    func testAFailedPreparationKeepsTheBlockAndAlerts() async throws {
        let rig = Rig(provider: .parakeet, steps: [halfway], failure: LoaderFailure())
        let model = rig.model

        await model.prepare()

        XCTAssertEqual(rig.owners.app.alertMessage, "The model files could not be downloaded.")
        XCTAssertEqual(model.statusLine, "Preparation failed")
        XCTAssertFalse(model.isPreparing, "The button is enabled to try again.")
        XCTAssertFalse(model.isLoaded)
        XCTAssertEqual(model.mainButtonTitle, "Download & use")
        let block = try XCTUnwrap(model.block)
        XCTAssertTrue(block.failed)
        XCTAssertEqual(block.statusLine, "Preparation failed")

        // The time of a preparation that ended stands still.
        let frozen = block.elapsedText
        await rig.owners.clock.advance(by: 30)
        XCTAssertEqual(model.block?.elapsedText, frozen)
    }

    // MARK: - The block belongs to its model (IOS-B13)

    @MainActor
    func testTheBlockOfOneModelIsNotShownForAnother() async throws {
        let rig = Rig(provider: .parakeet, steps: [halfway], failure: LoaderFailure())
        let model = rig.model
        await model.prepare()
        XCTAssertNotNil(model.block)
        XCTAssertEqual(rig.models.preparation?.variant, "parakeet")

        // The other on-device provider: Parakeet's last run is not Voz's.
        rig.owners.speech.select(.voz)
        XCTAssertEqual(model.model, TranscriptionModel.voz)
        XCTAssertNotNil(rig.models.preparation, "The manager still holds Parakeet's failed run.")
        XCTAssertNil(model.block)

        rig.owners.speech.select(.parakeet)
        XCTAssertNotNil(model.block)
    }

    // MARK: - Unload (AC-IOS-147)

    @MainActor
    func testUnloadReleasesTheModel() async {
        let rig = Rig(provider: .voz)
        let model = rig.model
        await model.prepare()
        XCTAssertTrue(model.isLoaded)
        XCTAssertEqual(model.mainButtonTitle, "Reload")

        model.unload()

        XCTAssertFalse(model.isLoaded)
        XCTAssertEqual(model.mainButtonTitle, "Download & use")
        XCTAssertEqual(model.statusLine, "Prepare a model")
        XCTAssertNil(model.block)
    }
}
