import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// PC model button rules (AC-IOS-099 to 105).
final class PCSpeechModelViewModelTests: XCTestCase {
    private func status(
        _ state: String,
        model: String? = nil,
        busy: Bool = false,
        elapsed: Int = 0,
        message: String = "A message from the PC."
    ) -> PCModelStatus {
        PCModelStatus(state: state, model: model, busy: busy, elapsedSeconds: elapsed, message: message)
    }

    @MainActor
    private func makeModel(_ owners: TestOwners) -> PCSpeechModelViewModel {
        PCSpeechModelViewModel(speech: owners.speech, relay: owners.relay)
    }

    /// The PC answers a status check with `status`, and the screen has asked once.
    @MainActor
    private func show(_ status: PCModelStatus, on owners: TestOwners, _ model: PCSpeechModelViewModel) async {
        await owners.gateway.setStatus(.success(status))
        await model.refresh()
    }

    // MARK: - Unknown

    @MainActor
    func testBeforeTheFirstStatusTheStateIsUnavailable() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)

        XCTAssertEqual(model.title, "Nemotron 3.5 · PC")
        XCTAssertEqual(model.stateText, "Unavailable")
        XCTAssertEqual(model.message, "Checking PC model…")
        XCTAssertFalse(model.isChanging)
        XCTAssertFalse(model.isError)
        XCTAssertFalse(model.isInUse)
        XCTAssertNil(model.elapsedText)
        XCTAssertNil(model.otherLoadedModel)
        // Start may be tried; there is nothing known to stop.
        XCTAssertTrue(model.canStart)
        XCTAssertFalse(model.canStop)
    }

    @MainActor
    func testWithoutAPairingBothButtonsAreDisabled() async {
        let owners = TestOwners(paired: false, provider: .qwen3)
        let model = makeModel(owners)

        await model.refresh()

        XCTAssertEqual(model.title, "Qwen3 1.7B · PC")
        XCTAssertEqual(model.stateText, "Unavailable")
        XCTAssertEqual(model.message, "Pair your PC first.")
        XCTAssertFalse(model.canStart)
        XCTAssertFalse(model.canStop)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [], "Nothing is sent without a pairing.")
    }

    @MainActor
    func testAFailedCheckLeavesTheStateUnavailableWithItsMessage() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        await show(status("ready", model: "nemotron"), on: owners, model)

        await owners.gateway.setStatus(.failure(RelayFailure.local(.unreachable)))
        await model.refresh()

        XCTAssertEqual(model.stateText, "Unavailable")
        XCTAssertTrue(model.message.hasPrefix("Could not check the PC model. Keep the updated LiveCue Desktop running. "))
        XCTAssertFalse(model.canStop)
    }

    // MARK: - Start and stop (AC-IOS-099, 100, 101)

    @MainActor
    func testStartingAndStoppingForBothPCModels() async {
        for provider in [SpeechProvider.nemotron, .qwen3] {
            let owners = TestOwners(provider: provider)
            let model = makeModel(owners)
            let id = provider.rawValue
            await show(status("stopped", message: "Stopped on the PC."), on: owners, model)

            XCTAssertEqual(model.stateText, "Stopped", id)
            XCTAssertEqual(model.message, "Stopped on the PC.", id)
            XCTAssertTrue(model.canStart, id)
            XCTAssertFalse(model.canStop, id)

            // "Start model": the PC's reply replaces the status at once.
            await owners.gateway.setStart(.success(status("starting", model: id, elapsed: 0, message: "Loading.")))
            await model.start()
            XCTAssertEqual(model.stateText, "Starting", id)
            XCTAssertEqual(model.message, "Loading.", id)
            XCTAssertTrue(model.isChanging, id)
            XCTAssertFalse(model.canStart, id)
            XCTAssertFalse(model.canStop, id)

            // Loading succeeded.
            await show(status("ready", model: id, message: "Loaded."), on: owners, model)
            XCTAssertEqual(model.stateText, "Ready", id)
            XCTAssertEqual(model.message, "Loaded.", id)
            XCTAssertFalse(model.isChanging, id)
            XCTAssertNil(model.elapsedText, id)
            XCTAssertFalse(model.canStart, "Already ready for \(id).")
            XCTAssertTrue(model.canStop, id)

            // "Stop model".
            await owners.gateway.setStop(.success(status("stopping", model: id, elapsed: 0, message: "Unloading.")))
            await model.stop()
            XCTAssertEqual(model.stateText, "Stopping", id)
            XCTAssertTrue(model.isChanging, id)
            XCTAssertFalse(model.canStart, id)
            XCTAssertFalse(model.canStop, id)

            await show(status("stopped", message: "Stopped on the PC."), on: owners, model)
            XCTAssertEqual(model.stateText, "Stopped", id)
            XCTAssertTrue(model.canStart, id)
            XCTAssertFalse(model.canStop, id)

            let requests = await owners.gateway.modelRequests
            XCTAssertEqual(requests, [provider, provider], "Start and stop name the selected model.")
        }
    }

    // MARK: - Error (AC-IOS-102)

    @MainActor
    func testAFailedLoadShowsErrorWithThePCMessageAndAllowsARetry() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)

        await show(status("error", model: "nemotron", message: "Could not load the PC model."), on: owners, model)

        XCTAssertEqual(model.stateText, "Error")
        XCTAssertTrue(model.isError)
        XCTAssertEqual(model.message, "Could not load the PC model.")
        XCTAssertFalse(model.isChanging)
        XCTAssertTrue(model.canStart, "Start is enabled to retry.")
    }

    @MainActor
    func testARefusedCommandChangesOnlyTheMessage() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        await show(status("stopped"), on: owners, model)

        await owners.gateway.setStart(.failure(RelayFailure.wire(.labNotConfigured)))
        await model.start()

        XCTAssertEqual(model.stateText, "Stopped")
        XCTAssertEqual(model.message, RelayMessages.text(for: RelayFailure.wire(.labNotConfigured)))
        XCTAssertFalse(model.isChanging)
        XCTAssertTrue(model.canStart)
    }

    // MARK: - Changing (AC-IOS-103)

    @MainActor
    func testWhileStartingOrStoppingASpinnerAndTheSecondsShowAndBothButtonsAreDisabled() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)

        for state in ["starting", "stopping"] {
            await show(status(state, model: "nemotron", elapsed: 12), on: owners, model)

            XCTAssertTrue(model.isChanging, state)
            XCTAssertEqual(model.elapsedText, "12 s", state)
            XCTAssertFalse(model.isError, state)
            XCTAssertFalse(model.canStart, state)
            XCTAssertFalse(model.canStop, state)
        }
    }

    @MainActor
    func testWhileACommandIsOnItsWayTheButtonsAreDisabled() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        await show(status("stopped"), on: owners, model)
        await owners.gateway.setHold(true, for: "startLocalModel")

        let starting = Task { await model.start() }
        await SpeechTest.waitUntil { owners.relay.isPCModelCommandInFlight }
        XCTAssertTrue(model.isChanging)
        XCTAssertNil(model.elapsedText, "The PC has not reported a start yet.")
        XCTAssertFalse(model.canStart)
        XCTAssertFalse(model.canStop)

        await owners.gateway.waitForCalls(count: 2)
        await owners.gateway.release("startLocalModel")
        await starting.value
        XCTAssertEqual(model.stateText, "Starting")
    }

    // MARK: - Locked (AC-IOS-104)

    @MainActor
    func testDuringASessionBothButtonsAreDisabled() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        await show(status("ready", model: "nemotron"), on: owners, model)
        XCTAssertTrue(model.canStop)

        owners.activity.isActive = true
        XCTAssertFalse(model.canStart)
        XCTAssertFalse(model.canStop)

        owners.activity.isActive = false
        XCTAssertTrue(model.canStop)
    }

    @MainActor
    func testAModelInUseShowsTheChipAndDisablesBothButtons() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)

        await show(status("ready", model: "nemotron", busy: true), on: owners, model)

        XCTAssertEqual(model.stateText, "Ready")
        XCTAssertTrue(model.isInUse)
        XCTAssertFalse(model.canStart)
        XCTAssertFalse(model.canStop)

        await show(status("ready", model: "nemotron"), on: owners, model)
        XCTAssertFalse(model.isInUse)
        XCTAssertTrue(model.canStop)
    }

    // MARK: - Another model (AC-IOS-105)

    @MainActor
    func testAnotherLoadedModelIsNamedAndCannotBeStoppedFromHere() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)

        await show(status("ready", model: "qwen3"), on: owners, model)

        XCTAssertEqual(model.otherLoadedModel, "Currently loaded: qwen3")
        XCTAssertEqual(model.stateText, "Ready")
        XCTAssertTrue(model.canStart, "Start switches to the selected model.")
        XCTAssertFalse(model.canStop)

        await show(status("ready", model: "nemotron"), on: owners, model)
        XCTAssertNil(model.otherLoadedModel)

        // A stopped PC names no loaded model, whatever it sends.
        await show(status("stopped", model: "qwen3"), on: owners, model)
        XCTAssertNil(model.otherLoadedModel)
    }

    // MARK: - Observation

    @MainActor
    func testTheStatusIsPolledOnlyWhileTheScreenIsVisible() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        owners.relay.start()
        await owners.clock.waitForSleepers(count: 1)

        model.appeared()
        model.appeared()
        await owners.clock.waitForSleepers(count: 2)

        model.disappeared()
        await SpeechTest.eventually { owners.clock.sleeperCount == 1 }
        XCTAssertEqual(owners.clock.sleeperCount, 1)
    }
}
