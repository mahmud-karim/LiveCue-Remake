import XCTest

/// T10 and T11: choosing the Codex model and effort, and what the timing of an answer shows.
final class AssistantUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
        executionTimeAllowance = 600
    }

    /// T10. AC-IOS-114, 117.
    @MainActor
    func testAnotherCodexModelAtLowEffortCanAssist() throws {
        let app = UITestWalk.launchToHome()

        // AC-IOS-114: the catalogue of the current service is loaded when Assistant Lab appears.
        UITestWalk.openAssistantLab()
        for model in ["gpt-6-astra", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-luna", "gpt-5.3-codex-spark"] {
            XCTAssertTrue(UITestSupport.element("choose-\(model)").waitForExistence(timeout: UITestSupport.timeout), "\(model) is not listed.")
        }
        XCTAssertTrue(UITestWalk.anyLabelContaining("From this PC's Codex catalog").waitForExistence(timeout: UITestSupport.timeout), "The catalogue is not reported as loaded.")
        XCTAssertTrue(UITestWalk.waitForText("selected-assistant-model", equals: "gpt-5.6-sol"), "The default model is not gpt-5.6-sol.")
        XCTAssertTrue(UITestWalk.anyLabelled("Reasoning: low").exists, "The default effort is not low.")
        shot("t10-assistant-codex")

        // AC-IOS-117: another model that supports the current effort keeps it.
        UITestWalk.tap("choose-gpt-5.6-luna")
        XCTAssertTrue(UITestWalk.waitForText("selected-assistant-model", equals: "gpt-5.6-luna"), "gpt-5.6-luna was not selected.")
        XCTAssertTrue(UITestWalk.anyLabelled("Reasoning: low").waitForExistence(timeout: 5), "The effort was not kept.")

        // AC-IOS-117: a model that does not support the current effort gets its first one.
        UITestWalk.tap("reasoning-picker")
        UITestWalk.tap("effort-none")
        XCTAssertTrue(UITestWalk.anyLabelled("Reasoning: none").waitForExistence(timeout: UITestSupport.timeout), "The effort did not become none.")
        UITestWalk.tap("choose-gpt-6-astra")
        XCTAssertTrue(UITestWalk.waitForText("selected-assistant-model", equals: "gpt-6-astra"), "gpt-6-astra was not selected.")
        XCTAssertTrue(UITestWalk.anyLabelled("Reasoning: low").waitForExistence(timeout: 5), "gpt-6-astra did not get its first effort.")
        XCTAssertTrue(UITestSupport.waitUntilSelected(UITestSupport.element("choose-gpt-6-astra")), "The gpt-6-astra row is not the selected one.")
        shot("t10-assistant-another-model")
        UITestWalk.back()

        // AC-IOS-117: an Assist with that selection succeeds, and its timing names it.
        UITestWalk.startSession()
        UITestWalk.assist(reaching: 1)
        XCTAssertFalse(UITestSupport.element("assist-error").exists, "The Assist failed with gpt-6-astra at low effort.")
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared.")
        UITestWalk.openLatestTiming()
        XCTAssertTrue(UITestWalk.anyLabelled("gpt-6-astra · low").waitForExistence(timeout: 5), "The timing does not name gpt-6-astra at low effort.")
        shot("t10-answer-with-another-model")
        UITestWalk.stopSession()
    }

    /// T11. AC-IOS-060, 068, 069, 115, 116, 125.
    @MainActor
    func testSelectionReasoningAndTiming() throws {
        UITestWalk.launchToHome()
        UITestWalk.openAssistantLab()

        // AC-IOS-115: a tapped model row becomes selected and its id is shown.
        UITestWalk.tap("choose-gpt-5.6-luna")
        XCTAssertTrue(UITestSupport.waitUntilSelected(UITestSupport.element("choose-gpt-5.6-luna")), "The tapped row is not selected.")
        XCTAssertFalse(UITestSupport.element("choose-gpt-5.6-sol").isSelected, "The row of the old model stayed selected.")
        XCTAssertTrue(UITestWalk.waitForText("selected-assistant-model", equals: "gpt-5.6-luna"), "The id of the tapped model is not shown.")

        // AC-IOS-116: the picker lists the efforts of the selected model; "None (no reasoning)" gives
        // "Reasoning: none".
        UITestWalk.tap("reasoning-picker")
        for effort in ["none", "low", "medium", "high"] {
            XCTAssertTrue(UITestSupport.element("effort-\(effort)").waitForExistence(timeout: UITestSupport.timeout), "The effort \(effort) is not listed.")
        }
        XCTAssertTrue(UITestSupport.waitUntilSelected(UITestSupport.element("effort-low")), "The current effort is not marked.")
        XCTAssertEqual(UITestSupport.element("effort-none").label, "None (no reasoning)")
        shot("t11-reasoning-picker")
        UITestWalk.tap("effort-none")
        XCTAssertTrue(UITestWalk.anyLabelled("Reasoning: none").waitForExistence(timeout: UITestSupport.timeout), "The selection does not read \"Reasoning: none\".")
        shot("t11-assistant-selection")
        UITestWalk.back()

        // AC-IOS-069: the timing of a Codex answer has a row that starts "PC: Codex CLI call".
        UITestWalk.startSession()
        UITestWalk.assist(reaching: 1)
        UITestWalk.openLatestTiming()
        XCTAssertTrue(UITestWalk.anyLabelled("gpt-5.6-luna · none").waitForExistence(timeout: 5), "The timing does not name the selection that answered.")
        XCTAssertTrue(UITestWalk.anyLabelStarting("PC: Codex CLI call").exists, "The timing has no \"PC: Codex CLI call\" row.")
        XCTAssertTrue(UITestWalk.anyLabelStarting("Transcription after tap").exists, "A fresh answer does not read \"Transcription after tap\".")
        shot("t11-timing-open")
        UITestWalk.closeLatestTiming()

        // AC-IOS-125: Assistant Lab can be reached from the live screen. AC-IOS-068: a change made
        // there during the session.
        let labLink = UITestSupport.waitFor("assistant-lab")
        XCTAssertTrue(labLink.isEnabled, "The link to Assistant Lab is disabled between Assists.")
        labLink.tap()
        UITestSupport.waitFor("assistant-provider")
        UITestWalk.tap("choose-gpt-6-luna")
        XCTAssertTrue(UITestWalk.waitForText("selected-assistant-model", equals: "gpt-6-luna"), "gpt-6-luna was not selected.")
        XCTAssertTrue(UITestWalk.anyLabelled("Reasoning: low").waitForExistence(timeout: 5), "gpt-6-luna did not get its first effort.")
        shot("t11-assistant-lab-in-session")
        UITestWalk.back()
        UITestSupport.waitFor("assist-button")

        // AC-IOS-060: the same-text retry uses the selection of this moment and reads "Transcription
        // (reused)".
        let retry = UITestSupport.waitFor("retry-same-text")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(retry), "The same-text retry is not enabled after an answer.")
        retry.tap()
        XCTAssertTrue(UITestSupport.element("previous-assistant-answer").waitForExistence(timeout: UITestSupport.timeout), "The retry brought no second answer.")
        XCTAssertTrue(UITestSupport.waitForLabel("assist-button", equals: "Assist"), "The retry did not finish.")
        XCTAssertFalse(UITestSupport.element("assist-error").exists, "The retry failed.")
        UITestWalk.openLatestTiming()
        XCTAssertTrue(UITestWalk.anyLabelled("gpt-6-luna · low").waitForExistence(timeout: 5), "The retry did not use the selected model.")
        XCTAssertTrue(UITestWalk.anyLabelStarting("Transcription (reused)").exists, "The retry does not read \"Transcription (reused)\".")
        XCTAssertTrue(UITestWalk.anyLabelContaining("same-text retry").exists, "The retry is not marked as a same-text retry.")
        shot("t11-timing-of-retry")
        UITestWalk.closeLatestTiming()

        // AC-IOS-068: the next Assist uses the new selection too.
        UITestWalk.assist(reaching: 3)
        UITestWalk.openLatestTiming()
        XCTAssertTrue(UITestWalk.anyLabelled("gpt-6-luna · low").waitForExistence(timeout: 5), "The next Assist did not use the new selection.")
        XCTAssertTrue(UITestWalk.anyLabelStarting("Transcription after tap").exists, "A fresh answer does not read \"Transcription after tap\".")
        XCTAssertFalse(UITestWalk.anyLabelContaining("same-text retry").exists, "A fresh answer is marked as a retry.")
        UITestWalk.closeLatestTiming()
        UITestWalk.stopSession()

        // Recent comparisons list the three answers, and one opens its run details (AC-IOS-126). This
        // part only keeps pictures: it is not one of this test's criteria.
        UITestWalk.openAssistantLab()
        XCUIApplication().swipeUp()
        shot("t11-recent-comparisons")
        let comparison = UITestWalk.button("gpt-6-luna")
        if comparison.waitForExistence(timeout: 5) {
            comparison.tap()
            if UITestSupport.element("response-timing").waitForExistence(timeout: 5) {
                shot("t11-run-details")
            } else {
                UITestWalk.note("Run details did not open from Recent comparisons.")
            }
        } else {
            UITestWalk.note("Recent comparisons has no gpt-6-luna row.")
        }
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }
}
