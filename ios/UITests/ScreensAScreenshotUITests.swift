import XCTest

/// Pictures of Home, the live session and Assistant Lab in their main states, for comparing the two
/// simulator legs with the mockups by eye. Each test only launches the app in fixture mode, walks to a
/// state and attaches a named screenshot; it asserts no more than that the element it waits for exists.
/// The tests that prove behaviour are the other UI test classes.
final class ScreensAScreenshotUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
        // A live session keeps its level meter and its chip moving, which slows every query down.
        executionTimeAllowance = 600
    }

    // MARK: - Home

    @MainActor
    func testHomeReady() throws {
        UITestSupport.launch()
        UITestSupport.waitFor("start-session", timeout: UITestSupport.launchTimeout)
        // The first health check of the fixture relay turns the chip to "PC online".
        _ = element(labelled: "PC online").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp10-home-ready")
    }

    @MainActor
    func testHomeSetup() throws {
        // A pairing store with nothing in it and no Meta key: the first launch.
        UITestSupport.launch(["-pairing-persistence-test", "-meta-no-key"])
        UITestSupport.waitFor("start-session", timeout: UITestSupport.launchTimeout)
        UITestSupport.waitFor("home-meta-state")
        shot("wp10-home-setup")
    }

    @MainActor
    func testHomeReconnecting() throws {
        UITestSupport.launch(["-pc-offline"])
        UITestSupport.waitFor("start-session", timeout: UITestSupport.launchTimeout)
        _ = element(labelled: "PC reconnecting").waitForExistence(timeout: UITestSupport.timeout)
        shot("wp10-home-pc-reconnecting")
    }

    // MARK: - Live session

    @MainActor
    func testLiveSessionListeningAnswersAndPause() throws {
        UITestSupport.launch()
        UITestSupport.waitFor("start-session", timeout: UITestSupport.launchTimeout).tap()

        // Listening, with the fixture segment in the collapsed transcript.
        UITestSupport.waitFor("assist-button")
        UITestSupport.waitFor("transcript-preview")
        shot("wp10-live-listening")

        UITestSupport.waitFor("expand-transcript").tap()
        UITestSupport.waitFor("live-transcript")
        shot("wp10-live-transcript-expanded")
        UITestSupport.waitFor("expand-transcript").tap()
        UITestSupport.waitFor("transcript-preview")

        // One Assist; the answer in progress is caught when the simulator is quick enough.
        UITestSupport.waitFor("assist-button").tap()
        if UITestSupport.element("streaming-answer").waitForExistence(timeout: 2) {
            shot("wp10-live-assist-running")
        }
        UITestSupport.waitFor("assistant-answer")
        shot("wp10-live-one-answer")

        UITestSupport.waitFor("timing-disclosure").tap()
        UITestSupport.waitFor("response-timing")
        shot("wp10-live-answer-timing-open")
        UITestSupport.waitFor("timing-disclosure").tap()

        // Three answers: one latest, two earlier.
        UITestSupport.waitFor("assist-button").tap()
        UITestSupport.waitFor("previous-assistant-answer")
        waitUntilAssistIsIdle()
        UITestSupport.waitFor("assist-button").tap()
        let earlier = XCUIApplication().descendants(matching: .any).matching(identifier: "previous-assistant-answer")
        XCTAssertTrue(earlier.element(boundBy: 1).waitForExistence(timeout: UITestSupport.timeout), "The third answer did not arrive.")
        waitUntilAssistIsIdle()
        UITestSupport.waitFor("assistant-answer")
        shot("wp10-live-three-answers")

        UITestSupport.waitFor("pause-session").tap()
        _ = UITestSupport.waitForLabel("pause-session", equals: "Resume")
        shot("wp10-live-paused")

        // Assistant Lab from the session, and back.
        UITestSupport.waitFor("assistant-lab").tap()
        UITestSupport.waitFor("assistant-provider")
        shot("wp10-assistant-lab-from-session")
        XCUIApplication().navigationBars.buttons.firstMatch.tap()

        UITestSupport.waitFor("end-session").tap()
        UITestSupport.waitFor("start-session")
        shot("wp10-home-after-session")

        // The answers of the session are now compared in Assistant Lab.
        UITestSupport.waitFor("assistant-lab").tap()
        UITestSupport.waitFor("assistant-provider")
        XCUIApplication().swipeUp()
        shot("wp10-assistant-recent-comparisons")

        // The first comparison opens its run details. Its row is titled with the model that answered.
        let comparison = XCUIApplication().buttons["gpt-5.6-sol"].firstMatch
        if comparison.waitForExistence(timeout: 5) {
            comparison.tap()
            UITestSupport.waitFor("response-timing")
            shot("wp10-run-details")
        }
    }

    @MainActor
    func testLiveSessionAssistErrorAndRetry() throws {
        UITestSupport.launch(["-assist-fails-once"])
        UITestSupport.waitFor("start-session", timeout: UITestSupport.launchTimeout).tap()

        UITestSupport.waitFor("assist-button").tap()
        UITestSupport.waitFor("assist-error")
        shot("wp10-live-assist-error")

        UITestSupport.waitFor("retry-failed-assist").tap()
        UITestSupport.waitFor("assistant-answer")
        shot("wp10-live-after-retry")

        UITestSupport.waitFor("end-session").tap()
        UITestSupport.waitFor("start-session")
    }

    // MARK: - Assistant Lab

    @MainActor
    func testAssistantLabWithCodex() throws {
        UITestSupport.launch()
        UITestSupport.waitFor("assistant-lab", timeout: UITestSupport.launchTimeout).tap()
        UITestSupport.waitFor("assistant-provider")
        UITestSupport.waitFor("choose-gpt-5.6-sol")
        shot("wp10-assistant-codex")

        XCUIApplication().swipeUp()
        shot("wp10-assistant-codex-lower")
        XCUIApplication().swipeDown()

        UITestSupport.waitFor("reasoning-picker").tap()
        UITestSupport.waitFor("effort-low")
        shot("wp10-assistant-reasoning")
    }

    @MainActor
    func testAssistantLabWithOpenRouterAndNoKey() throws {
        UITestSupport.launch(["-openrouter-ui-reset"])
        UITestSupport.waitFor("assistant-lab", timeout: UITestSupport.launchTimeout).tap()
        UITestSupport.waitFor("assistant-provider-openrouter").tap()
        UITestSupport.waitFor("openrouter-key")
        UITestSupport.waitFor("openrouter-model-picker")
        shot("wp10-assistant-openrouter-no-key")

        UITestSupport.waitFor("openrouter-model-picker").tap()
        UITestSupport.waitFor("choose-test/direct")
        shot("wp10-assistant-openrouter-models")
    }

    @MainActor
    func testAssistantLabWithOpenRouterKeyAndModel() throws {
        UITestSupport.launch(["-openrouter-ui-reset"])
        UITestSupport.waitFor("assistant-lab", timeout: UITestSupport.launchTimeout).tap()
        UITestSupport.waitFor("assistant-provider-openrouter").tap()

        // A made-up key for the fixture service, typed like a person would.
        // The field is tapped at its centre and the text goes to whatever has the keyboard: on iOS 26 a
        // secure field is not reported as hittable.
        let field = UITestSupport.waitFor("openrouter-key")
        field.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).tap()
        // A simulator with a hardware keyboard shows none on screen, so this only gives focus time to settle.
        _ = XCUIApplication().keyboards.firstMatch.waitForExistence(timeout: 5)
        XCUIApplication().typeText("fixture-openrouter-key")
        UITestSupport.waitFor("save-openrouter-key").tap()
        UITestSupport.waitFor("verify-openrouter-key")

        UITestSupport.waitFor("openrouter-model-picker").tap()
        UITestSupport.waitFor("choose-test/direct").tap()
        UITestSupport.waitFor("selected-assistant-model")
        UITestSupport.waitFor("verify-openrouter-key").tap()
        shot("wp10-assistant-openrouter")

        XCUIApplication().swipeUp()
        shot("wp10-assistant-openrouter-lower")
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }

    /// Any element with exactly this label.
    @MainActor
    private func element(labelled label: String) -> XCUIElement {
        XCUIApplication().descendants(matching: .any).matching(NSPredicate(format: "label == %@", label)).firstMatch
    }

    /// The Assist control reads "Assist" again once an answer has been saved.
    @MainActor
    private func waitUntilAssistIsIdle() {
        _ = UITestSupport.waitForLabel("assist-button", equals: "Assist")
    }
}
