import XCTest

/// T3 and T4: the Meta key, the OpenRouter key and model, and conversations that need no PC.
final class CloudOnlyUITests: XCTestCase {
    /// Made-up keys for the fixture services. They open nothing.
    private static let metaKey = "ui-test-meta-key"
    private static let openRouterKey = "ui-test-openrouter-key"

    override func setUpWithError() throws {
        continueAfterFailure = false
        // Two launches, two keys typed and a conversation.
        executionTimeAllowance = 600
    }

    /// T3. AC-IOS-005, 007, 010, 016, 096, 107, 108, 110, 111, 112, 053.
    @MainActor
    func testMetaKeyAndCloudOnlyConversation() throws {
        UITestWalk.launchToHome(["-meta-no-key", "-pc-offline", "-openrouter-ui-reset"])

        // No Meta key yet. AC-IOS-010: a streaming provider never offers the download.
        XCTAssertTrue(UITestWalk.waitForText("home-meta-state", equals: "Add API key"), "Home does not ask for the Meta key.")
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home offers a model download for Meta.")
        // The PC is paired and switched off, and Codex answers need it.
        XCTAssertTrue(UITestWalk.anyLabelled("PC reconnecting").waitForExistence(timeout: UITestSupport.timeout), "Home does not read \"PC reconnecting\".")
        shot("t3-home-no-meta-key")

        // AC-IOS-016: Start without a key raises the alert, and no session starts.
        UITestWalk.tap("start-session")
        var alert = UITestWalk.waitForAlert(containing: "Meta API key")
        shot("t3-start-needs-meta-key")
        UITestWalk.close(alert)
        XCTAssertFalse(UITestSupport.element("assist-button").exists, "A session started without a Meta key.")
        XCTAssertTrue(UITestSupport.element("start-session").exists, "Home is gone after the alert.")

        // AC-IOS-096 and 107: the row reads "Required" and the screen "No key saved".
        UITestWalk.openSettings()
        XCTAssertTrue(UITestWalk.waitForValue("meta-key-settings", equals: "Required"), "The Meta API key row does not read \"Required\".")
        UITestWalk.tap("meta-key-settings")
        UITestSupport.waitFor("meta-key")
        XCTAssertTrue(UITestSupport.waitForLabel("meta-key-state", equals: "No key saved"), "The key state does not read \"No key saved\".")
        XCTAssertFalse(UITestSupport.element("verify-meta-key").exists, "A key can be tested before one is saved.")
        shot("t3-meta-key-empty")

        // AC-IOS-107 and 108: saving shows "Key saved", empties the field, and the key is never readable.
        UITestWalk.saveKey(CloudOnlyUITests.metaKey, field: "meta-key", save: "save-meta-key")
        XCTAssertTrue(UITestSupport.waitForLabel("meta-key-state", equals: "Key saved"), "The key state does not read \"Key saved\".")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(UITestSupport.element("save-meta-key")), "The field was not emptied by the save.")
        XCTAssertTrue(UITestWalk.nothingShows(CloudOnlyUITests.metaKey), "The saved key can be read on screen.")

        // AC-IOS-110: the test says that no microphone audio was sent.
        UITestWalk.tap("verify-meta-key")
        XCTAssertTrue(
            UITestSupport.waitForLabel("meta-key-message", contains: "No microphone audio"),
            "The connection test did not report: \(UITestSupport.element("meta-key-message").label)"
        )
        shot("t3-meta-key-tested")
        UITestWalk.back()
        XCTAssertTrue(UITestWalk.waitForValue("meta-key-settings", equals: "Saved on iPhone"), "The Meta API key row does not read \"Saved on iPhone\".")

        // OpenRouter answers, so that nothing needs the PC.
        UITestWalk.openAssistantLab()
        UITestWalk.tap("assistant-provider-openrouter")
        UITestWalk.saveKey(CloudOnlyUITests.openRouterKey, field: "openrouter-key", save: "save-openrouter-key")
        XCTAssertTrue(UITestSupport.waitForLabel("openrouter-key-state", equals: "Key saved"), "The OpenRouter key was not saved.")
        UITestWalk.tap("openrouter-model-picker")
        UITestWalk.tap("choose-test/direct")
        XCTAssertTrue(UITestWalk.waitForText("selected-assistant-model", equals: "test/direct"), "The OpenRouter model was not selected.")

        // AC-IOS-005 and 007: all cloud, whatever the PC does.
        UITestWalk.openTab("Live")
        UITestSupport.waitFor("start-session")
        XCTAssertTrue(UITestWalk.anyLabelled("Cloud direct · no PC").waitForExistence(timeout: UITestSupport.timeout), "Home does not read \"Cloud direct · no PC\".")
        XCTAssertTrue(UITestWalk.waitForText("home-meta-state", equals: "Key saved · direct"), "Home does not read \"Key saved · direct\".")
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home offers a model download for Meta.")
        shot("t3-home-cloud-direct")

        // AC-IOS-111: the key is still there after a relaunch that does not seed one.
        let app = UITestWalk.launchToHome(["-meta-key-persistence", "-openrouter-persistence-test", "-pc-offline"])
        XCTAssertTrue(UITestWalk.waitForText("home-meta-state", equals: "Key saved · direct"), "The Meta key did not survive the relaunch.")
        XCTAssertTrue(UITestWalk.anyLabelled("Cloud direct · no PC").waitForExistence(timeout: UITestSupport.timeout), "The answer service did not survive the relaunch.")
        XCTAssertTrue(UITestWalk.nothingShows(CloudOnlyUITests.metaKey), "The saved key can be read on Home.")

        // AC-IOS-053: a conversation with three answers, with the PC switched off.
        UITestWalk.startSession()
        XCTAssertTrue(UITestWalk.sessionState("Listening").waitForExistence(timeout: UITestSupport.timeout), "The state does not read \"Listening\".")
        UITestWalk.assist(reaching: 1)
        UITestWalk.assist(reaching: 2)
        UITestWalk.assist(reaching: 3)
        XCTAssertEqual(UITestWalk.count(of: "assistant-answer"), 1, "Exactly one answer is the latest.")
        XCTAssertEqual(UITestWalk.count(of: "previous-assistant-answer"), 2, "Two answers are earlier ones.")
        XCTAssertFalse(UITestSupport.element("assist-error").exists, "An Assist failed without the PC.")
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared during the conversation.")
        shot("t3-cloud-conversation")
        UITestWalk.stopSession()

        // AC-IOS-112: removing the key shows "No key saved", and Start asks for the key again.
        UITestWalk.openSettings()
        XCTAssertTrue(UITestWalk.waitForValue("meta-key-settings", equals: "Saved on iPhone"), "The Meta API key row does not read \"Saved on iPhone\".")
        UITestWalk.tap("meta-key-settings")
        UITestWalk.tap("remove-meta-key")
        XCTAssertTrue(UITestSupport.waitForLabel("meta-key-state", equals: "No key saved"), "The key state does not read \"No key saved\" after the removal.")
        shot("t3-meta-key-removed")
        UITestWalk.openTab("Live")
        UITestWalk.tap("start-session")
        alert = UITestWalk.waitForAlert(containing: "Meta API key")
        UITestWalk.close(alert)
        XCTAssertFalse(UITestSupport.element("assist-button").exists, "A session started after the key was removed.")
    }

    /// T4. AC-IOS-005, 056, 118 to 123, 053, 079; and the half of 061 that needs no earlier request.
    @MainActor
    func testOpenRouterKeyModelAndThreeAssists() throws {
        var app = UITestWalk.launchToHome(["-openrouter-ui-reset", "-pc-offline", "-pc-catalog-slow"])
        XCTAssertTrue(UITestWalk.anyLabelled("PC reconnecting").waitForExistence(timeout: UITestSupport.timeout), "Home does not read \"PC reconnecting\".")

        // AC-IOS-123: the service is switched while the PC's catalogue, which takes 8 seconds here, is
        // still loading.
        UITestWalk.openAssistantLab()
        let labAppeared = Date()
        XCTAssertFalse(UITestSupport.element("choose-gpt-5.6-sol").exists, "The slow PC catalogue is there at once.")
        UITestWalk.tap("assistant-provider-openrouter")

        // AC-IOS-118: the key section appears.
        UITestSupport.waitFor("openrouter-key")
        XCTAssertTrue(UITestSupport.waitForLabel("openrouter-key-state", equals: "No key saved"), "The key state does not read \"No key saved\".")
        shot("t4-assistant-openrouter-no-key")

        // AC-IOS-123: OpenRouter's catalogue is there within 5 seconds, and the PC's never replaces it.
        UITestWalk.tap("openrouter-model-picker")
        XCTAssertTrue(
            UITestSupport.element("choose-test/direct").waitForExistence(timeout: 5),
            "OpenRouter's catalogue did not appear within 5 seconds of the switch."
        )
        shot("t4-openrouter-models")
        let untilThePCWouldAnswer = max(1, 10 - Date().timeIntervalSince(labAppeared))
        XCTAssertFalse(
            UITestSupport.element("choose-gpt-6-astra").waitForExistence(timeout: untilThePCWouldAnswer),
            "The PC's catalogue replaced OpenRouter's."
        )
        XCTAssertTrue(UITestSupport.element("choose-test/direct").exists, "OpenRouter's catalogue went away.")

        // AC-IOS-120: the row shows id and prices (the fixture model has none, which reads "Unknown"),
        // a search by id keeps it, a search that matches nothing hides it.
        XCTAssertTrue(UITestWalk.anyLabelContaining("Input Unknown · output Unknown").exists, "The model row shows no prices.")
        let search = app.searchFields.firstMatch
        XCTAssertTrue(search.waitForExistence(timeout: UITestSupport.timeout), "The model picker has no search field.")
        search.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)).tap()
        _ = app.keyboards.firstMatch.waitForExistence(timeout: 5)
        app.typeText("t/d")
        XCTAssertTrue(UITestSupport.element("choose-test/direct").waitForExistence(timeout: 5), "A search by id hides the model.")
        app.typeText("zz")
        XCTAssertTrue(UITestSupport.waitForDisappearance(of: "choose-test/direct"), "A search that matches nothing still lists the model.")
        XCTAssertTrue(UITestWalk.anyLabelContaining("No models").waitForExistence(timeout: 5), "An empty result is not explained.")
        shot("t4-openrouter-search-empty")
        app.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 2))
        // Choosing closes the picker and shows the id as selected.
        UITestWalk.tap("choose-test/direct")
        XCTAssertTrue(UITestWalk.waitForText("selected-assistant-model", equals: "test/direct"), "The chosen model is not shown as selected.")
        XCTAssertTrue(UITestSupport.waitForDisappearance(of: "choose-test/direct"), "The picker stayed open.")
        UITestWalk.back()

        // AC-IOS-005: Meta speech with OpenRouter answers reads "Cloud direct · no PC".
        XCTAssertTrue(UITestWalk.anyLabelled("Cloud direct · no PC").waitForExistence(timeout: UITestSupport.timeout), "Home does not read \"Cloud direct · no PC\".")

        // AC-IOS-056: without a key the Assist fails inline, with no alert. AC-IOS-061: nothing was
        // sent, so there is nothing to retry.
        UITestWalk.startSession()
        UITestWalk.tap("assist-button")
        UITestSupport.waitFor("assist-error")
        XCTAssertTrue(
            UITestSupport.waitForLabel("assist-error", contains: "OpenRouter API key"),
            "The error card does not ask for the key: \(UITestSupport.element("assist-error").label)"
        )
        XCTAssertEqual(app.alerts.count, 0, "The missing key raised an alert.")
        XCTAssertFalse(UITestSupport.element("assistant-answer").exists, "An answer arrived without a key.")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(UITestSupport.element("retry-failed-assist")), "A retry is offered although nothing was sent.")
        shot("t4-assist-needs-key")
        UITestWalk.stopSession()

        // AC-IOS-118: saving shows "Key saved"; the key is never readable.
        UITestWalk.openAssistantLab()
        UITestWalk.saveKey(CloudOnlyUITests.openRouterKey, field: "openrouter-key", save: "save-openrouter-key")
        XCTAssertTrue(UITestSupport.waitForLabel("openrouter-key-state", equals: "Key saved"), "The key state does not read \"Key saved\".")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(UITestSupport.element("save-openrouter-key")), "The field was not emptied by the save.")
        XCTAssertTrue(UITestWalk.nothingShows(CloudOnlyUITests.openRouterKey), "The saved key can be read on screen.")
        // AC-IOS-119: the check says that no inference request was made.
        UITestWalk.tap("verify-openrouter-key")
        XCTAssertTrue(
            UITestWalk.anyLabelContaining("No inference request was made").waitForExistence(timeout: UITestSupport.timeout),
            "The key check did not report its result."
        )
        shot("t4-assistant-openrouter")
        UITestWalk.back()

        // AC-IOS-005: on-device speech with OpenRouter answers reads "OpenRouter direct".
        UITestWalk.openTranscription()
        UITestWalk.selectProvider("provider-voz")
        UITestWalk.openTab("Live")
        UITestSupport.waitFor("download-model")
        XCTAssertTrue(UITestWalk.anyLabelled("OpenRouter direct").waitForExistence(timeout: UITestSupport.timeout), "Home does not read \"OpenRouter direct\".")
        shot("t4-home-openrouter-direct")

        // AC-IOS-121: key and model are still there after a relaunch.
        app = UITestWalk.launchToHome(["-openrouter-persistence-test", "-pc-offline"])
        XCTAssertTrue(UITestWalk.anyLabelled("Cloud direct · no PC").waitForExistence(timeout: UITestSupport.timeout), "The answer service did not survive the relaunch.")
        UITestWalk.openAssistantLab()
        XCTAssertTrue(
            UITestSupport.waitUntilSelected(UITestSupport.element("assistant-provider-openrouter")),
            "OpenRouter is not the selected service after the relaunch."
        )
        XCTAssertTrue(UITestWalk.waitForText("selected-assistant-model", equals: "test/direct"), "The chosen model did not survive the relaunch.")
        XCTAssertTrue(UITestSupport.waitForLabel("openrouter-key-state", equals: "Key saved"), "The key did not survive the relaunch.")
        XCTAssertTrue(UITestWalk.nothingShows(CloudOnlyUITests.openRouterKey), "The saved key can be read after the relaunch.")
        UITestWalk.back()

        // AC-IOS-053: three Assists in a row succeed without the PC.
        UITestWalk.startSession()
        UITestWalk.assist(reaching: 1)
        UITestWalk.assist(reaching: 2)
        UITestWalk.assist(reaching: 3)
        XCTAssertEqual(UITestWalk.count(of: "assistant-answer"), 1, "Exactly one answer is the latest.")
        XCTAssertEqual(UITestWalk.count(of: "previous-assistant-answer"), 2, "Two answers are earlier ones.")
        XCTAssertFalse(UITestSupport.element("assist-error").exists, "An Assist failed without the PC.")
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared during the conversation.")
        shot("t4-three-openrouter-answers")
        UITestWalk.stopSession()

        // AC-IOS-079: no summary is asked for, so the conversation keeps the title its first segment
        // gave it. (The fixture summary would rename it at once.)
        UITestWalk.openTab("History")
        XCTAssertTrue(
            UITestWalk.historyRow("1 transcript segments · 3 assists").waitForExistence(timeout: UITestSupport.timeout),
            "History does not list the conversation."
        )
        XCTAssertTrue(UITestWalk.anyLabelContaining("What is the main advantage of local").exists, "The conversation lost its own title.")
        XCTAssertFalse(UITestWalk.anyLabelContaining(UITestWalk.summaryTitle).exists, "A summary renamed a conversation with OpenRouter answers.")
        shot("t4-history-without-summary")

        // AC-IOS-122: removing the key shows "No key saved".
        UITestWalk.openTab("Live")
        UITestWalk.openAssistantLab()
        // The button is low on the screen; the scroll brings it clear of the tab bar.
        app.swipeUp()
        UITestWalk.tap("remove-openrouter-key")
        XCTAssertTrue(UITestSupport.waitForLabel("openrouter-key-state", equals: "No key saved"), "The key state does not read \"No key saved\" after the removal.")
        shot("t4-openrouter-key-removed")
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }
}
