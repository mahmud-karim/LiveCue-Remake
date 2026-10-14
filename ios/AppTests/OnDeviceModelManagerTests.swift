import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The steps of section 4.6: preparation with its per-model progress block, Reload, one recogniser in
/// memory, failure, unloading, and what a change of provider does (IOS-B13, AC-IOS-145 to 149).
final class OnDeviceModelManagerTests: XCTestCase {
    // MARK: - At launch

    @MainActor
    func testNothingIsLoadedAtLaunch() async {
        let rig = ModelRig()

        XCTAssertNil(rig.models.loadedVariant)
        XCTAssertNil(rig.models.preparation)
        XCTAssertFalse(rig.models.isPreparing)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")
        XCTAssertNil(rig.models.vozEngine)
        XCTAssertNil(rig.models.parakeetEngine)
        XCTAssertNil(rig.models.fluidEngine)
        for provider in SpeechProvider.allCases {
            XCTAssertFalse(rig.models.isLoaded(for: provider))
            XCTAssertNil(rig.models.loadedModel(for: provider))
        }
    }

    // MARK: - Preparation

    @MainActor
    func testPreparingAModelShowsItsBlockAtOnce() async throws {
        let gate = FakeGate()
        let rig = ModelRig(gate: gate)

        let preparing = Task { try await rig.models.prepare(.voz) }
        await SpeechTest.waitUntil {rig.models.isPreparing }

        let block = try XCTUnwrap(rig.models.preparation)
        XCTAssertEqual(block.variant, "voz")
        XCTAssertEqual(block.fraction, 0)
        XCTAssertEqual(block.startedAt, rig.clock.now())
        XCTAssertNil(block.finishedAt)
        XCTAssertNil(block.counterText)
        XCTAssertFalse(block.failed)
        XCTAssertEqual(rig.models.statusLine, "Downloading and preparing Voz on Assist…")
        XCTAssertNil(rig.models.loadedVariant)

        await gate.open()
        try await preparing.value

        // Ready: the engine is kept, and the block is gone (IOS-B13).
        XCTAssertEqual(rig.models.loadedVariant, "voz")
        XCTAssertTrue(rig.models.isLoaded(for: .voz))
        XCTAssertFalse(rig.models.isLoaded(for: .parakeet))
        XCTAssertEqual(rig.models.loadedModel(for: .voz), TranscriptionModel.voz)
        XCTAssertNil(rig.models.loadedModel(for: .parakeet))
        XCTAssertNotNil(rig.models.vozEngine)
        XCTAssertNil(rig.models.parakeetEngine)
        XCTAssertEqual(rig.models.statusLine, "Ready")
        XCTAssertNil(rig.models.preparation)
        XCTAssertFalse(rig.models.isPreparing)
    }

    @MainActor
    func testVozProgressShowsBytesAndTheNeuralEnginePhase() async throws {
        let downloading = ModelPreparationProgress(
            fraction: 0.25,
            phase: .downloading,
            counter: .bytes(completed: 116_750_000, total: 467_000_000)
        )
        let complete = ModelPreparationProgress(
            fraction: 1,
            phase: .preparing,
            counter: .bytes(completed: 467_000_000, total: 467_000_000)
        )
        let whole = ByteCountFormatter.string(fromByteCount: 467_000_000, countStyle: .file)
        let quarter = ByteCountFormatter.string(fromByteCount: 116_750_000, countStyle: .file)

        // While downloading.
        let early = try await shown(preparing: .voz, after: [downloading])
        XCTAssertEqual(early.status, "Downloading Voz…")
        XCTAssertEqual(early.block?.variant, "voz")
        XCTAssertEqual(early.block?.counterText, "\(quarter) / \(whole)")
        XCTAssertEqual(early.block?.failed, false)
        XCTAssertNil(early.block?.finishedAt)

        // The download is complete and the model is being prepared: the bar sits at 100%.
        let late = try await shown(preparing: .voz, after: [downloading, complete])
        XCTAssertEqual(late.status, "Download complete · preparing Neural Engine…")
        XCTAssertEqual(late.block?.fraction, 1)
        XCTAssertEqual(late.block?.counterText, "\(whole) / \(whole)")
        XCTAssertNil(late.block?.finishedAt, "Preparation goes on after the download.")
        XCTAssertEqual(late.rig.models.statusLine, "Ready")
        XCTAssertTrue(late.rig.models.isLoaded(for: .voz))
    }

    @MainActor
    func testParakeetProgressShowsItsThreePhasesAndAFileCount() async throws {
        let checking = ModelPreparationProgress(fraction: 0.05, phase: .checking, counter: .none)
        let downloading = ModelPreparationProgress(fraction: 0.4, phase: .downloading, counter: .files(completed: 3, total: 7))
        let compiling = ModelPreparationProgress(fraction: 0.9, phase: .preparing, counter: .none)

        let first = try await shown(preparing: .parakeet, after: [checking])
        XCTAssertEqual(first.block?.variant, "parakeet")
        XCTAssertEqual(first.status, "Checking model files…")
        XCTAssertNil(first.block?.counterText)

        let second = try await shown(preparing: .parakeet, after: [checking, downloading])
        XCTAssertEqual(second.status, "Downloading Parakeet…")
        XCTAssertEqual(second.block?.counterText, "3 / 7 files")

        let third = try await shown(preparing: .parakeet, after: [checking, downloading, compiling])
        XCTAssertEqual(third.status, "Download complete · preparing model…")
        XCTAssertEqual(third.block?.fraction, 0.9)
        XCTAssertEqual(third.block?.counterText, "3 / 7 files", "The last count stays.")

        let models = third.rig.models
        XCTAssertEqual(models.loadedVariant, "parakeet")
        XCTAssertTrue(models.isLoaded(for: .parakeet))
        XCTAssertEqual(models.loadedModel(for: .parakeet), TranscriptionModel.parakeet)
        XCTAssertNotNil(models.parakeetEngine)
        XCTAssertNil(models.vozEngine)
        XCTAssertNil(models.preparation)
    }

    /// D-29: Fluid Speech Mini reports the three phases, with the bytes of its own download.
    @MainActor
    func testFluidProgressShowsItsThreePhasesAndAByteCount() async throws {
        let total: Int64 = 252_710_832
        let checking = ModelPreparationProgress(fraction: 0.02, phase: .checking, counter: .none)
        let downloading = ModelPreparationProgress(
            fraction: 0.5,
            phase: .downloading,
            counter: .bytes(completed: total / 2, total: total)
        )
        let preparing = ModelPreparationProgress(fraction: 1, phase: .preparing, counter: .bytes(completed: total, total: total))
        let whole = ByteCountFormatter.string(fromByteCount: total, countStyle: .file)
        let half = ByteCountFormatter.string(fromByteCount: total / 2, countStyle: .file)

        let first = try await shown(preparing: .fluid, after: [checking])
        XCTAssertEqual(first.block?.variant, "fluid-mini")
        XCTAssertEqual(first.status, "Checking model files…")
        XCTAssertNil(first.block?.counterText)

        let second = try await shown(preparing: .fluid, after: [checking, downloading])
        XCTAssertEqual(second.status, "Downloading Fluid Speech Mini…")
        XCTAssertEqual(second.block?.counterText, "\(half) / \(whole)")

        let third = try await shown(preparing: .fluid, after: [checking, downloading, preparing])
        XCTAssertEqual(third.status, "Download complete · preparing model…")
        XCTAssertEqual(third.block?.fraction, 1)
        XCTAssertEqual(third.block?.counterText, "\(whole) / \(whole)")
        XCTAssertNil(third.block?.finishedAt, "The first load goes on after the download.")

        let models = third.rig.models
        XCTAssertEqual(models.loadedVariant, "fluid-mini")
        XCTAssertTrue(models.isLoaded(for: .fluid))
        XCTAssertEqual(models.loadedModel(for: .fluid), TranscriptionModel.fluid)
        XCTAssertNotNil(models.fluidEngine)
        XCTAssertNil(models.parakeetEngine, "The other streaming recogniser stays empty.")
        XCTAssertNil(models.vozEngine)
        XCTAssertFalse(models.isLoaded(for: .parakeet))
        XCTAssertEqual(models.statusLine, "Ready")
        XCTAssertNil(models.preparation)
    }

    @MainActor
    func testPreparingFluidSaysSoAtOnce() async throws {
        let gate = FakeGate()
        let rig = ModelRig(gate: gate)

        let preparing = Task { try await rig.models.prepare(.fluid) }
        await SpeechTest.waitUntil { rig.models.isPreparing }
        XCTAssertEqual(rig.models.preparation?.variant, "fluid-mini")
        XCTAssertEqual(rig.models.statusLine, "Downloading and preparing Live Fluid Speech Mini…")
        XCTAssertFalse(rig.models.isLoaded(for: .fluid))

        await gate.open()
        try await preparing.value
        XCTAssertTrue(rig.models.isLoaded(for: .fluid))
    }

    @MainActor
    func testAFailedPreparationKeepsItsBlockAndRethrows() async throws {
        let gate = FakeGate()
        let rig = ModelRig(failure: FakeEngineFailure(), gate: gate)

        let preparing = Task { () -> (any Error)? in
            do {
                try await rig.models.prepare(.parakeet)
                return nil
            } catch {
                return error
            }
        }
        await SpeechTest.waitUntil {rig.models.isPreparing }
        let startedAt = rig.clock.now()
        await rig.clock.advance(by: 3)
        await gate.open()
        let thrown = await preparing.value

        XCTAssertTrue(thrown is FakeEngineFailure, "The loader's own error reaches the alert.")
        XCTAssertEqual(rig.models.statusLine, "Preparation failed")
        let block = try XCTUnwrap(rig.models.preparation)
        XCTAssertEqual(block.variant, "parakeet")
        XCTAssertTrue(block.failed)
        // The elapsed time freezes when the preparation ends.
        XCTAssertEqual(block.startedAt, startedAt)
        XCTAssertEqual(block.finishedAt, startedAt.addingTimeInterval(3))
        XCTAssertFalse(rig.models.isPreparing)
        XCTAssertNil(rig.models.loadedVariant)
        XCTAssertNil(rig.models.parakeetEngine)

        // "Download & use" again: a new block replaces the failed one.
        try? await rig.models.prepare(.parakeet)
        XCTAssertEqual(rig.models.statusLine, "Preparation failed")
        XCTAssertEqual(rig.models.preparation?.startedAt, startedAt.addingTimeInterval(3))
        XCTAssertEqual(rig.models.preparation?.failed, true)
    }

    @MainActor
    func testReloadReleasesTheModelAndPreparesItAgain() async throws {
        let gate = FakeGate()
        let rig = ModelRig(gate: gate)
        rig.models.installPreloaded(variant: "parakeet", voz: nil, parakeet: rig.parakeet)
        XCTAssertTrue(rig.models.isLoaded(for: .parakeet))

        // The button reads "Reload".
        let reloading = Task { try await rig.models.prepare(.parakeet) }
        await SpeechTest.waitUntil {rig.models.isPreparing }
        XCTAssertNil(rig.models.loadedVariant, "The model is released first.")
        XCTAssertFalse(rig.models.isLoaded(for: .parakeet))
        XCTAssertNil(rig.models.parakeetEngine)
        XCTAssertEqual(rig.models.preparation?.variant, "parakeet")

        await gate.open()
        try await reloading.value
        let cleanups = await rig.parakeet.cleanupCount
        XCTAssertEqual(cleanups, 1, "The recogniser that was loaded was cleaned up.")
        XCTAssertTrue(rig.models.isLoaded(for: .parakeet))
        XCTAssertEqual(rig.models.statusLine, "Ready")
        XCTAssertNil(rig.models.preparation)
    }

    @MainActor
    func testPreparingOneModelReleasesTheOther() async throws {
        let rig = ModelRig()
        rig.models.installPreloaded(variant: "parakeet", voz: nil, parakeet: rig.parakeet)

        try await rig.models.prepare(.voz)

        XCTAssertTrue(rig.models.isLoaded(for: .voz))
        XCTAssertFalse(rig.models.isLoaded(for: .parakeet))
        XCTAssertNil(rig.models.parakeetEngine, "One recogniser in memory at a time.")
        let cleanups = await rig.parakeet.cleanupCount
        XCTAssertEqual(cleanups, 1)

        try await rig.models.prepare(.parakeet)
        XCTAssertTrue(rig.models.isLoaded(for: .parakeet))
        XCTAssertFalse(rig.models.isLoaded(for: .voz))
        XCTAssertNil(rig.models.vozEngine)

        // Fluid Speech Mini takes the place of Parakeet, and Parakeet takes it back (D-29).
        try await rig.models.prepare(.fluid)
        XCTAssertTrue(rig.models.isLoaded(for: .fluid))
        XCTAssertFalse(rig.models.isLoaded(for: .parakeet))
        XCTAssertNil(rig.models.parakeetEngine, "One recogniser in memory at a time.")
        let parakeetCleanups = await rig.parakeet.cleanupCount
        XCTAssertEqual(parakeetCleanups, 2)

        try await rig.models.prepare(.parakeet)
        XCTAssertTrue(rig.models.isLoaded(for: .parakeet))
        XCTAssertFalse(rig.models.isLoaded(for: .fluid))
        XCTAssertNil(rig.models.fluidEngine)
        let fluidCleanups = await rig.fluid.cleanupCount
        XCTAssertEqual(fluidCleanups, 1)
    }

    /// D-29: "Reload", "Unload" and a change of provider release the rolling recogniser, and with it the
    /// model.
    @MainActor
    func testFluidIsReleasedOnReloadUnloadAndAChangeOfProvider() async throws {
        let rig = ModelRig()
        try await rig.models.prepare(.fluid)

        try await rig.models.prepare(.fluid)
        XCTAssertTrue(rig.models.isLoaded(for: .fluid))
        let afterReload = await rig.fluid.cleanupCount
        XCTAssertEqual(afterReload, 1, "The recogniser that was loaded was cleaned up before the reload.")

        rig.models.unload()
        XCTAssertFalse(rig.models.isLoaded(for: .fluid))
        XCTAssertNil(rig.models.fluidEngine)
        XCTAssertNil(rig.models.loadedVariant)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")
        await SpeechTest.eventually { await rig.fluid.cleanupCount == 2 }

        try await rig.models.prepare(.fluid)
        rig.models.providerChanged(to: .parakeet)
        XCTAssertFalse(rig.models.isLoaded(for: .fluid))
        XCTAssertNil(rig.models.fluidEngine)
        await SpeechTest.eventually { await rig.fluid.cleanupCount == 3 }
    }

    @MainActor
    func testNothingIsPreparedDuringASession() async throws {
        let rig = ModelRig()
        rig.activity.isActive = true

        try await rig.models.prepare(.voz)

        XCTAssertNil(rig.models.loadedVariant)
        XCTAssertNil(rig.models.preparation)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")
        XCTAssertNil(rig.models.vozEngine)
    }

    @MainActor
    func testASecondPreparationWhileOneRunsDoesNothing() async throws {
        let gate = FakeGate()
        let rig = ModelRig(gate: gate)

        let first = Task { try await rig.models.prepare(.voz) }
        await SpeechTest.waitUntil {rig.models.isPreparing }
        try await rig.models.prepare(.parakeet)
        XCTAssertEqual(rig.models.preparation?.variant, "voz", "The running preparation keeps its block.")
        XCTAssertTrue(rig.models.isPreparing)

        await gate.open()
        try await first.value
        XCTAssertEqual(rig.models.loadedVariant, "voz")
        XCTAssertNil(rig.models.parakeetEngine)
    }

    @MainActor
    func testAModelThatIsNotOnDeviceIsNotPrepared() async throws {
        let rig = ModelRig()
        let unknown = TranscriptionModel(variant: "nemotron", displayName: "Not on device", approximateMegabytes: 1, quality: "", recommended: false)

        try await rig.models.prepare(unknown)
        XCTAssertNil(rig.models.preparation)
        XCTAssertNil(rig.models.loadedVariant)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")
    }

    @MainActor
    func testAReportOfAnEarlierPreparationChangesNothing() async throws {
        let loader = ReportingLoader()
        let clock = ManualClock()
        let models = OnDeviceModelManager(loader: loader, activity: SessionActivity(), clock: clock)

        // The first preparation ends, and its reporter is kept by the package.
        try await models.prepare(.voz)
        XCTAssertEqual(models.statusLine, "Ready")
        await loader.report(ModelPreparationProgress(fraction: 0.3, phase: .downloading, counter: .none), toCall: 0)
        await SpeechTest.settle()
        XCTAssertNil(models.preparation, "A report after the end opens no block.")
        XCTAssertEqual(models.statusLine, "Ready")

        // A second preparation runs. A late report of the first does not touch its block.
        let gate = FakeGate()
        await loader.setGate(gate)
        let second = Task { try await models.prepare(.parakeet) }
        await SpeechTest.waitUntil {models.isPreparing }
        await loader.waitForCalls(2)
        await loader.report(ModelPreparationProgress(fraction: 0.8, phase: .preparing, counter: .none), toCall: 0)
        await SpeechTest.settle()
        XCTAssertEqual(models.preparation?.fraction, 0)
        XCTAssertEqual(models.statusLine, "Downloading and preparing Live Parakeet…")

        // Its own report does.
        await loader.report(ModelPreparationProgress(fraction: 0.6, phase: .downloading, counter: .files(completed: 4, total: 7)), toCall: 1)
        await SpeechTest.waitUntil {models.preparation?.fraction == 0.6 }
        XCTAssertEqual(models.preparation?.counterText, "4 / 7 files")
        XCTAssertEqual(models.statusLine, "Downloading Parakeet…")

        await gate.open()
        try await second.value
        XCTAssertEqual(models.loadedVariant, "parakeet")
    }

    // MARK: - Unloading

    @MainActor
    func testUnloadReleasesTheModelAndDeletesNothing() async {
        let rig = ModelRig()
        rig.models.installPreloaded(variant: "parakeet", voz: nil, parakeet: rig.parakeet)
        XCTAssertEqual(rig.models.statusLine, "Ready")

        rig.models.unload()

        XCTAssertNil(rig.models.loadedVariant)
        XCTAssertFalse(rig.models.isLoaded(for: .parakeet))
        XCTAssertNil(rig.models.parakeetEngine)
        XCTAssertNil(rig.models.vozEngine)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")
        // The recogniser is cleaned up; that happens on its own actor.
        await SpeechTest.eventually { await rig.parakeet.cleanupCount == 1 }
    }

    @MainActor
    func testUnloadDoesNothingDuringASession() async {
        let rig = ModelRig()
        rig.models.installPreloaded(variant: "voz", voz: rig.voz, parakeet: nil)
        rig.activity.isActive = true

        rig.models.unload()
        XCTAssertTrue(rig.models.isLoaded(for: .voz), "The transcriber of the session is using the recogniser.")
        XCTAssertEqual(rig.models.statusLine, "Ready")
    }

    @MainActor
    func testChangingTheProviderReleasesTheModel() async {
        let rig = ModelRig()
        rig.models.installPreloaded(variant: "voz", voz: rig.voz, parakeet: nil)
        XCTAssertTrue(rig.models.isLoaded(for: .voz))

        rig.models.providerChanged(to: .meta)

        XCTAssertNil(rig.models.loadedVariant, "The model must be loaded again before Start is offered (AC-IOS-149).")
        XCTAssertFalse(rig.models.isLoaded(for: .voz))
        XCTAssertNil(rig.models.vozEngine)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")
    }

    @MainActor
    func testUnloadingWhileAPreparationRunsAbandonsIt() async throws {
        let gate = FakeGate()
        let rig = ModelRig(gate: gate)

        let preparing = Task { try await rig.models.prepare(.parakeet) }
        await SpeechTest.waitUntil {rig.models.isPreparing }
        rig.models.unload()
        XCTAssertNil(rig.models.preparation)
        XCTAssertFalse(rig.models.isPreparing)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")

        // The loader finishes anyway. What it loaded is released again and nothing shows as loaded.
        await gate.open()
        try await preparing.value
        XCTAssertNil(rig.models.loadedVariant)
        XCTAssertNil(rig.models.parakeetEngine)
        XCTAssertEqual(rig.models.statusLine, "Prepare a model")
        let cleanups = await rig.parakeet.cleanupCount
        XCTAssertEqual(cleanups, 1)
    }

    // MARK: - The status line and the fixture state

    @MainActor
    func testTranscribersMirrorTheirStatusLine() async {
        let rig = ModelRig()
        rig.models.installPreloaded(variant: "parakeet", voz: nil, parakeet: rig.parakeet)

        rig.models.reportStatus("Listening")
        XCTAssertEqual(rig.models.statusLine, "Listening")
        rig.models.reportStatus("Paused")
        XCTAssertEqual(rig.models.statusLine, "Paused")
    }

    @MainActor
    func testAPreloadedModelCountsAsLoaded() async {
        let rig = ModelRig()
        rig.models.installPreloaded(variant: "parakeet", voz: nil, parakeet: FixtureStreamingEngine())

        XCTAssertEqual(rig.models.loadedVariant, "parakeet")
        XCTAssertTrue(rig.models.isLoaded(for: .parakeet))
        XCTAssertFalse(rig.models.isLoaded(for: .voz))
        XCTAssertNotNil(rig.models.parakeetEngine)
        XCTAssertEqual(rig.models.statusLine, "Ready")
        XCTAssertFalse(rig.models.isPreparing)
    }

    /// D-29: what the seam's `-fluid-ready` does.
    @MainActor
    func testAPreloadedFluidModelCountsAsLoaded() async {
        let rig = ModelRig()
        rig.models.installPreloaded(variant: "fluid-mini", voz: nil, parakeet: nil, fluid: FixtureStreamingEngine())

        XCTAssertEqual(rig.models.loadedVariant, "fluid-mini")
        XCTAssertTrue(rig.models.isLoaded(for: .fluid))
        XCTAssertEqual(rig.models.loadedModel(for: .fluid), TranscriptionModel.fluid)
        XCTAssertFalse(rig.models.isLoaded(for: .parakeet))
        XCTAssertFalse(rig.models.isLoaded(for: .voz))
        XCTAssertNotNil(rig.models.fluidEngine)
        XCTAssertNil(rig.models.parakeetEngine)
        XCTAssertEqual(rig.models.statusLine, "Ready")

        // A recogniser under another model's name is not the model that is asked for.
        rig.models.installPreloaded(variant: "parakeet", voz: nil, parakeet: nil, fluid: FixtureStreamingEngine())
        XCTAssertFalse(rig.models.isLoaded(for: .fluid))
        XCTAssertFalse(rig.models.isLoaded(for: .parakeet))
    }

    // MARK: - The text of a failed load

    @MainActor
    func testAPackageErrorKeepsItsOwnWords() async {
        struct Described: Error, CustomStringConvertible {
            var description: String { "invalid model: vocab.json is missing" }
        }
        struct Localized: LocalizedError {
            var errorDescription: String? { "The download stalled." }
        }
        struct Plain: Error {}

        // A package error that only describes itself: its description becomes the alert's text.
        let described = EngineLoadFailure.readable(Described())
        XCTAssertEqual(described.localizedDescription, "invalid model: vocab.json is missing")
        XCTAssertEqual(described as? EngineLoadFailure, EngineLoadFailure(errorDescription: "invalid model: vocab.json is missing"))

        // Errors that already have a text of their own pass unchanged.
        XCTAssertTrue(EngineLoadFailure.readable(Localized()) is Localized)
        XCTAssertEqual(EngineLoadFailure.readable(Localized()).localizedDescription, "The download stalled.")
        let system = NSError(domain: NSURLErrorDomain, code: NSURLErrorNotConnectedToInternet)
        XCTAssertTrue((EngineLoadFailure.readable(system) as NSError) === system)
        XCTAssertTrue(EngineLoadFailure.readable(CancellationError()) is CancellationError)
        XCTAssertTrue(EngineLoadFailure.readable(Plain()) is Plain)
    }

    // MARK: - Helpers

    /// Prepares `model` on a loader that makes the given reports and then holds. Returns what the
    /// manager shows once the last report was applied; the preparation is finished before it returns.
    @MainActor
    private func shown(
        preparing model: TranscriptionModel,
        after steps: [ModelPreparationProgress],
        file: StaticString = #filePath,
        line: UInt = #line
    ) async throws -> (rig: ModelRig, status: String, block: ModelPreparation?) {
        let gate = FakeGate()
        let rig = ModelRig(steps: steps, gate: gate)
        let last = steps.last?.fraction ?? 0
        let preparing = Task { try await rig.models.prepare(model) }
        await SpeechTest.waitUntil({ rig.models.preparation?.fraction == last }, file: file, line: line)

        let status = rig.models.statusLine
        let block = rig.models.preparation
        XCTAssertTrue(rig.models.isPreparing, file: file, line: line)
        await gate.open()
        try await preparing.value
        return (rig, status, block)
    }
}

/// An `OnDeviceModelManager` on the fake loader.
@MainActor private final class ModelRig {
    let clock: ManualClock
    let activity: SessionActivity
    let voz: FakeBatchEngine
    let parakeet: FakeStreamingEngine
    let fluid: FakeStreamingEngine
    let models: OnDeviceModelManager

    init(steps: [ModelPreparationProgress] = [], failure: (any Error)? = nil, gate: FakeGate? = nil) {
        let clock = ManualClock()
        let activity = SessionActivity()
        let voz = FakeBatchEngine()
        let parakeet = FakeStreamingEngine()
        let fluid = FakeStreamingEngine()
        self.clock = clock
        self.activity = activity
        self.voz = voz
        self.parakeet = parakeet
        self.fluid = fluid
        self.models = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: voz, parakeet: parakeet, fluid: fluid, steps: steps, failure: failure, gate: gate),
            activity: activity,
            clock: clock
        )
    }
}

/// A loader that keeps every reporter it was given, so a test can report late, as a package may.
private actor ReportingLoader: OnDeviceEngineLoading {
    private var reporters: [@Sendable (ModelPreparationProgress) -> Void] = []
    private var gate: FakeGate?
    private var callWaiters: [(count: Int, continuation: CheckedContinuation<Void, Never>)] = []

    func setGate(_ gate: FakeGate?) {
        self.gate = gate
    }

    /// Calls the reporter of the load with that index, in call order.
    func report(_ progress: ModelPreparationProgress, toCall index: Int) {
        guard reporters.indices.contains(index) else { return }
        reporters[index](progress)
    }

    /// Returns when at least `count` loads were started.
    func waitForCalls(_ count: Int) async {
        if reporters.count >= count { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            callWaiters.append((count: count, continuation: continuation))
        }
    }

    func loadVoz(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any BatchRecognizing {
        await began(progress)
        return FakeBatchEngine()
    }

    func loadParakeet(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing {
        await began(progress)
        return FakeStreamingEngine()
    }

    func loadFluid(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing {
        await began(progress)
        return FakeStreamingEngine()
    }

    private func began(_ progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async {
        reporters.append(progress)
        let ready = callWaiters.filter { $0.count <= reporters.count }
        callWaiters.removeAll { $0.count <= reporters.count }
        for waiter in ready {
            waiter.continuation.resume()
        }
        if let gate {
            await gate.wait()
        }
    }
}
