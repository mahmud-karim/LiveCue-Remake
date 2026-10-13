import XCTest

/// T5, T6, T8 and T9: the speech model on the PC, from its own screen and from Home.
final class PCModelUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
        executionTimeAllowance = 600
    }

    /// T5. AC-IOS-093 as changed (choosing a provider applies at once and stays on Transcription), 097,
    /// 099, 100, 101.
    @MainActor
    func testStartAndStopOnPCSpeechModelScreen() throws {
        let app = UITestWalk.launchToHome()

        // AC-IOS-096 and 097: with Meta, Settings has the key row and no PC speech model row.
        UITestWalk.openSettings()
        XCTAssertTrue(UITestSupport.element("meta-key-settings").exists, "Settings has no Meta API key row for Meta.")
        XCTAssertFalse(UITestWalk.button("PC speech model").exists, "Settings has a PC speech model row for Meta.")

        // AC-IOS-093: five providers.
        UITestWalk.tap("speech-provider")
        for identifier in ["provider-meta", "provider-nemotron", "provider-qwen3", "provider-voz", "provider-parakeet"] {
            XCTAssertTrue(UITestSupport.element(identifier).appears(within: UITestSupport.timeout), "\"\(identifier)\" is not listed.")
        }
        XCTAssertTrue(UITestWalk.waitUntilSelected(UITestSupport.element("provider-meta")), "Meta is not the selected provider.")
        shot("t5-transcription-meta")

        // AC-IOS-093 and 097: the choice applies at once, on this screen, and brings the PC speech model row.
        UITestWalk.selectProvider("provider-nemotron")
        XCTAssertFalse(UITestSupport.element("provider-meta").isSelected, "Meta stayed selected.")
        XCTAssertTrue(UITestWalk.button("PC speech model").appears(within: UITestSupport.timeout), "There is no PC speech model row for a PC provider.")
        XCTAssertFalse(UITestSupport.element("meta-key-settings").exists, "The Meta API key row stayed for a PC provider.")
        shot("t5-transcription-pc-provider")

        // AC-IOS-099 and 100 for Nemotron, then AC-IOS-101: the same for Qwen3.
        UITestWalk.openPCSpeechModel()
        startAndStopTheModel(named: "Nemotron 3.5 · PC", picture: "t5-pc-model-nemotron")
        UITestWalk.back()
        UITestWalk.selectProvider("provider-qwen3")
        UITestWalk.openPCSpeechModel()
        startAndStopTheModel(named: "Qwen3 1.7B · PC", picture: "t5-pc-model-qwen3")

        // Back on Settings the provider row names the choice, and the PC speech model row is there.
        UITestWalk.back()
        UITestWalk.back()
        UITestWalk.waitFor("speech-provider")
        XCTAssertTrue(UITestWalk.waitForValue("speech-provider", equals: "Qwen3 1.7B · PC"), "Settings does not name the chosen provider.")
        XCTAssertTrue(UITestWalk.button("PC speech model").appears(within: UITestSupport.timeout), "Settings has no PC speech model row.")
        XCTAssertFalse(UITestSupport.element("meta-key-settings").exists, "Settings kept the Meta API key row.")
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared.")
        shot("t5-settings-pc-provider")
    }

    /// T6. AC-IOS-014, 102.
    @MainActor
    func testStartErrorAllowsRetry() throws {
        UITestWalk.launchToHome(["-pc-model-start-error"])
        UITestWalk.openTranscription()
        UITestWalk.selectProvider("provider-nemotron")
        UITestWalk.openPCSpeechModel()

        // AC-IOS-102: the state is "Error", the PC's message is shown, and Start model can be tried again.
        UITestWalk.tapStartPCModel()
        XCTAssertTrue(UITestWalk.waitForText("pc-model-state", equals: "Error", timeout: 30), "The state does not read \"Error\".")
        XCTAssertTrue(
            UITestWalk.waitForLabel("pc-model-message", contains: "Docker Desktop"),
            "The PC's message is not shown: \(UITestSupport.element("pc-model-message").label)"
        )
        XCTAssertTrue(UITestWalk.waitUntilEnabled(UITestSupport.element("start-pc-model")), "\"Start model\" is not enabled after a failed start.")
        shot("t6-pc-model-error")

        // AC-IOS-014: Home reads "Error" and Start is disabled.
        UITestWalk.openTab("Live")
        UITestWalk.waitFor("start-session")
        XCTAssertTrue(UITestWalk.waitForLabel("home-pc-model-state", contains: "Error"), "Home does not read \"Error\".")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(UITestSupport.element("start-session")), "Start is enabled after a failed model start.")
        shot("t6-home-error")

        // The retry is taken: the start runs again and ends in the same error.
        UITestWalk.openTab("Settings")
        let start = UITestWalk.waitFor("start-pc-model")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(start), "\"Start model\" is not enabled for the retry.")
        start.tap()
        XCTAssertTrue(UITestWalk.waitForText("pc-model-state", equals: "Error", timeout: 30), "The retry did not end in \"Error\".")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(start), "\"Start model\" is not enabled after the retry.")
    }

    /// T8. AC-IOS-031, 039, 077, 081.
    @MainActor
    func testPCSpeechHidesCostInSessionAndHistory() throws {
        let app = UITestWalk.launchToHome()
        UITestWalk.openTranscription()
        UITestWalk.selectProvider("provider-nemotron")
        UITestWalk.openPCSpeechModel()
        UITestWalk.tapStartPCModel()
        XCTAssertTrue(UITestWalk.waitForText("pc-model-state", equals: "Ready", timeout: 30), "The PC model did not become ready.")

        // AC-IOS-010: a streaming provider never offers the download.
        UITestWalk.openTab("Live")
        UITestWalk.waitFor("start-session")
        XCTAssertTrue(UITestWalk.waitForText("home-pc-model-state", equals: "Ready"), "Home does not read \"Ready\".")
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home offers a model download for a PC provider.")
        UITestWalk.startSession()

        // AC-IOS-031: no cost for a PC provider.
        XCTAssertTrue(UITestWalk.sessionState("Listening").appears(within: UITestSupport.timeout), "The state does not read \"Listening\".")
        XCTAssertFalse(UITestSupport.element("live-transcription-cost").exists, "A PC provider shows a cost.")
        XCTAssertFalse(UITestWalk.anyLabelContaining("USD").exists, "A PC provider shows a cost.")
        shot("t8-live-pc-speech")

        // AC-IOS-039: Pause, Resume, Pause. Each stream is left running for more than a second before it
        // is closed, so that it has usage to show.
        XCTAssertTrue(UITestWalk.staysListening(), "The session paused by itself.")
        UITestWalk.tap("pause-session")
        XCTAssertTrue(UITestWalk.sessionState("Paused").appears(within: UITestSupport.timeout), "The state does not read \"Paused\".")
        UITestWalk.tap("pause-session")
        XCTAssertTrue(UITestWalk.sessionState("Listening").appears(within: UITestSupport.timeout), "The state does not read \"Listening\" after Resume.")
        XCTAssertTrue(UITestWalk.staysListening(), "The session paused by itself after Resume.")
        UITestWalk.tap("pause-session")
        XCTAssertTrue(UITestWalk.sessionState("Paused").appears(within: UITestSupport.timeout), "The state does not read \"Paused\" again.")
        XCTAssertFalse(UITestSupport.element("live-transcription-cost").exists, "A paused PC session shows a cost.")

        // AC-IOS-077: Stop while paused ends the session normally.
        UITestWalk.stopSession()
        XCTAssertTrue(app.tabBars.firstMatch.appears(within: UITestSupport.timeout), "The tabs did not come back.")
        XCTAssertEqual(app.alerts.count, 0, "Stopping while paused raised an alert.")

        // AC-IOS-081: History reads "… · Local", with no cost.
        UITestWalk.openTab("History")
        let local = UITestWalk.waitFor("history-local-transcription")
        let label = UITestWalk.text(of: local)
        XCTAssertTrue(label.hasSuffix("· Local"), "The History row reads \"\(label)\".")
        XCTAssertFalse(label.contains("$"), "The History row shows a currency sign: \"\(label)\".")
        XCTAssertFalse(UITestSupport.element("history-transcription-cost").exists, "The History row has a cost label.")
        XCTAssertFalse(UITestWalk.anyLabelContaining("USD").exists, "History shows a cost for a PC session.")
        shot("t8-history-local")
    }

    /// T9. AC-IOS-011, 012, 013.
    @MainActor
    func testHomeFollowsLoadingReadyAndStopped() throws {
        let app = UITestWalk.launchToHome(["-pc-model-slow-start"])
        UITestWalk.openTranscription()
        UITestWalk.selectProvider("provider-nemotron")

        // AC-IOS-011 and 013: Home has the PC speech model line; a stopped model keeps Start disabled.
        UITestWalk.openTab("Live")
        let start = UITestWalk.waitFor("start-session")
        XCTAssertTrue(UITestWalk.anyLabelled("PC speech model:").appears(within: UITestSupport.timeout), "Home has no PC speech model line.")
        XCTAssertTrue(UITestWalk.waitForLabel("home-pc-model-state", contains: "Stopped"), "Home does not read \"Stopped\".")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(start), "Start is enabled for a stopped model.")
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home offers a model download for a PC provider.")
        shot("t9-home-stopped")

        // The start takes 6 seconds here. Home is looked at while it runs.
        UITestWalk.openTab("Settings")
        UITestWalk.openPCSpeechModel()
        UITestWalk.tapStartPCModel()
        UITestSupport.tabButton("Live").tap()

        // AC-IOS-012: "Loading" with Start disabled. Both are read from one snapshot, so that they belong
        // to the same moment.
        XCTAssertTrue(UITestWalk.waitForLabel("home-pc-model-state", contains: "Loading", timeout: 6), "Home did not read \"Loading\" while the model started.")
        let loading = try app.snapshot()
        let label = UITestWalk.find("home-pc-model-state", in: loading)?.label ?? ""
        let startWhileLoading = UITestWalk.find("start-session", in: loading)
        XCTAssertNotNil(startWhileLoading, "Start is not on Home while the model loads.")
        if label.contains("Loading") {
            XCTAssertEqual(startWhileLoading?.isEnabled, false, "Start is enabled while the model loads.")
        } else {
            XCTFail("The model was no longer loading when Home was read: \"\(label)\".")
        }
        shot("t9-home-loading")

        // AC-IOS-012: without any action the label becomes "Ready" and Start becomes usable; nothing
        // left the screen, and the transcription row still takes a tap.
        XCTAssertTrue(UITestWalk.waitForText("home-pc-model-state", equals: "Ready", timeout: 30), "Home did not become \"Ready\" by itself.")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(start), "Start did not become enabled when the model was ready.")
        XCTAssertTrue(UITestWalk.isOnScreen(start), "Start is not on screen.")
        let transcription = UITestWalk.waitFor("transcription-settings")
        XCTAssertTrue(UITestWalk.isOnScreen(transcription), "The transcription row is not on screen.")
        XCTAssertLessThanOrEqual(transcription.frame.maxY, start.frame.minY, "The transcription row is under Start.")
        shot("t9-home-ready")
        transcription.tap()
        UITestWalk.waitFor("provider-nemotron")

        // AC-IOS-013: after a stop Home reads "Stopped" and Start is disabled again.
        UITestWalk.openPCSpeechModel()
        let stop = UITestWalk.waitFor("stop-pc-model")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(stop), "\"Stop model\" is not enabled for a ready model.")
        stop.tap()
        XCTAssertTrue(UITestWalk.waitForText("pc-model-state", equals: "Stopped", timeout: 30), "The model did not stop.")
        UITestWalk.back()
        UITestWalk.back()
        UITestWalk.waitFor("start-session")
        XCTAssertTrue(UITestWalk.waitForLabel("home-pc-model-state", contains: "Stopped"), "Home does not read \"Stopped\" after the stop.")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(start), "Start is enabled after the model was stopped.")
        shot("t9-home-stopped-again")
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }

    /// On the PC speech model screen: Start model until "Ready", then Stop model until "Stopped", with
    /// the two buttons checked in each state (AC-IOS-099, 100).
    @MainActor
    private func startAndStopTheModel(named title: String, picture: String) {
        let start = UITestWalk.waitFor("start-pc-model")
        let stop = UITestWalk.waitFor("stop-pc-model")
        XCTAssertTrue(UITestWalk.anyLabelled(title).exists, "The screen is not titled \"\(title)\".")

        XCTAssertTrue(UITestWalk.waitForText("pc-model-state", equals: "Stopped"), "\(title) is not reported stopped.")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(start), "\"Start model\" is not enabled for a stopped model.")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(stop), "\"Stop model\" is enabled for a stopped model.")

        start.tap()
        XCTAssertTrue(UITestWalk.waitForText("pc-model-state", equals: "Ready", timeout: 30), "\(title) did not become ready.")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(start), "\"Start model\" is enabled for a ready model.")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(stop), "\"Stop model\" is not enabled for a ready model.")
        shot(picture + "-ready")

        stop.tap()
        XCTAssertTrue(UITestWalk.waitForText("pc-model-state", equals: "Stopped", timeout: 30), "\(title) did not stop.")
        XCTAssertTrue(UITestWalk.waitUntilEnabled(start), "\"Start model\" is not enabled after the stop.")
        XCTAssertTrue(UITestWalk.waitUntilDisabled(stop), "\"Stop model\" is enabled after the stop.")
        shot(picture + "-stopped")
    }
}
