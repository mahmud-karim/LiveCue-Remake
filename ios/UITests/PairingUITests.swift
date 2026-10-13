import XCTest

/// T7 and T13: the saved pairing and the way to the QR scanner.
final class PairingUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
        // Three launches.
        executionTimeAllowance = 600
    }

    /// T7. AC-IOS-132, 133, 134, 135. The pairing is saved by the launch argument, not typed.
    @MainActor
    func testPairingSurvivesRelaunchAndMissingKeychainCopy() throws {
        // Pictures of the first run, taken while this simulator has no test pairing yet. Nothing is
        // asserted here: on a simulator that ran this test before, the pairing is already saved.
        UITestWalk.launchToHome(["-pairing-persistence-test", "-meta-no-key"])
        if UITestWalk.anyLabelled("PC offline").waitForExistence(timeout: 5) {
            shot("t7-home-first-run")
            UITestWalk.tap("pair-pc")
            if UITestSupport.element("scan-pairing-qr").waitForExistence(timeout: UITestSupport.timeout) {
                shot("t7-pairing-not-paired")
            }
        } else {
            UITestWalk.note("A test pairing was already saved on this simulator.")
        }

        // The launch saves the fixture pairing through the real pairing store.
        UITestWalk.launchToHome(["-pairing-persistence-test", "-save-pairing-fixture"])
        UITestWalk.openSettings()
        UITestWalk.tap("pair-pc")
        UITestSupport.waitFor("saved-pc-state")
        XCTAssertTrue(
            UITestWalk.waitForText("saved-pc-address", equals: UITestWalk.pairingAddress),
            "The saved address reads \"\(UITestWalk.text(of: UITestSupport.element("saved-pc-address")))\"."
        )
        // AC-IOS-134: Reconnect is offered, and the QR option is hidden until the options are opened.
        XCTAssertTrue(UITestSupport.element("reconnect-pc").exists, "A saved pairing has no Reconnect.")
        XCTAssertTrue(UITestSupport.element("show-pairing-options").exists, "A saved pairing has no way to the pairing options.")
        XCTAssertFalse(UITestSupport.element("scan-pairing-qr").exists, "The QR option is shown before the options are opened.")
        shot("t7-pairing-saved")

        // AC-IOS-132 and 133: after a relaunch, with the Keychain copy gone, the pairing is restored
        // from the file and the address is shown exactly as saved, port and trailing slash included.
        UITestWalk.launchToHome(["-pairing-persistence-test", "-drop-test-keychain"])
        XCTAssertTrue(UITestWalk.anyLabelled("Saved · connected").waitForExistence(timeout: UITestSupport.timeout), "Home does not show the saved PC after the relaunch.")
        UITestWalk.openSettings()
        UITestWalk.tap("pair-pc")
        UITestSupport.waitFor("saved-pc-state")
        XCTAssertTrue(
            UITestWalk.waitForText("saved-pc-address", equals: UITestWalk.pairingAddress),
            "The restored address reads \"\(UITestWalk.text(of: UITestSupport.element("saved-pc-address")))\"."
        )
        XCTAssertTrue(UITestSupport.element("reconnect-pc").exists, "The restored pairing has no Reconnect.")
        XCTAssertFalse(UITestSupport.element("scan-pairing-qr").exists, "The QR option is shown before the options are opened.")

        // AC-IOS-135: Reconnect with a reachable PC reads "Connected".
        UITestWalk.tap("reconnect-pc")
        XCTAssertTrue(
            UITestSupport.waitForLabel("saved-pc-state", contains: "Connected"),
            "The state reads \"\(UITestSupport.element("saved-pc-state").label)\" after Reconnect."
        )
        XCTAssertTrue(UITestSupport.waitForLabel("reconnect-pc", equals: "Reconnect"), "Reconnect is still checking.")
        XCTAssertTrue(UITestWalk.anyLabelContaining("Your pairing is saved on this iPhone").exists, "The connected message is not shown.")
        shot("t7-pairing-restored")
    }

    /// T13. AC-IOS-127.
    @MainActor
    func testPairingOffersQRScanner() throws {
        let app = UITestWalk.launchToHome()
        UITestWalk.openSettings()
        UITestWalk.tap("pair-pc")
        UITestSupport.waitFor("saved-pc-state")
        XCTAssertTrue(UITestSupport.waitForLabel("saved-pc-state", equals: "Connected"), "The saved PC is not connected.")
        XCTAssertFalse(UITestSupport.element("scan-pairing-qr").exists, "The QR option is shown before the options are opened.")
        shot("t13-pairing-saved")

        // AC-IOS-127: with a saved pairing the options are opened first; then "Scan PC QR code" is offered.
        UITestWalk.tap("show-pairing-options")
        let scan = UITestSupport.waitFor("scan-pairing-qr")
        XCTAssertEqual(scan.label, "Scan PC QR code")
        XCTAssertTrue(scan.isEnabled, "\"Scan PC QR code\" is disabled.")
        XCTAssertTrue(UITestWalk.button("Verify and pair").exists, "Manual pairing is not offered beside the scanner.")
        shot("t13-pairing-options")
        app.swipeUp()
        shot("t13-pairing-options-lower")
        app.swipeDown()

        // The simulator has no camera: the sheet closes and an alert says so. This is kept as a picture
        // and not asserted, because the test seam has no camera.
        UITestWalk.tap("scan-pairing-qr")
        let alert = app.alerts.firstMatch
        if alert.waitForExistence(timeout: UITestSupport.timeout) {
            shot("t13-qr-camera-unavailable")
            if alert.buttons["OK"].exists {
                alert.buttons["OK"].tap()
            }
        } else {
            shot("t13-qr-sheet")
            UITestWalk.note("The QR sheet raised no alert on this simulator.")
        }
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }
}
