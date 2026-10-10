import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Health and PC model polling, commands and home labels.
final class RelayMonitorTests: XCTestCase {
    private let connectedMessage = "Connected. Your pairing is saved on this iPhone."
    private let unauthorizedText = "The PC no longer recognizes this pairing. Scan its pairing QR once to reconnect."
    private let unreachableText = "Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically."
    private let tlsText = "The secure connection to your PC failed. Check the PC tunnel and retry. Scanning the QR again will not repair this connection."
    private let timeoutText = "Your PC did not answer in time. Check that LiveCue Desktop is running, then retry."
    private let checkingPCModel = "Checking PC model\u{2026}"

    /// Waits, for at most five seconds of real time, until `condition` holds. Used only where the monitor
    /// started a check by itself, so that no call of the test returns when the check ends.
    @MainActor
    private func waitUntil(
        _ what: String,
        file: StaticString = #filePath,
        line: UInt = #line,
        _ condition: @MainActor () -> Bool
    ) async {
        for _ in 0..<5_000 {
            if condition() { return }
            try? await Task.sleep(nanoseconds: 1_000_000)
        }
        XCTFail("Timed out waiting until \(what).", file: file, line: line)
    }

    /// Another PC than the fixture pairing's.
    private func otherPairing() throws -> PairingPayload {
        try PairingPayload.manual(address: "https://other.example.test:11443/", token: FixtureValues.pairingToken)
    }

    // MARK: - Nothing is sent when nothing may be sent (P2-220, AC-IOS-143)

    @MainActor
    func testNoRelayRequestWhenThePCIsNotNeeded() async {
        for provider in [SpeechProvider.meta, .voz, .parakeet] {
            let owners = TestOwners(provider: provider, answers: .openrouter)
            let relay = owners.relay
            XCTAssertFalse(relay.needsPC, provider.rawValue)
            XCTAssertTrue(relay.isPaired, provider.rawValue)

            relay.start()
            await owners.clock.waitForSleepers(count: 1)
            relay.appBecameActive()
            relay.selectionChanged()
            relay.beginPCModelObservation()
            await relay.refreshPCModel()
            await relay.startPCModel()
            await relay.stopPCModel()
            for _ in 0..<3 {
                await owners.clock.advance(by: 5)
                await owners.clock.waitForSleepers(count: 1)
            }

            let calls = await owners.gateway.calls
            XCTAssertEqual(calls, [], provider.rawValue)
            XCTAssertEqual(owners.clock.sleeperCount, 1, "Only the health loop waits. The PC model loop does not run.")
            // The stored state is left as it was.
            XCTAssertFalse(relay.isOnline, provider.rawValue)
            XCTAssertEqual(relay.savedPCState, .reconnecting, provider.rawValue)
            XCTAssertNil(relay.connectionMessage, provider.rawValue)
            XCTAssertFalse(relay.isCheckingHealth, provider.rawValue)
            XCTAssertNil(relay.pcModelStatus, provider.rawValue)
            XCTAssertEqual(relay.pcModelMessage, checkingPCModel, provider.rawValue)
        }
    }

    @MainActor
    func testPollingStopsWhenThePCIsNoLongerNeededAndTheStateStays() async {
        let owners = TestOwners()
        let relay = owners.relay
        XCTAssertTrue(relay.needsPC, "Meta speech with Codex answers needs the PC.")

        relay.start()
        await owners.clock.waitForSleepers(count: 1)
        XCTAssertTrue(relay.isOnline)

        owners.assistant.switchProvider(.openrouter)
        XCTAssertFalse(relay.needsPC)
        relay.appBecameActive()
        for _ in 0..<3 {
            await owners.clock.advance(by: 5)
            await owners.clock.waitForSleepers(count: 1)
        }

        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health"], "Only the check at launch was sent.")
        XCTAssertTrue(relay.isOnline, "What the last check found stays.")
        XCTAssertEqual(relay.savedPCState, .connected)
        XCTAssertEqual(relay.connectionMessage, connectedMessage)
    }

    @MainActor
    func testNoRelayRequestWhenNothingIsPaired() async {
        let owners = TestOwners(paired: false, provider: .nemotron)
        let relay = owners.relay
        XCTAssertTrue(relay.needsPC)
        XCTAssertFalse(relay.isPaired)
        XCTAssertNil(relay.pairing)

        relay.start()
        await owners.clock.waitForSleepers(count: 1)
        relay.appBecameActive()
        relay.selectionChanged()
        // The PC model loop runs, and finds nothing to ask.
        relay.beginPCModelObservation()
        await owners.clock.waitForSleepers(count: 2)
        XCTAssertNil(relay.pcModelStatus)
        XCTAssertEqual(relay.pcModelMessage, "Pair your PC first.")

        let online = await relay.checkHealthNow()
        XCTAssertFalse(online)
        await relay.refreshPCModel()
        await relay.startPCModel()
        await relay.stopPCModel()
        await owners.clock.advance(by: 30)

        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
        XCTAssertFalse(relay.isOnline)
        XCTAssertEqual(relay.savedPCState, .reconnecting)
        XCTAssertNil(relay.connectionMessage)
        XCTAssertFalse(relay.isCheckingHealth)
    }

    @MainActor
    func testNothingIsPolledBeforeStart() async {
        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        XCTAssertTrue(relay.isPaired, "The saved pairing is known before start.")
        XCTAssertEqual(relay.pairing, FixtureValues.pairing)

        relay.appBecameActive()
        relay.selectionChanged()
        relay.beginPCModelObservation()

        XCTAssertEqual(owners.clock.sleeperCount, 0)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    // MARK: - Health checks (AC-IOS-142)

    @MainActor
    func testHealthIsCheckedAtStartAndFiveSecondsAfterEachCheckEnds() async {
        let owners = TestOwners()
        let relay = owners.relay
        await owners.gateway.setHold(true, for: "health")

        relay.start()
        await owners.gateway.waitForCalls(count: 1)
        XCTAssertTrue(relay.isCheckingHealth)
        XCTAssertFalse(relay.isOnline)
        XCTAssertNil(relay.connectionMessage, "There is no message until the first check ends.")

        // The check takes thirty seconds. Nothing else is sent meanwhile.
        await owners.clock.advance(by: 30)
        XCTAssertEqual(owners.clock.sleeperCount, 0, "The next check is not scheduled while one runs.")
        var calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health"])

        await owners.gateway.setHold(false, for: "health")
        await owners.gateway.release("health")
        await owners.clock.waitForSleepers(count: 1)
        XCTAssertFalse(relay.isCheckingHealth)
        XCTAssertTrue(relay.isOnline)
        XCTAssertEqual(relay.savedPCState, .connected)
        XCTAssertEqual(relay.connectionMessage, connectedMessage)

        // Five seconds are counted from the end of the check.
        await owners.clock.advance(by: 4.5)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health"])
        XCTAssertEqual(owners.clock.sleeperCount, 1)

        await owners.clock.advance(by: 0.5)
        await owners.gateway.waitForCalls(count: 2)
        await owners.clock.waitForSleepers(count: 1)

        await owners.clock.advance(by: 5)
        await owners.gateway.waitForCalls(count: 3)
        await owners.clock.waitForSleepers(count: 1)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "health", "health"])
        let pairings = await owners.gateway.pairings
        XCTAssertEqual(pairings, [FixtureValues.pairing, FixtureValues.pairing, FixtureValues.pairing])
    }

    @MainActor
    func testStartIsIdempotent() async {
        let owners = TestOwners()
        owners.relay.start()
        owners.relay.start()
        await owners.clock.waitForSleepers(count: 1)
        owners.relay.start()

        await owners.clock.advance(by: 5)
        await owners.gateway.waitForCalls(count: 2)
        await owners.clock.waitForSleepers(count: 1)

        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "health"], "One loop: one check at start and one five seconds later.")
        XCTAssertEqual(owners.clock.sleeperCount, 1)
    }

    @MainActor
    func testHealthIsCheckedWhenTheAppBecomesActive() async {
        let owners = TestOwners()
        let relay = owners.relay
        relay.start()
        await owners.clock.waitForSleepers(count: 1)
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))

        relay.appBecameActive()
        await owners.gateway.waitForCalls(count: 2)
        await waitUntil("the check on activation ended") { relay.savedPCState == .reconnecting && !relay.isCheckingHealth }

        XCTAssertFalse(relay.isOnline)
        XCTAssertEqual(relay.connectionMessage, "Your pairing is saved. " + unreachableText)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "health"])
    }

    @MainActor
    func testChecksNeverOverlap() async {
        let owners = TestOwners()
        let relay = owners.relay
        relay.start()
        await owners.clock.waitForSleepers(count: 1)

        await owners.gateway.setHold(true, for: "health")
        relay.appBecameActive()
        await owners.gateway.waitForCalls(count: 2)
        XCTAssertTrue(relay.isCheckingHealth)

        // While that check is in flight: another activation, "Reconnect", and the loop's own turn.
        relay.appBecameActive()
        let reconnect = Task { await relay.checkHealthNow() }
        await owners.clock.advance(by: 5)
        let duringTheCheck = await owners.gateway.calls
        XCTAssertEqual(duringTheCheck, ["health", "health"], "Each of them joined the check in flight.")

        await owners.gateway.setHold(false, for: "health")
        await owners.gateway.release("health")
        let online = await reconnect.value
        XCTAssertTrue(online)
        await owners.clock.waitForSleepers(count: 1)

        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "health"])
        XCTAssertFalse(relay.isCheckingHealth)
    }

    @MainActor
    func testACheckFollowsWhenThePCHasJustBecomeNeeded() async {
        let owners = TestOwners(provider: .meta, answers: .openrouter)
        let relay = owners.relay
        relay.start()
        await owners.clock.waitForSleepers(count: 1)
        var calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])

        // Codex answers need the PC.
        owners.assistant.switchProvider(.codex)
        XCTAssertTrue(relay.needsPC)
        await owners.gateway.waitForCalls(count: 1)
        await waitUntil("the check ended") { relay.savedPCState == .connected && !relay.isCheckingHealth }
        calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health"])

        // A PC speech provider on top of that changes nothing: the PC was needed already.
        owners.speech.select(.nemotron)
        XCTAssertEqual(owners.speech.provider, .nemotron)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health"])

        // Not needed, then needed again by the speech provider alone.
        owners.speech.select(.parakeet)
        owners.assistant.switchProvider(.openrouter)
        XCTAssertFalse(relay.needsPC)
        owners.speech.select(.qwen3)
        XCTAssertTrue(relay.needsPC)
        await owners.gateway.waitForCalls(count: 3)
        await waitUntil("the PC model was checked") { relay.pcModelStatus != nil }
        calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "health", "localModelStatus"])
    }

    // MARK: - The saved PC follows the most recent check only (IOS-B11, AC-IOS-136, 137)

    @MainActor
    func testTheSavedPCStateFollowsTheMostRecentCheckOnly() async {
        let owners = TestOwners()
        let relay = owners.relay
        XCTAssertEqual(relay.savedPCState, .reconnecting)
        XCTAssertNil(relay.connectionMessage)

        var online = await relay.checkHealthNow()
        XCTAssertTrue(online)
        XCTAssertTrue(relay.isOnline)
        XCTAssertEqual(relay.savedPCState, .connected)
        XCTAssertEqual(relay.connectionMessage, connectedMessage)

        // The relay answers 401: its text, without the prefix.
        await owners.gateway.setHealth(.failure(RelayFailure.wire(.unauthorized)))
        online = await relay.checkHealthNow()
        XCTAssertFalse(online)
        XCTAssertFalse(relay.isOnline)
        XCTAssertEqual(relay.savedPCState, .needsAttention)
        XCTAssertEqual(relay.connectionMessage, unauthorizedText)

        // A plain network failure afterwards: the rejection does not stick.
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        online = await relay.checkHealthNow()
        XCTAssertFalse(online)
        XCTAssertEqual(relay.savedPCState, .reconnecting)
        XCTAssertEqual(relay.connectionMessage, "Your pairing is saved. " + unreachableText)

        await owners.gateway.setHealth(.failure(RelayFailure.local(.tls)))
        await relay.checkHealthNow()
        XCTAssertEqual(relay.savedPCState, .reconnecting)
        XCTAssertEqual(relay.connectionMessage, "Your pairing is saved. " + tlsText)

        // A code other than UNAUTHORIZED: reconnecting, and its own text without the prefix.
        await owners.gateway.setHealth(.failure(RelayFailure.wire(.protocolMismatch)))
        await relay.checkHealthNow()
        XCTAssertEqual(relay.savedPCState, .reconnecting)
        XCTAssertEqual(relay.connectionMessage, "This iPhone app and LiveCue Desktop are different versions. Update both, then retry.")

        await owners.gateway.setHealth(.failure(RelayFailure.wire(.unauthorized)))
        await relay.checkHealthNow()
        XCTAssertEqual(relay.savedPCState, .needsAttention)

        await owners.gateway.setHealth(.success(RelayHealth(relayVersion: "1.0.0")))
        online = await relay.checkHealthNow()
        XCTAssertTrue(online)
        XCTAssertEqual(relay.savedPCState, .connected)
        XCTAssertEqual(relay.connectionMessage, connectedMessage)
        XCTAssertFalse(relay.isCheckingHealth)

        let calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 7, "One request for each check.")
    }

    @MainActor
    func testReconnectChecksAPairedPCEvenWhenItIsNotNeeded() async {
        let owners = TestOwners(provider: .meta, answers: .openrouter)
        XCTAssertFalse(owners.relay.needsPC)

        let online = await owners.relay.checkHealthNow()

        XCTAssertTrue(online)
        XCTAssertEqual(owners.relay.savedPCState, .connected)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health"])
    }

    // MARK: - Pairing

    @MainActor
    func testASavedPairingSetsConnected() async throws {
        let owners = TestOwners(paired: false)
        let relay = owners.relay
        XCTAssertFalse(relay.isPaired)

        relay.pairingSaved(FixtureValues.pairing)

        XCTAssertTrue(relay.isPaired)
        XCTAssertEqual(relay.pairing, FixtureValues.pairing)
        XCTAssertTrue(relay.isOnline)
        XCTAssertEqual(relay.savedPCState, .connected)
        XCTAssertEqual(relay.connectionMessage, connectedMessage)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [], "The pairing was verified by the caller. Nothing more is sent.")
    }

    @MainActor
    func testACheckOfAnOlderPairingIsDropped() async throws {
        let owners = TestOwners()
        let relay = owners.relay
        await owners.gateway.setHealth(.failure(RelayFailure.wire(.unauthorized)))
        await owners.gateway.setHold(true, for: "health")

        let check = Task { await relay.checkHealthNow() }
        await owners.gateway.waitForCalls(count: 1)
        XCTAssertTrue(relay.isCheckingHealth)

        // Another PC is paired while the old one is still being asked.
        let other = try otherPairing()
        relay.pairingSaved(other)
        XCTAssertFalse(relay.isCheckingHealth)

        await owners.gateway.setHold(false, for: "health")
        await owners.gateway.release("health")
        _ = await check.value

        XCTAssertEqual(relay.pairing, other)
        XCTAssertTrue(relay.isOnline, "The answer about the old PC changed nothing.")
        XCTAssertEqual(relay.savedPCState, .connected)
        XCTAssertEqual(relay.connectionMessage, connectedMessage)
        XCTAssertFalse(relay.isCheckingHealth)
    }

    @MainActor
    func testReloadingReadsTheStoreAgain() async throws {
        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        await relay.checkHealthNow()
        XCTAssertTrue(relay.isOnline)
        XCTAssertNotNil(relay.pcModelStatus)

        // The same pairing: nothing changes.
        relay.reloadPairing()
        XCTAssertTrue(relay.isOnline)
        XCTAssertNotNil(relay.pcModelStatus)

        // Another pairing in the store: what was known belonged to the old PC.
        let other = try otherPairing()
        try owners.pairingStore.save(other)
        relay.reloadPairing()
        XCTAssertEqual(relay.pairing, other)
        XCTAssertFalse(relay.isOnline)
        XCTAssertEqual(relay.savedPCState, .reconnecting)
        XCTAssertNil(relay.connectionMessage)
        XCTAssertNil(relay.pcModelStatus)
        XCTAssertEqual(relay.pcModelMessage, checkingPCModel)

        await relay.checkHealthNow()
        let pairings = await owners.gateway.pairings
        XCTAssertEqual(pairings.last, other, "The next request goes to the new PC.")
    }

    // MARK: - PC model status

    @MainActor
    func testThePCModelIsCheckedAfterASuccessfulHealthCheckForAPCSpeechProvider() async {
        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        let stopped = PCModelStatus(state: "stopped", message: "PC model is stopped. Start opens Docker Desktop if needed.")
        await owners.gateway.setStatus(.success(stopped))

        await relay.checkHealthNow()
        var calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "localModelStatus"])
        XCTAssertEqual(relay.pcModelStatus, stopped)
        XCTAssertEqual(relay.pcModelMessage, stopped.message)

        // A failed check is not followed by one.
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await relay.checkHealthNow()
        calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "localModelStatus", "health"])

        // Neither is a check for a provider that is not PC-local.
        let meta = TestOwners(provider: .meta)
        await meta.relay.checkHealthNow()
        let metaCalls = await meta.gateway.calls
        XCTAssertEqual(metaCalls, ["health"])
        XCTAssertNil(meta.relay.pcModelStatus)
    }

    @MainActor
    func testThePCModelIsPolledEveryTwoSecondsOnlyWhileObservedAndPCLocal() async {
        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        let clock = owners.clock

        // Second 0: the check at launch and the status check that follows it. The health loop waits until 5.
        relay.start()
        await clock.waitForSleepers(count: 1)
        var calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["health", "localModelStatus"])

        // No screen shows the PC model: no status poll.
        await clock.advance(by: 4)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 2)

        // Second 4: a screen appears. One check at once, the next at 6.
        relay.beginPCModelObservation()
        await clock.waitForSleepers(count: 2)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 3)
        XCTAssertEqual(calls.last, "localModelStatus")

        // Second 5: the health loop, with the status check that follows it.
        await clock.advance(by: 1)
        await clock.waitForSleepers(count: 2)
        calls = await owners.gateway.calls
        XCTAssertEqual(Array(calls.suffix(2)), ["health", "localModelStatus"])
        XCTAssertEqual(calls.count, 5)

        // Seconds 6 and 8: the status poll.
        await clock.advance(by: 1)
        await clock.waitForSleepers(count: 2)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 6)
        XCTAssertEqual(calls.last, "localModelStatus")

        await clock.advance(by: 2)
        await clock.waitForSleepers(count: 2)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 7)
        XCTAssertEqual(calls.last, "localModelStatus")

        // The screen disappears: the poll stops at once.
        relay.endPCModelObservation()
        XCTAssertEqual(clock.sleeperCount, 1)
        await clock.advance(by: 1.5)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 7)

        // Second 9.5: observed again, then the provider is no longer PC-local.
        relay.beginPCModelObservation()
        await clock.waitForSleepers(count: 2)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 8)
        owners.speech.select(.meta)
        XCTAssertEqual(owners.speech.provider, .meta)
        XCTAssertEqual(clock.sleeperCount, 1, "The PC model loop stops with the provider.")

        // Second 10: Codex still needs the PC, so health goes on. Nothing follows it for Meta.
        await clock.advance(by: 0.5)
        await clock.waitForSleepers(count: 1)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 9)
        XCTAssertEqual(calls.last, "health")

        await clock.advance(by: 4)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls.count, 9)

        // Two screens count as observed until both are gone.
        owners.speech.select(.qwen3)
        relay.beginPCModelObservation()
        await clock.waitForSleepers(count: 2)
        relay.endPCModelObservation()
        XCTAssertEqual(clock.sleeperCount, 2, "One screen still shows the PC model.")
        relay.endPCModelObservation()
        XCTAssertEqual(clock.sleeperCount, 1)
    }

    @MainActor
    func testRefreshingThePCModel() async {
        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        XCTAssertNil(relay.pcModelStatus)
        XCTAssertEqual(relay.pcModelMessage, checkingPCModel)

        // Success: the status is the reply and the message its message.
        let ready = PCModelStatus(state: "ready", model: "nemotron", message: "Model loaded on the PC GPU. Ready for live transcription.")
        await owners.gateway.setStatus(.success(ready))
        await relay.refreshPCModel()
        XCTAssertEqual(relay.pcModelStatus, ready)
        XCTAssertEqual(relay.pcModelMessage, "Model loaded on the PC GPU. Ready for live transcription.")

        // Failure: unknown, with the composed message.
        await owners.gateway.setStatus(.failure(RelayFailure.local(.timeout)))
        await relay.refreshPCModel()
        XCTAssertNil(relay.pcModelStatus)
        XCTAssertEqual(
            relay.pcModelMessage,
            "Could not check the PC model. Keep the updated LiveCue Desktop running. " + timeoutText
        )

        // Another refresh in flight: the second one sends nothing and returns with the first.
        await owners.gateway.setStatus(.success(ready))
        await owners.gateway.setHold(true, for: "localModelStatus")
        let first = Task { await relay.refreshPCModel() }
        await owners.gateway.waitForCalls(count: 3)
        let second = Task { await relay.refreshPCModel() }
        await owners.gateway.setHold(false, for: "localModelStatus")
        await owners.gateway.release("localModelStatus")
        await first.value
        await second.value
        let afterBoth = await owners.gateway.calls
        XCTAssertEqual(afterBoth.count, 3, "Status checks never overlap.")
        XCTAssertEqual(relay.pcModelStatus, ready)

        // A provider that is not PC-local: nothing.
        let meta = TestOwners(provider: .meta)
        await meta.relay.refreshPCModel()
        let metaCalls = await meta.gateway.calls
        XCTAssertEqual(metaCalls, [])
        XCTAssertNil(meta.relay.pcModelStatus)
        XCTAssertEqual(meta.relay.pcModelMessage, checkingPCModel)
    }

    // MARK: - PC model commands

    @MainActor
    func testACommandReplacesTheStatusAndAFailureChangesOnlyTheMessage() async {
        let owners = TestOwners(provider: .qwen3)
        let relay = owners.relay
        let starting = PCModelStatus(
            state: "starting",
            model: "qwen3",
            message: "Starting Docker and loading the PC model. First load can take several minutes."
        )
        await owners.gateway.setStart(.success(starting))

        await relay.startPCModel()
        XCTAssertEqual(relay.pcModelStatus, starting)
        XCTAssertEqual(relay.pcModelMessage, starting.message)
        XCTAssertFalse(relay.isPCModelCommandInFlight)
        var calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["startLocalModel"])
        var requested = await owners.gateway.modelRequests
        XCTAssertEqual(requested, [.qwen3])

        // The poll follows the operation to its end.
        let ready = PCModelStatus(state: "ready", model: "qwen3", message: "Model loaded on the PC GPU. Ready for live transcription.")
        await owners.gateway.setStatus(.success(ready))
        await relay.refreshPCModel()
        XCTAssertEqual(relay.pcModelStatus, ready)

        // A refused stop: the status stays, the message says why.
        await owners.gateway.setStop(.failure(RelayFailure.wire(.modelBusy)))
        await relay.stopPCModel()
        XCTAssertEqual(relay.pcModelStatus, ready)
        XCTAssertEqual(relay.pcModelMessage, "The PC model is in use. Stop the other conversation or lab test, then retry.")
        XCTAssertFalse(relay.isPCModelCommandInFlight)

        // A stop that the PC accepts.
        let stopping = PCModelStatus(state: "stopping", model: "qwen3", message: "Unloading the PC model to free GPU memory\u{2026}")
        await owners.gateway.setStop(.success(stopping))
        await relay.stopPCModel()
        XCTAssertEqual(relay.pcModelStatus, stopping)
        XCTAssertEqual(relay.pcModelMessage, stopping.message)

        calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["startLocalModel", "localModelStatus", "stopLocalModel", "stopLocalModel"])
        requested = await owners.gateway.modelRequests
        XCTAssertEqual(requested, [.qwen3, .qwen3, .qwen3])
    }

    @MainActor
    func testAFailedStartChangesOnlyTheMessage() async {
        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        let stopped = PCModelStatus(state: "stopped", message: "PC model is stopped. Start opens Docker Desktop if needed.")
        await owners.gateway.setStatus(.success(stopped))
        await relay.refreshPCModel()

        await owners.gateway.setStart(.failure(RelayFailure.local(.unreachable)))
        await relay.startPCModel()

        XCTAssertEqual(relay.pcModelStatus, stopped)
        XCTAssertEqual(relay.pcModelMessage, unreachableText, "The text of the failure, with no prefix.")
        XCTAssertFalse(relay.isPCModelCommandInFlight)
    }

    @MainActor
    func testCommandsAreRefusedWhileLocked() async {
        // Nothing is paired.
        let unpaired = TestOwners(paired: false, provider: .nemotron)
        XCTAssertTrue(unpaired.relay.isPCModelLocked)
        await unpaired.relay.startPCModel()
        await unpaired.relay.stopPCModel()
        let unpairedCalls = await unpaired.gateway.calls
        XCTAssertEqual(unpairedCalls, [])

        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        XCTAssertFalse(relay.isPCModelLocked, "Paired, no session, no command, and nothing known about the PC model.")

        // A session is active.
        owners.activity.isActive = true
        XCTAssertTrue(relay.isPCModelLocked)
        await relay.startPCModel()
        await relay.stopPCModel()
        owners.activity.isActive = false
        XCTAssertFalse(relay.isPCModelLocked)

        // The PC reports starting, then stopping.
        for state in ["starting", "stopping"] {
            let changing = PCModelStatus(state: state, model: "nemotron", elapsedSeconds: 3, message: "Changing")
            await owners.gateway.setStatus(.success(changing))
            await relay.refreshPCModel()
            XCTAssertTrue(relay.isPCModelLocked, state)
            await relay.startPCModel()
            await relay.stopPCModel()
        }

        // The PC reports the model in use.
        let busy = PCModelStatus(state: "ready", model: "nemotron", busy: true, message: "Model is in use. Stop the conversation or lab test before unloading.")
        await owners.gateway.setStatus(.success(busy))
        await relay.refreshPCModel()
        XCTAssertTrue(relay.isPCModelLocked)
        await relay.startPCModel()
        await relay.stopPCModel()

        let refused = await owners.gateway.calls
        XCTAssertEqual(refused, ["localModelStatus", "localModelStatus", "localModelStatus"], "No command reached the PC.")

        // Ready and idle: not locked. A command in flight locks.
        let ready = PCModelStatus(state: "ready", model: "nemotron", message: "Ready")
        await owners.gateway.setStatus(.success(ready))
        await relay.refreshPCModel()
        XCTAssertFalse(relay.isPCModelLocked)

        await owners.gateway.setHold(true, for: "stopLocalModel")
        let stop = Task { await relay.stopPCModel() }
        await owners.gateway.waitForCalls(count: 5)
        XCTAssertTrue(relay.isPCModelCommandInFlight)
        XCTAssertTrue(relay.isPCModelLocked)
        await relay.startPCModel()
        await relay.stopPCModel()
        await relay.refreshPCModel()
        let duringTheCommand = await owners.gateway.calls
        XCTAssertEqual(duringTheCommand.count, 5, "No second command and no status check while one is in flight.")

        await owners.gateway.setHold(false, for: "stopLocalModel")
        await owners.gateway.release("stopLocalModel")
        await stop.value
        XCTAssertFalse(relay.isPCModelCommandInFlight)
    }

    @MainActor
    func testACommandEndsAStatusCheckInFlightBeforeItIsSent() async {
        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        let stale = PCModelStatus(state: "stopped", message: "PC model is stopped. Start opens Docker Desktop if needed.")
        let starting = PCModelStatus(state: "starting", model: "nemotron", message: "Starting")
        await owners.gateway.setStatus(.success(stale))
        await owners.gateway.setStart(.success(starting))
        await owners.gateway.setHold(true, for: "localModelStatus")

        let refresh = Task { await relay.refreshPCModel() }
        await owners.gateway.waitForCalls(count: 1)

        await relay.startPCModel()
        await refresh.value

        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["localModelStatus", "startLocalModel"])
        XCTAssertEqual(relay.pcModelStatus, starting, "The status check that was cut short says nothing.")
        XCTAssertEqual(relay.pcModelMessage, "Starting")
    }

    // MARK: - Home label and start (spec 01 section 3.7)

    @MainActor
    func testTheTenHomeLabels() async {
        let unpaired = TestOwners(paired: false, provider: .nemotron)
        XCTAssertEqual(unpaired.relay.homeLabel(for: .nemotron), "Pair PC first")

        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        XCTAssertEqual(relay.homeLabel(for: .nemotron), "PC offline", "Paired, and no check has succeeded yet.")

        // Online, and the PC model is unknown.
        await owners.gateway.setStatus(.failure(RelayFailure.local(.timeout)))
        await relay.checkHealthNow()
        XCTAssertTrue(relay.isOnline)
        XCTAssertNil(relay.pcModelStatus)
        XCTAssertEqual(relay.homeLabel(for: .nemotron), "Checking\u{2026}")

        let rows: [(status: PCModelStatus, label: String)] = [
            (PCModelStatus(state: "ready", model: "qwen3", message: "m"), "Different model loaded"),
            (PCModelStatus(state: "starting", model: "nemotron", elapsedSeconds: 12, message: "m"), "Loading \u{00B7} 12s"),
            (PCModelStatus(state: "stopping", model: "nemotron", elapsedSeconds: 3, message: "m"), "Stopping \u{00B7} 3s"),
            (PCModelStatus(state: "ready", model: "nemotron", busy: true, message: "m"), "In use"),
            (PCModelStatus(state: "ready", model: "nemotron", message: "m"), "Ready"),
            (PCModelStatus(state: "error", model: "nemotron", message: "m"), "Error \u{00B7} open Settings"),
            (PCModelStatus(state: "stopped", message: "m"), "Stopped \u{00B7} open Settings")
        ]
        for row in rows {
            await owners.gateway.setStatus(.success(row.status))
            await relay.refreshPCModel()
            XCTAssertEqual(relay.homeLabel(for: .nemotron), row.label)
        }

        // Offline wins over whatever the PC model last said.
        await owners.gateway.setStatus(.success(PCModelStatus(state: "ready", model: "nemotron", message: "m")))
        await relay.refreshPCModel()
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await relay.checkHealthNow()
        XCTAssertEqual(relay.homeLabel(for: .nemotron), "PC offline")
    }

    @MainActor
    func testASessionCanStartOnlyOnlineReadyIdleAndWithNoCommandInFlight() async {
        let owners = TestOwners(provider: .nemotron)
        let relay = owners.relay
        let ready = PCModelStatus(state: "ready", model: "nemotron", message: "Ready")
        await owners.gateway.setStatus(.success(ready))
        await owners.gateway.setStart(.success(ready))

        // Ready, but the relay is not known to be online.
        await relay.refreshPCModel()
        XCTAssertTrue(relay.isReady(for: .nemotron))
        XCTAssertFalse(relay.canStartSession(for: .nemotron))

        await relay.checkHealthNow()
        XCTAssertTrue(relay.canStartSession(for: .nemotron))
        XCTAssertFalse(relay.isReady(for: .qwen3), "Ready means ready for the selected provider.")
        XCTAssertFalse(relay.canStartSession(for: .qwen3))
        XCTAssertFalse(relay.canStartSession(for: .meta))

        // In use.
        await owners.gateway.setStatus(.success(PCModelStatus(state: "ready", model: "nemotron", busy: true, message: "In use")))
        await relay.refreshPCModel()
        XCTAssertTrue(relay.isReady(for: .nemotron), "In use is still ready.")
        XCTAssertFalse(relay.canStartSession(for: .nemotron))

        // Every state but ready.
        for state in ["stopped", "starting", "stopping", "error"] {
            await owners.gateway.setStatus(.success(PCModelStatus(state: state, model: "nemotron", message: state)))
            await relay.refreshPCModel()
            XCTAssertFalse(relay.isReady(for: .nemotron), state)
            XCTAssertFalse(relay.canStartSession(for: .nemotron), state)
        }

        // A command in flight.
        await owners.gateway.setStatus(.success(ready))
        await relay.refreshPCModel()
        XCTAssertTrue(relay.canStartSession(for: .nemotron))
        let sent = await owners.gateway.calls.count
        await owners.gateway.setHold(true, for: "startLocalModel")
        let start = Task { await relay.startPCModel() }
        await owners.gateway.waitForCalls(count: sent + 1)
        XCTAssertTrue(relay.isPCModelCommandInFlight)
        XCTAssertFalse(relay.canStartSession(for: .nemotron))
        await owners.gateway.setHold(false, for: "startLocalModel")
        await owners.gateway.release("startLocalModel")
        await start.value
        XCTAssertTrue(relay.canStartSession(for: .nemotron))

        // The relay goes offline.
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await relay.checkHealthNow()
        XCTAssertFalse(relay.canStartSession(for: .nemotron))
        XCTAssertTrue(relay.isReady(for: .nemotron), "What the PC model last said is kept.")
    }
}

/// The relay of the test seam answers as the relay of protocol 2 does (architecture 4.9).
final class FixtureRelayGatewayTests: XCTestCase {
    private let stoppedMessage = "PC model is stopped. Start opens Docker Desktop if needed."
    private let readyMessage = "Model loaded on the PC GPU. Ready for live transcription."
    private let startingMessage = "Starting Docker and loading the PC model. First load can take several minutes."
    private let stoppingMessage = "Unloading the PC model to free GPU memory\u{2026}"
    private let startFailedMessage = "Could not load the PC model. Check Docker Desktop, available GPU memory and the installed model weights, then retry."
    private let unreachableText = "Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically."

    private func mode(_ arguments: String...) -> TestMode {
        TestMode(arguments: arguments)
    }

    /// The failure a call threw, or nil when it returned.
    @MainActor
    private func relayFailure<Value: Sendable>(of call: @Sendable () async throws -> Value) async -> RelayFailure? {
        do {
            _ = try await call()
            return nil
        } catch let failure as RelayFailure {
            return failure
        } catch {
            XCTFail("Unexpected error: \(error)")
            return nil
        }
    }

    // MARK: - Health

    @MainActor
    func testHealthSucceedsAndASwitchedOffPCIsUnreachable() async throws {
        let pairing = FixtureValues.pairing
        let online = FixtureRelayGateway(mode: TestMode(), clock: ManualClock())
        let health = try await online.health(pairing)
        XCTAssertEqual(health, RelayHealth(relayVersion: "1.0.0"))
        try await online.verifyPairing(pairing)

        let offline = FixtureRelayGateway(mode: mode("-pc-offline"), clock: ManualClock())
        let healthFailure = await relayFailure { try await offline.health(pairing) }
        XCTAssertEqual(healthFailure, RelayFailure.local(.unreachable))
        let verifyFailure = await relayFailure { try await offline.verifyPairing(pairing) }
        XCTAssertEqual(verifyFailure, RelayFailure.local(.unreachable))
        let statusFailure = await relayFailure { try await offline.localModelStatus(pairing) }
        XCTAssertEqual(statusFailure, RelayFailure.local(.unreachable))
        let startFailure = await relayFailure { try await offline.startLocalModel(.nemotron, pairing: pairing) }
        XCTAssertEqual(startFailure, RelayFailure.local(.unreachable))
        let catalogueFailure = await relayFailure { try await offline.models(pairing) }
        XCTAssertEqual(catalogueFailure, RelayFailure.local(.unreachable))
    }

    /// X-09: the saved-PC message of a launch with `-pc-offline` is the composition of the
    /// `L-UNREACHABLE` text. The old fixture text "PC offline." no longer exists.
    @MainActor
    func testTheMonitorShowsTheUnreachableTextForASwitchedOffPC() async {
        let owners = TestOwners()
        let relay = RelayMonitor(
            gateway: FixtureRelayGateway(mode: mode("-pc-offline"), clock: owners.clock),
            pairingStore: owners.pairingStore,
            clock: owners.clock,
            speech: owners.speech,
            assistant: owners.assistant,
            activity: owners.activity
        )

        let online = await relay.checkHealthNow()

        XCTAssertFalse(online)
        XCTAssertEqual(relay.savedPCState, .reconnecting)
        XCTAssertEqual(relay.connectionMessage, "Your pairing is saved. " + unreachableText)
        XCTAssertEqual(relay.homeLabel(for: .nemotron), "PC offline", "The Home label has no full stop and is unchanged.")
    }

    // MARK: - PC model

    @MainActor
    func testThePCModelStartsStoppedAndStartAndStopTakeOneSecond() async throws {
        let clock = ManualClock()
        let relay = FixtureRelayGateway(mode: TestMode(), clock: clock)
        let pairing = FixtureValues.pairing

        var status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status, PCModelStatus(state: "stopped", model: nil, busy: false, elapsedSeconds: 0, message: stoppedMessage))

        // 202: the status as the operation begins.
        status = try await relay.startLocalModel(.nemotron, pairing: pairing)
        XCTAssertEqual(status, PCModelStatus(state: "starting", model: "nemotron", busy: false, elapsedSeconds: 0, message: startingMessage))

        await clock.advance(by: 0.5)
        status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status.state, "starting")
        XCTAssertEqual(status.elapsedSeconds, 0, "Whole seconds.")

        await clock.advance(by: 0.5)
        status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status, PCModelStatus(state: "ready", model: "nemotron", busy: false, elapsedSeconds: 0, message: readyMessage))
        XCTAssertTrue(status.isReady(for: .nemotron))

        // 200: starting the loaded model again needs nothing.
        status = try await relay.startLocalModel(.nemotron, pairing: pairing)
        XCTAssertEqual(status.state, "ready")

        status = try await relay.stopLocalModel(.nemotron, pairing: pairing)
        XCTAssertEqual(status, PCModelStatus(state: "stopping", model: "nemotron", busy: false, elapsedSeconds: 0, message: stoppingMessage))

        await clock.advance(by: 1)
        status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status, PCModelStatus(state: "stopped", model: nil, busy: false, elapsedSeconds: 0, message: stoppedMessage))
    }

    @MainActor
    func testASlowStartTakesSixSecondsAndCountsThem() async throws {
        let clock = ManualClock()
        let relay = FixtureRelayGateway(mode: mode("-pc-model-slow-start"), clock: clock)
        let pairing = FixtureValues.pairing

        _ = try await relay.startLocalModel(.qwen3, pairing: pairing)
        for second in 1...5 {
            await clock.advance(by: 1)
            let status = try await relay.localModelStatus(pairing)
            XCTAssertEqual(status.state, "starting", "second \(second)")
            XCTAssertEqual(status.model, "qwen3")
            XCTAssertEqual(status.elapsedSeconds, second)
            XCTAssertEqual(status.homeLabel(for: .qwen3), "Loading \u{00B7} \(second)s")
        }

        await clock.advance(by: 1)
        let status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status.state, "ready")
        XCTAssertEqual(status.elapsedSeconds, 0)
        XCTAssertEqual(status.homeLabel(for: .qwen3), "Ready")
    }

    @MainActor
    func testAFailingStartEndsInErrorAndCanBeRetried() async throws {
        let clock = ManualClock()
        let relay = FixtureRelayGateway(mode: mode("-pc-model-start-error"), clock: clock)
        let pairing = FixtureValues.pairing

        var status = try await relay.startLocalModel(.nemotron, pairing: pairing)
        XCTAssertEqual(status.state, "starting")

        await clock.advance(by: 1)
        status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status, PCModelStatus(state: "error", model: "nemotron", busy: false, elapsedSeconds: 0, message: startFailedMessage))
        XCTAssertTrue(status.message.contains("Docker Desktop"))
        XCTAssertEqual(status.homeLabel(for: .nemotron), "Error \u{00B7} open Settings")

        // A retry begins a new operation, which ends the same way.
        status = try await relay.startLocalModel(.nemotron, pairing: pairing)
        XCTAssertEqual(status.state, "starting")
        await clock.advance(by: 1)
        status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status.state, "error")

        // The error is forgotten after C-48.
        await clock.advance(by: 119)
        status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status.state, "error")
        await clock.advance(by: 1)
        status = try await relay.localModelStatus(pairing)
        XCTAssertEqual(status.state, "stopped")
        XCTAssertNil(status.model)
    }

    @MainActor
    func testThePCModelRefusesWhatTheRelayRefuses() async throws {
        let clock = ManualClock()
        let relay = FixtureRelayGateway(mode: TestMode(), clock: clock)
        let pairing = FixtureValues.pairing

        // Only the two PC speech models can be named.
        let invalidStart = await relayFailure { try await relay.startLocalModel(.meta, pairing: pairing) }
        XCTAssertEqual(invalidStart, RelayFailure.wire(.invalidModel))
        let invalidStop = await relayFailure { try await relay.stopLocalModel(.parakeet, pairing: pairing) }
        XCTAssertEqual(invalidStop, RelayFailure.wire(.invalidModel))

        // One operation at a time.
        _ = try await relay.startLocalModel(.nemotron, pairing: pairing)
        let secondStart = await relayFailure { try await relay.startLocalModel(.qwen3, pairing: pairing) }
        XCTAssertEqual(secondStart, RelayFailure.wire(.modelOperationInProgress))
        let stopWhileStarting = await relayFailure { try await relay.stopLocalModel(.nemotron, pairing: pairing) }
        XCTAssertEqual(stopWhileStarting, RelayFailure.wire(.modelOperationInProgress))

        // Stop names the loaded model.
        await clock.advance(by: 1)
        let mismatch = await relayFailure { try await relay.stopLocalModel(.qwen3, pairing: pairing) }
        XCTAssertEqual(mismatch, RelayFailure.wire(.modelMismatch))

        // Starting the other model switches.
        var status = try await relay.startLocalModel(.qwen3, pairing: pairing)
        XCTAssertEqual(status.state, "starting")
        XCTAssertEqual(status.model, "qwen3")
        await clock.advance(by: 1)
        status = try await relay.localModelStatus(pairing)
        XCTAssertTrue(status.isReady(for: .qwen3))
        XCTAssertFalse(status.isReady(for: .nemotron))
    }

    // MARK: - Catalogue and answers

    @MainActor
    func testTheCatalogueIsTheCodexFixtureAndCanBeSlow() async throws {
        let pairing = FixtureValues.pairing
        let fast = FixtureRelayGateway(mode: TestMode(), clock: ManualClock())
        let catalogue = try await fast.models(pairing)
        XCTAssertEqual(catalogue, FixtureValues.codexCatalogue)

        let clock = ManualClock()
        let slow = FixtureRelayGateway(mode: mode("-pc-catalog-slow"), clock: clock)
        let load = Task { try await slow.models(pairing) }
        await clock.waitForSleepers(count: 1)
        await clock.advance(by: 8)
        let late = try await load.value
        XCTAssertEqual(late, FixtureValues.codexCatalogue)
        XCTAssertEqual(clock.uptime(), 8)
    }

    @MainActor
    func testAnAnswerEchoesTheSelectionAndAnUnknownSelectionIsRefused() async throws {
        let relay = FixtureRelayGateway(mode: TestMode(), clock: ManualClock())
        let pairing = FixtureValues.pairing
        var request = AssistRequest(sessionId: UUID(), memory: SessionMemory(), transcript: FixtureValues.segmentText)

        let withoutSelection = await relayFailure { [request] in try await relay.assist(request, pairing: pairing) }
        XCTAssertEqual(withoutSelection, RelayFailure.wire(.invalidAssistant))

        request.assistant = AssistantConfiguration(model: "gpt-6-luna", reasoningEffort: "high", provider: .codex)
        let response = try await relay.assist(request, pairing: pairing)
        XCTAssertEqual(response.answer, FixtureValues.answer)
        XCTAssertEqual(response.detectedQuestion, FixtureValues.segmentText)
        XCTAssertEqual(response.execution?.model, "gpt-6-luna")
        XCTAssertEqual(response.execution?.reasoningEffort, "high")
        XCTAssertEqual(response.execution?.codexMs, 600)
        XCTAssertNil(response.memory.throughSegmentId, "The relay never sends a marker.")

        // "none" is offered by the two 5.6 models only.
        request.assistant = AssistantConfiguration(model: "gpt-6-luna", reasoningEffort: "none", provider: .codex)
        let notOffered = await relayFailure { [request] in try await relay.assist(request, pairing: pairing) }
        XCTAssertEqual(notOffered, RelayFailure.wire(.assistantSelectionUnavailable))

        let summary = try await relay.summarize(
            SummaryRequest(sessionId: UUID(), assistant: AssistantConfiguration(), memory: SessionMemory(), transcript: FixtureValues.segmentText),
            pairing: pairing
        )
        XCTAssertFalse(summary.title.isEmpty)
        XCTAssertFalse(summary.summary.isEmpty)
    }
}
