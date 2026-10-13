import LiveCueCore
import SwiftUI
import XCTest
@testable import LiveCueRemake

/// Pairing fields after a read; alert without prefix on a failed verify; the confirmation before a saved
/// PC is replaced by another host (ST-18).
final class PairingViewModelTests: XCTestCase {
    private let connectedMessage = "Connected. Your pairing is saved on this iPhone."
    private let defaultMessage = "Your pairing is saved. The app reconnects automatically after restarting."
    private let payloadText = "Use the pairing QR from LiveCue Desktop. It must contain an HTTPS PC address and a valid pairing token."
    private let unreachableText = "Your PC is currently unreachable. Keep LiveCue Desktop running and check your internet connection. The app will reconnect automatically."
    /// Another PC than the fixture's, on a reserved test domain.
    private let otherAddress = "https://other.example.test:11443/"

    @MainActor
    private func makeModel(_ owners: TestOwners) -> PairingViewModel {
        PairingViewModel(pairing: owners.pairing, relay: owners.relay, app: owners.app)
    }

    private func otherPairing() throws -> PairingPayload {
        try PairingPayload.manual(address: otherAddress, token: FixtureValues.pairingToken)
    }

    // MARK: - The two layouts

    @MainActor
    func testWithoutAPairingTheOptionsAreShown() async {
        let model = makeModel(TestOwners(paired: false))

        XCTAssertEqual(model.title, "Pair Windows PC")
        XCTAssertFalse(model.isPaired)
        XCTAssertTrue(model.optionsVisible)
        XCTAssertEqual(model.savedAddress, "")
        XCTAssertFalse(model.canReadPayload)
        XCTAssertFalse(model.canVerify)
    }

    @MainActor
    func testWithAPairingTheOptionsAreHiddenUntilRevealed() async {
        let model = makeModel(TestOwners())

        XCTAssertEqual(model.title, "PC connection")
        XCTAssertTrue(model.isPaired)
        XCTAssertFalse(model.optionsVisible)
        XCTAssertEqual(model.savedAddress, "https://pc.example.test:11443/", "Exactly as saved, with port and slash.")
        XCTAssertEqual(model.savedMessage, defaultMessage)

        model.showsOptions = true
        XCTAssertTrue(model.optionsVisible)

        // Coming back to the screen starts with the options closed.
        model.appeared()
        XCTAssertFalse(model.optionsVisible)
    }

    @MainActor
    func testAppearingFillsTheAddressAndEmptiesTheOtherFields() async {
        let model = makeModel(TestOwners())
        model.address = "https://typed.example.test/"
        model.token = "typed"
        model.pasteText = "pasted"

        model.appeared()

        XCTAssertEqual(model.address, "https://pc.example.test:11443/")
        XCTAssertEqual(model.token, "")
        XCTAssertEqual(model.pasteText, "")

        let unpaired = makeModel(TestOwners(paired: false))
        unpaired.address = "https://typed.example.test/"
        unpaired.appeared()
        XCTAssertEqual(unpaired.address, "")
    }

    // MARK: - The saved PC (AC-IOS-135, 136, 137)

    @MainActor
    func testTheSavedStateFollowsTheLatestCheck() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        XCTAssertEqual(model.savedStateChip, StatusChipModel("Reconnecting", tone: .warning, mark: .spinner))

        await model.reconnect()
        XCTAssertEqual(model.savedStateChip, StatusChipModel("Connected", tone: .success, mark: .check))
        XCTAssertEqual(model.savedMessage, connectedMessage)
        XCTAssertFalse(model.isChecking)

        // The relay rejects the token: no "Your pairing is saved." in front.
        await owners.gateway.setHealth(.failure(RelayFailure.wire(.unauthorized)))
        await model.reconnect()
        XCTAssertEqual(model.savedStateChip, StatusChipModel("Pairing needs attention", tone: .danger, mark: .alert))
        XCTAssertEqual(model.savedMessage, RelayMessages.text(for: RelayFailure.wire(.unauthorized)))

        // A later plain network failure returns it to "Reconnecting".
        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await model.reconnect()
        XCTAssertEqual(model.savedStateChip, StatusChipModel("Reconnecting", tone: .warning, mark: .spinner))
        XCTAssertEqual(model.savedMessage, "Your pairing is saved. " + unreachableText)
    }

    @MainActor
    func testReconnectIsBusyWhileTheCheckRuns() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        await owners.gateway.setHold(true, for: "health")

        let checking = Task { await model.reconnect() }
        await owners.gateway.waitForCalls(count: 1)
        XCTAssertTrue(model.isChecking)

        await owners.gateway.release("health")
        await checking.value
        XCTAssertFalse(model.isChecking)
    }

    // MARK: - Reading a payload (AC-IOS-128, 129)

    @MainActor
    func testReadingAPayloadFillsTheFieldsAndClearsTheBox() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        model.pasteText = FixtureValues.pairing.storedText
        XCTAssertTrue(model.canReadPayload)

        model.readPayload()

        XCTAssertEqual(model.address, "https://pc.example.test:11443/")
        XCTAssertEqual(model.token, FixtureValues.pairingToken)
        XCTAssertEqual(model.pasteText, "")
        XCTAssertTrue(model.canVerify)
        // Nothing was saved, sent or announced.
        XCTAssertFalse(model.isPaired)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertNil(owners.app.alertMessage)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    @MainActor
    func testAPayloadThatIsNotValidAlertsAndChangesNothing() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        model.address = "https://typed.example.test/"
        model.token = "typed"
        model.pasteText = "not a pairing payload"

        model.readPayload()

        XCTAssertEqual(owners.app.alertMessage, payloadText)
        XCTAssertEqual(model.address, "https://typed.example.test/")
        XCTAssertEqual(model.token, "typed")
        XCTAssertEqual(model.pasteText, "not a pairing payload", "The box keeps the text for another try.")
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    // MARK: - The scanner (AC-IOS-138)

    @MainActor
    func testAScannedCodeIsHandledLikePastedText() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        model.isScannerPresented = true

        model.scannerFinished(.success(FixtureValues.pairing.storedText))

        XCTAssertFalse(model.isScannerPresented, "The sheet closes.")
        XCTAssertEqual(model.address, "https://pc.example.test:11443/")
        XCTAssertEqual(model.token, FixtureValues.pairingToken)
        XCTAssertNil(owners.app.alertMessage)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
    }

    @MainActor
    func testAScannedCodeThatIsNotAPairingAlerts() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        model.isScannerPresented = true

        model.scannerFinished(.success("https://pc.example.test/not-a-payload"))

        XCTAssertFalse(model.isScannerPresented)
        XCTAssertEqual(owners.app.alertMessage, payloadText)
        XCTAssertEqual(model.address, "")
        XCTAssertEqual(model.token, "")
    }

    @MainActor
    func testAScannerFailureClosesTheSheetAndAlerts() async {
        let failures: [QRScanFailure] = [.cameraDenied, .cameraUnavailable, .scannerUnavailable]
        for failure in failures {
            let owners = TestOwners(paired: false)
            let model = makeModel(owners)
            model.isScannerPresented = true

            model.scannerFinished(.failure(failure))

            XCTAssertFalse(model.isScannerPresented)
            XCTAssertEqual(owners.app.alertMessage, failure.message)
            // Paste and manual pairing remain usable.
            XCTAssertTrue(model.optionsVisible)
        }
    }

    // MARK: - Verify and pair (AC-IOS-130, 131)

    @MainActor
    func testASuccessfulVerificationShowsTheSavedPC() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        model.address = " HTTPS://PC.Example.Test:11443 "
        model.token = FixtureValues.pairingToken

        await model.verifyAndPair()

        XCTAssertNil(owners.app.alertMessage)
        XCTAssertNil(model.replacementPrompt)
        XCTAssertTrue(model.isPaired)
        XCTAssertEqual(model.title, "PC connection")
        XCTAssertEqual(owners.pairingStore.saved, [FixtureValues.pairing])
        XCTAssertEqual(model.savedStateChip, StatusChipModel("Connected", tone: .success, mark: .check))
        XCTAssertEqual(model.savedMessage, connectedMessage)
        XCTAssertEqual(model.savedAddress, "https://pc.example.test:11443/")
        // The token is not kept on screen, and the address field shows what was saved.
        XCTAssertEqual(model.token, "")
        XCTAssertEqual(model.address, "https://pc.example.test:11443/")
        XCTAssertFalse(model.optionsVisible)
        XCTAssertFalse(model.isVerifying)
    }

    @MainActor
    func testAFailedVerificationAlertsWithoutThePrefixAndKeepsEverything() async {
        // A saved pairing and the same host, so no confirmation is asked.
        let owners = TestOwners()
        let model = makeModel(owners)
        model.showsOptions = true
        model.address = "https://pc.example.test:11443/"
        model.token = FixtureValues.pairingToken
        await owners.gateway.setVerify(.failure(RelayFailure.local(.unreachable)))

        await model.verifyAndPair()

        XCTAssertEqual(owners.app.alertMessage, unreachableText)
        XCTAssertEqual(owners.app.alertMessage?.hasPrefix("Your pairing is saved."), false)
        // The fields keep their text, and the earlier pairing is untouched.
        XCTAssertEqual(model.address, "https://pc.example.test:11443/")
        XCTAssertEqual(model.token, FixtureValues.pairingToken)
        XCTAssertTrue(model.optionsVisible)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertEqual(owners.relay.pairing, FixtureValues.pairing)
        XCTAssertFalse(model.isVerifying)

        // A rejected token reads the same way: the relay's text, no prefix.
        await owners.gateway.setVerify(.failure(RelayFailure.wire(.unauthorized)))
        await model.verifyAndPair()
        XCTAssertEqual(owners.app.alertMessage, RelayMessages.text(for: RelayFailure.wire(.unauthorized)))
    }

    @MainActor
    func testInputThatIsNotValidAlertsAndSendsNothing() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        model.address = "http://pc.example.test:11443/"
        model.token = FixtureValues.pairingToken

        await model.verifyAndPair()

        XCTAssertEqual(owners.app.alertMessage, payloadText)
        XCTAssertEqual(model.address, "http://pc.example.test:11443/")
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    @MainActor
    func testAPairingThatCannotBeStoredAlerts() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        owners.pairingStore.saveFails = true
        model.address = "https://pc.example.test:11443/"
        model.token = FixtureValues.pairingToken

        await model.verifyAndPair()

        XCTAssertEqual(owners.app.alertMessage, "This iPhone could not save the pairing. Try again.")
        XCTAssertFalse(model.isPaired)
        XCTAssertEqual(model.token, FixtureValues.pairingToken)
    }

    @MainActor
    func testVerifyIsOfferedOnlyWithBothFieldsAndNotWhileVerifying() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)

        model.address = "https://pc.example.test:11443/"
        XCTAssertFalse(model.canVerify)
        model.token = FixtureValues.pairingToken
        XCTAssertTrue(model.canVerify)

        await owners.gateway.setHold(true, for: "verifyPairing")
        let verifying = Task { await model.verifyAndPair() }
        await owners.gateway.waitForCalls(count: 1)
        XCTAssertTrue(model.isVerifying)
        XCTAssertFalse(model.canVerify)

        await owners.gateway.release("verifyPairing")
        await verifying.value
        XCTAssertFalse(model.isVerifying)
    }

    // MARK: - Replacing a saved PC (ST-18)

    @MainActor
    func testAnotherHostAsksBeforeAnythingIsSent() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        model.address = otherAddress
        model.token = FixtureValues.pairingToken

        await model.verifyAndPair()

        let prompt = try XCTUnwrap(model.replacementPrompt)
        XCTAssertEqual(prompt, PairingReplacement(currentHost: "pc.example.test", newHost: "other.example.test"))
        XCTAssertFalse(prompt.newHostIsTailscaleName)
        XCTAssertNil(owners.app.alertMessage, "The confirmation is the screen's own alert.")
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertEqual(owners.relay.pairing, FixtureValues.pairing)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [], "Nothing is sent before \"Replace\".")
    }

    @MainActor
    func testCancellingTheReplacementSendsNothingAndKeepsTheSavedPairing() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        model.address = otherAddress
        model.token = FixtureValues.pairingToken
        await model.verifyAndPair()
        XCTAssertNotNil(model.replacementPrompt)

        model.cancelReplacement()

        XCTAssertNil(model.replacementPrompt)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertEqual(owners.pairingStore.load(), FixtureValues.pairing)
        XCTAssertEqual(owners.relay.pairing, FixtureValues.pairing)
        // The fields keep what was typed.
        XCTAssertEqual(model.address, otherAddress)
        XCTAssertEqual(model.token, FixtureValues.pairingToken)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    @MainActor
    func testConfirmingTheReplacementVerifiesAndPairs() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        let other = try otherPairing()
        model.address = otherAddress
        model.token = FixtureValues.pairingToken
        await model.verifyAndPair()
        XCTAssertNotNil(model.replacementPrompt)

        await model.confirmReplacement()

        XCTAssertNil(model.replacementPrompt)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["verifyPairing"])
        let pairings = await owners.gateway.pairings
        XCTAssertEqual(pairings, [other], "The request went to the new PC.")
        XCTAssertEqual(owners.pairingStore.saved, [other])
        XCTAssertEqual(owners.relay.pairing, other)
        XCTAssertEqual(model.savedAddress, otherAddress)
        XCTAssertEqual(model.savedMessage, connectedMessage)
        XCTAssertNil(owners.app.alertMessage)
    }

    @MainActor
    func testAFailedReplacementLeavesTheSavedPairing() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        model.address = otherAddress
        model.token = FixtureValues.pairingToken
        await owners.gateway.setVerify(.failure(RelayFailure.local(.unreachable)))
        await model.verifyAndPair()

        await model.confirmReplacement()

        XCTAssertNil(model.replacementPrompt)
        XCTAssertEqual(owners.app.alertMessage, unreachableText)
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)
        XCTAssertEqual(owners.relay.pairing, FixtureValues.pairing)
    }

    @MainActor
    func testTheSameHostNeedsNoConfirmation() async {
        // Another port, another path and another letter case are still the saved PC.
        for address in ["https://pc.example.test:11443/", "https://PC.Example.Test/", "https://pc.example.test:8443"] {
            let owners = TestOwners()
            let model = makeModel(owners)
            model.address = address
            model.token = FixtureValues.pairingToken

            await model.verifyAndPair()

            XCTAssertNil(model.replacementPrompt, address)
            let calls = await owners.gateway.calls
            XCTAssertEqual(calls, ["verifyPairing"], address)
        }
    }

    @MainActor
    func testWithNothingSavedThereIsNoConfirmation() async {
        let owners = TestOwners(paired: false)
        let model = makeModel(owners)
        model.address = otherAddress
        model.token = FixtureValues.pairingToken

        await model.verifyAndPair()

        XCTAssertNil(model.replacementPrompt)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, ["verifyPairing"])
        XCTAssertTrue(model.isPaired)
    }

    @MainActor
    func testNothingIsSentBetweenAScanAndVerifyAndPair() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        let other = try otherPairing()
        model.isScannerPresented = true

        // A scanned code only fills the two fields, whoever showed it.
        model.scannerFinished(.success(other.storedText))
        XCTAssertEqual(model.address, otherAddress)
        XCTAssertEqual(model.token, FixtureValues.pairingToken)
        XCTAssertNil(model.replacementPrompt)
        var calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
        XCTAssertTrue(owners.pairingStore.saved.isEmpty)

        // "Verify and pair" then asks first, and still sends nothing.
        await model.verifyAndPair()
        XCTAssertNotNil(model.replacementPrompt)
        calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    /// The screen with its confirmation, hosted in a window: a picture of it is kept with the test
    /// results. Nothing is sent while the confirmation is up.
    @MainActor
    func testTheReplacementConfirmationIsDrawn() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        let window = ScreensBHost.open(
            NavigationStack {
                PairingScreen()
            }
            .environment(model)
            .environment(owners.app)
        )
        // Appearing fills the address with the saved one, so the other address is typed after it.
        ScreensBHost.settle(window, seconds: 0.3)

        model.showsOptions = true
        model.address = otherAddress
        model.token = FixtureValues.pairingToken
        await model.verifyAndPair()
        XCTAssertNotNil(model.replacementPrompt)
        ScreensBHost.settle(window, seconds: 1)
        ScreensBHost.attachPicture(of: window, named: "wp11-pairing-replace-prompt", to: self)

        model.cancelReplacement()
        ScreensBHost.settle(window, seconds: 0.6)
        ScreensBHost.close(window)
        let calls = await owners.gateway.calls
        XCTAssertEqual(calls, [])
    }

    @MainActor
    func testATailscaleNameIsRecognised() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        // Put together at run time: no real host is written here.
        model.address = "https://" + ["other-pc", "example-net", "ts", "net"].joined(separator: ".") + ":11443/"
        model.token = FixtureValues.pairingToken

        await model.verifyAndPair()

        let prompt = try XCTUnwrap(model.replacementPrompt)
        XCTAssertTrue(prompt.newHostIsTailscaleName)
        XCTAssertEqual(prompt.currentHost, "pc.example.test")
    }
}
