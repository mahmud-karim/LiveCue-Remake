import XCTest

/// Pictures of Settings, Transcription, PC speech model, Meta API key, Pairing, Model Library, Speech
/// benchmark, History and Session detail in their main states, for comparing the two simulator legs with
/// the mockups by eye. Each test only launches the app in fixture mode, walks to a state and attaches a
/// named screenshot; it asserts no more than that the first element it waits for exists. The tests that
/// prove behaviour are the other UI test classes.
final class ScreensBScreenshotUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
        executionTimeAllowance = 600
    }

    // MARK: - Settings, Transcription and the Meta key

    @MainActor
    func testSettingsFromTopToBottom() throws {
        UITestSupport.launch()
        openSettings()
        // The first health check of the fixture relay turns the pairing row to "connected".
        _ = element(labelled: "Saved · connected").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-settings")

        XCUIApplication().swipeUp()
        shot("wp11-settings-middle")
        XCUIApplication().swipeUp()
        shot("wp11-settings-bottom")
    }

    @MainActor
    func testTranscriptionAndMetaKeyWithASavedKey() throws {
        UITestSupport.launch()
        openTranscription()
        shot("wp11-transcription-meta")

        tap("meta-key-settings")
        UITestSupport.waitFor("meta-key")
        shot("wp11-meta-key-saved")

        tap("verify-meta-key")
        _ = UITestSupport.waitForLabel("meta-key-message", contains: "No microphone audio")
        shot("wp11-meta-key-tested")
    }

    @MainActor
    func testMetaKeyWithoutAKey() throws {
        UITestSupport.launch(["-meta-no-key"])
        openSettings()
        shot("wp11-settings-meta-key-required")

        tap("meta-key-settings")
        UITestSupport.waitFor("meta-key")
        shot("wp11-meta-key-empty")
    }

    // MARK: - PC speech model

    @MainActor
    func testPCSpeechModelStartAndStop() throws {
        UITestSupport.launch(["-pc-model-slow-start"])
        openTranscription()
        tap("provider-nemotron")
        _ = button("PC speech model").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-transcription-pc-model")

        tap(button("PC speech model"))
        UITestSupport.waitFor("pc-model-state")
        _ = UITestSupport.waitForLabel("pc-model-state", equals: "Stopped")
        shot("wp11-pc-model-stopped")

        tap("start-pc-model")
        _ = UITestSupport.waitForLabel("pc-model-state", equals: "Starting")
        shot("wp11-pc-model-starting")

        _ = UITestSupport.waitForLabel("pc-model-state", equals: "Ready", timeout: 30)
        shot("wp11-pc-model-ready")

        tap("stop-pc-model")
        _ = UITestSupport.waitForLabel("pc-model-state", equals: "Stopped", timeout: 30)

        // Back on Settings the same row is shown directly.
        back()
        back()
        _ = UITestSupport.element("speech-provider").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-settings-pc-model")
    }

    @MainActor
    func testPCSpeechModelStartError() throws {
        UITestSupport.launch(["-pc-model-start-error"])
        openTranscription()
        tap("provider-qwen3")
        tap(button("PC speech model"))
        UITestSupport.waitFor("pc-model-state")

        tap("start-pc-model")
        _ = UITestSupport.waitForLabel("pc-model-state", equals: "Error", timeout: 30)
        shot("wp11-pc-model-error")
    }

    // MARK: - On-device models

    @MainActor
    func testModelLibraryAndSpeechBenchmark() throws {
        UITestSupport.launch()
        openTranscription()
        tap("provider-voz")
        _ = button("Model Library").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-transcription-on-device")

        tap(button("Model Library"))
        _ = button("Download & use").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-model-library-not-loaded")

        // The fixture loader stays at one half for two seconds.
        tap(button("Download & use"))
        _ = UITestSupport.element("model-download-progress").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-model-library-preparing")

        _ = button("Reload").waitForExistence(timeout: 30)
        shot("wp11-model-library-ready")

        back()
        tap(button("Speech benchmark"))
        _ = button("Use latest transcript").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-benchmark")
    }

    @MainActor
    func testModelLibraryWithParakeetReady() throws {
        UITestSupport.launch(["-on-device-ready"])
        openSettings()
        shot("wp11-settings-on-device")

        tap(button("Model Library"))
        _ = button("Reload").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-model-library-parakeet-ready")
    }

    // MARK: - Pairing

    @MainActor
    func testPairingWithASavedPC() throws {
        UITestSupport.launch()
        openSettings()
        tap("pair-pc")
        UITestSupport.waitFor("saved-pc-state")
        _ = UITestSupport.waitForLabel("saved-pc-state", equals: "Connected")
        shot("wp11-pairing-saved")

        tap("show-pairing-options")
        _ = UITestSupport.element("scan-pairing-qr").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-pairing-options")
        XCUIApplication().swipeUp()
        shot("wp11-pairing-options-lower")
        XCUIApplication().swipeDown()

        // The simulator has no camera: the sheet closes and the alert says so.
        tap("scan-pairing-qr")
        let alert = XCUIApplication().alerts.firstMatch
        _ = alert.waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-qr-camera-unavailable")
        if alert.buttons["OK"].exists {
            alert.buttons["OK"].tap()
        }
    }

    @MainActor
    func testPairingReplacesAnotherHostOnlyAfterAsking() throws {
        UITestSupport.launch()
        openSettings()
        tap("pair-pc")
        UITestSupport.waitFor("saved-pc-state")
        tap("show-pairing-options")
        _ = UITestSupport.element("scan-pairing-qr").waitForExistence(timeout: UITestSupport.timeout)

        // Another host on a reserved test domain, and the synthetic token of the fixtures.
        // Typing is best effort here: a field under the keyboard is scrolled up first, and a field that
        // cannot be reached is left alone. The screenshots show how far the walk came.
        let app = XCUIApplication()
        let address = app.textFields["PC address"]
        if bringIntoReach(address) {
            address.tap()
            address.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 48))
            address.typeText("https://other.example.test:11443/")
        }
        shot("wp11-pairing-address-typed")

        let token = app.secureTextFields["Pairing token"]
        if bringIntoReach(token) {
            token.tap()
            token.typeText(String(repeating: "A", count: 43))
        }
        shot("wp11-pairing-typed")

        let verify = button("Verify and pair")
        if bringIntoReach(verify) {
            verify.tap()
        }
        let alert = app.alerts["Replace this PC?"]
        _ = alert.waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-pairing-replace-prompt")
        if alert.buttons["Cancel"].exists {
            alert.buttons["Cancel"].tap()
        }
    }

    @MainActor
    func testPairingWhileThePCIsOffline() throws {
        UITestSupport.launch(["-pc-offline"])
        openSettings()
        tap("pair-pc")
        UITestSupport.waitFor("saved-pc-state")
        _ = element(labelledWith: "Your pairing is saved. Your PC is currently unreachable.").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-pairing-reconnecting")
    }

    @MainActor
    func testPairingWithNothingSaved() throws {
        // The pairing store of the persistence tests starts empty unless a test saved into it.
        UITestSupport.launch(["-pairing-persistence-test"])
        openSettings()
        shot("wp11-settings-not-paired")

        tap("pair-pc")
        _ = UITestSupport.element("scan-pairing-qr").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-pairing-new")
        XCUIApplication().swipeUp()
        shot("wp11-pairing-new-lower")
    }

    // MARK: - History and Session detail

    @MainActor
    func testHistoryEmpty() throws {
        UITestSupport.launch()
        openTab("History")
        _ = element(labelledWith: "No conversations yet").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-history-empty")
    }

    /// Needs the Start control of Home, which another work package builds: without it the test is
    /// skipped, and the pictures of History with conversations come from the unit tests instead.
    @MainActor
    func testHistoryWithAConversationAndItsDetail() throws {
        UITestSupport.launch()
        openTab("History")
        openTab("Live")
        guard UITestSupport.element("start-session").waitForExistence(timeout: 5) else {
            throw XCTSkip("Home has no Start control in this build.")
        }

        // One conversation with one answer, then back to the tabs.
        tap("start-session")
        _ = UITestSupport.element("assist-button").waitForExistence(timeout: UITestSupport.timeout)
        tap("assist-button")
        _ = UITestSupport.element("assistant-answer").waitForExistence(timeout: UITestSupport.timeout)
        tap("end-session")
        _ = UITestSupport.tabButton("History").waitForExistence(timeout: UITestSupport.timeout)

        openTab("History")
        let usage = UITestSupport.element("history-transcription-cost")
        _ = usage.waitForExistence(timeout: UITestSupport.timeout)
        shot("wp11-history-items")

        // The row is the button over the usage label.
        let row = XCUIApplication().buttons.matching(NSPredicate(format: "label CONTAINS %@", "transcript segments")).firstMatch
        if row.waitForExistence(timeout: UITestSupport.timeout) {
            row.tap()
            _ = element(labelledWith: "Transcription usage").waitForExistence(timeout: UITestSupport.timeout)
            shot("wp11-session-detail")
            XCUIApplication().swipeUp()
            shot("wp11-session-detail-lower")
            back()
        }

        if row.waitForExistence(timeout: UITestSupport.timeout) {
            row.swipeLeft()
            let delete = XCUIApplication().buttons["Delete"].firstMatch
            if delete.waitForExistence(timeout: UITestSupport.timeout) {
                shot("wp11-history-swiped")
                delete.tap()
                let alert = XCUIApplication().alerts["Delete this conversation?"]
                _ = alert.waitForExistence(timeout: UITestSupport.timeout)
                shot("wp11-history-delete-prompt")
                if alert.buttons["Cancel"].exists {
                    alert.buttons["Cancel"].tap()
                }
            }
        }
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }

    /// Selects a tab. The first wait after a launch may take as long as the launch does.
    @MainActor
    private func openTab(_ title: String) {
        let anyButton = XCUIApplication().buttons[title].firstMatch
        XCTAssertTrue(anyButton.waitForExistence(timeout: UITestSupport.launchTimeout), "There is no \(title) tab.")
        UITestSupport.tabButton(title).tap()
    }

    @MainActor
    private func openSettings() {
        openTab("Settings")
        UITestSupport.waitFor("speech-provider")
    }

    @MainActor
    private func openTranscription() {
        openSettings()
        tap("speech-provider")
        UITestSupport.waitFor("provider-meta")
    }

    /// Taps the element with this identifier once it exists; a missing element leaves the walk where it
    /// is, and the next screenshot shows why.
    @MainActor
    private func tap(_ identifier: String) {
        tap(UITestSupport.element(identifier))
    }

    @MainActor
    private func tap(_ element: XCUIElement) {
        if element.waitForExistence(timeout: UITestSupport.timeout) {
            element.tap()
        }
    }

    /// True once the element can be tapped. An element that exists but is covered, by the keyboard or by
    /// the tab bar, is scrolled up a little at a time.
    @MainActor
    private func bringIntoReach(_ element: XCUIElement) -> Bool {
        guard element.waitForExistence(timeout: UITestSupport.timeout) else {
            return false
        }
        let app = XCUIApplication()
        for _ in 0..<4 where !element.isHittable {
            let from = app.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.35))
            let to = app.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.2))
            from.press(forDuration: 0.05, thenDragTo: to)
        }
        return element.isHittable
    }

    @MainActor
    private func back() {
        let button = XCUIApplication().navigationBars.buttons.firstMatch
        if button.waitForExistence(timeout: UITestSupport.timeout) {
            button.tap()
        }
    }

    @MainActor
    private func button(_ label: String) -> XCUIElement {
        XCUIApplication().buttons[label].firstMatch
    }

    /// Any element with exactly this label.
    @MainActor
    private func element(labelled label: String) -> XCUIElement {
        XCUIApplication().descendants(matching: .any).matching(NSPredicate(format: "label == %@", label)).firstMatch
    }

    /// Any element whose label contains this text.
    @MainActor
    private func element(labelledWith text: String) -> XCUIElement {
        XCUIApplication().descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS %@", text)).firstMatch
    }
}
