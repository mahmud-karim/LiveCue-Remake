import XCTest

/// T1, T2 and T16: a conversation in base test mode, with Meta speech and Codex answers.
final class ConversationUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
        // A live session keeps its level meter and its elapsed time moving, which slows every query down.
        executionTimeAllowance = 600
    }

    /// T1. AC-IOS-019, 025, 026, 035, 036, 049, 050, 051, 053, 074; nothing is called `dismiss-answer`.
    @MainActor
    func testInlineAnswersTranscriptAndPinnedControls() throws {
        let app = UITestWalk.launchToHome()
        UITestWalk.startSession()

        // AC-IOS-019 and 035: the tabs are gone and the three controls can be used.
        XCTAssertTrue(UITestWalk.waitForNoTabBar(), "The tab bar is still there during a session.")
        UITestWalk.assertSessionControlsEnabled()

        // AC-IOS-025: the transcript starts collapsed, and nothing scrollable holds it.
        XCTAssertTrue(
            UITestWalk.waitForText("transcript-preview", equals: UITestWalk.segment),
            "The collapsed transcript does not show the committed segment."
        )
        XCTAssertFalse(UITestSupport.element("live-transcript").exists, "A new session shows the expanded transcript.")
        XCTAssertEqual(UITestSupport.element("expand-transcript").label, "Expand live transcript")
        shot("t1-listening")

        // AC-IOS-026: a tap shows every committed segment, a second tap hides them, and Stop stays usable.
        UITestWalk.tap("expand-transcript")
        UITestWalk.waitFor("live-transcript")
        XCTAssertTrue(
            UITestWalk.anyShowing(UITestWalk.segment).appears(within: UITestSupport.timeout),
            "The expanded transcript does not list the committed segment."
        )
        XCTAssertFalse(UITestSupport.element("transcript-preview").exists, "The preview stays beside the expanded transcript.")
        XCTAssertTrue(UITestSupport.element("end-session").isEnabled, "Stop cannot be used while the transcript is expanded.")
        shot("t1-transcript-expanded")
        UITestWalk.tap("expand-transcript")
        XCTAssertTrue(UITestWalk.waitForDisappearance(of: "live-transcript"), "The transcript did not collapse.")
        UITestWalk.waitFor("transcript-preview")
        XCTAssertTrue(UITestSupport.element("end-session").isEnabled, "Stop cannot be used after the transcript collapsed.")

        // AC-IOS-049: the answer is a card in the feed; no alert, no sheet, nothing to dismiss.
        UITestWalk.assist(reaching: 1)
        XCTAssertEqual(UITestWalk.text(of: UITestSupport.element("assistant-answer")), UITestWalk.answer)
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared with the answer.")
        XCTAssertEqual(app.sheets.count, 0, "A sheet appeared with the answer.")
        XCTAssertFalse(UITestSupport.element("dismiss-answer").exists, "The answer has a dismiss control.")
        // AC-IOS-051: the controls are back.
        UITestWalk.assertSessionControlsEnabled()
        // AC-IOS-036: the feed has more than half the screen.
        let feed = answerFeed()
        XCTAssertGreaterThan(
            feed.frame.height,
            app.frame.height / 2,
            "The answer feed is \(feed.frame.height) high on a screen of \(app.frame.height)."
        )
        shot("t1-first-answer")

        // AC-IOS-053 and 050: three in a row; one is the latest and two are earlier ones.
        UITestWalk.assist(reaching: 2)
        UITestWalk.assist(reaching: 3)
        XCTAssertEqual(UITestWalk.count(of: "assistant-answer"), 1, "Exactly one answer is the latest.")
        XCTAssertEqual(UITestWalk.count(of: "previous-assistant-answer"), 2, "Two answers are earlier ones.")
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared during the three Assists.")

        // AC-IOS-049: the newest card is the first of the feed, above every earlier one.
        let latest = UITestSupport.element("assistant-answer")
        let latestTop = latest.frame.minY
        let earlier = app.descendants(matching: .any).matching(identifier: "previous-assistant-answer").allElementsBoundByIndex
        for previous in earlier {
            XCTAssertLessThan(latestTop, previous.frame.minY, "An earlier answer is above the latest one.")
        }
        XCTAssertLessThan(latestTop, feed.frame.midY, "The latest answer is not in the upper half of the feed.")
        XCTAssertFalse(app.tabBars.firstMatch.exists, "The tab bar came back during the session.")
        UITestWalk.assertSessionControlsEnabled()
        shot("t1-three-answers")

        // AC-IOS-074: Stop returns to the tabs with Home and Start.
        UITestWalk.stopSession()
        XCTAssertTrue(app.tabBars.firstMatch.appears(within: UITestSupport.timeout), "The tabs did not come back.")
        XCTAssertTrue(UITestWalk.waitUntilSelected(UITestSupport.tabButton("Live")), "Home is not the selected tab after Stop.")
        XCTAssertTrue(UITestWalk.isOnScreen(UITestSupport.element("start-session")), "Start is not on screen after Stop.")
        shot("t1-home-after-stop")
    }

    /// T2. AC-IOS-057, 058, 061.
    @MainActor
    func testFailedAssistIsInlineAndRetryRecovers() throws {
        let app = UITestWalk.launchToHome(["-assist-fails-once"])
        UITestWalk.startSession()

        // AC-IOS-061: with no earlier request there is nothing to retry.
        XCTAssertFalse(UITestSupport.element("retry-failed-assist").exists, "A retry is offered before any Assist.")
        XCTAssertFalse(UITestSupport.element("retry-same-text").exists, "A same-text retry is offered before any Assist.")

        UITestWalk.tap("assist-button")
        UITestWalk.waitFor("assist-error")
        XCTAssertTrue(UITestWalk.waitForLabel("assist-button", equals: "Assist"), "Assist did not finish.")

        // AC-IOS-057: an inline error, the partial text labelled as incomplete, no answer, no alert, no sheet.
        XCTAssertTrue(
            UITestWalk.waitForLabel("assist-error", contains: "OpenRouter could not complete this request"),
            "The error card does not show the reason: \(UITestSupport.element("assist-error").label)"
        )
        XCTAssertTrue(
            UITestWalk.waitForText("streaming-answer", equals: UITestWalk.partialAnswer),
            "The partial answer did not stay visible."
        )
        XCTAssertTrue(UITestWalk.anyLabelled("Incomplete answer").exists, "The partial answer is not labelled \"Incomplete answer\".")
        XCTAssertFalse(UITestSupport.element("assistant-answer").exists, "A failed Assist left an answer card.")
        XCTAssertFalse(UITestSupport.element("previous-assistant-answer").exists, "A failed Assist left an earlier answer.")
        XCTAssertEqual(app.alerts.count, 0, "The failure raised an alert.")
        XCTAssertEqual(app.sheets.count, 0, "The failure raised a sheet.")
        XCTAssertTrue(UITestSupport.element("end-session").isEnabled, "Stop cannot be used after a failed Assist.")
        // AC-IOS-061: the request is remembered, so the retry is offered.
        XCTAssertTrue(UITestWalk.waitUntilEnabled(UITestSupport.element("retry-failed-assist")), "\"Retry same text\" is not enabled.")
        shot("t2-assist-error")

        // AC-IOS-058: the retry succeeds; error and partial text go, one answer is left.
        UITestWalk.tap("retry-failed-assist")
        UITestWalk.waitFor("assistant-answer")
        XCTAssertTrue(UITestWalk.waitForLabel("assist-button", equals: "Assist"), "The retry did not finish.")
        XCTAssertTrue(UITestWalk.waitForDisappearance(of: "assist-error"), "The error stayed after a successful retry.")
        XCTAssertTrue(UITestWalk.waitForDisappearance(of: "streaming-answer"), "The partial text stayed after a successful retry.")
        XCTAssertFalse(UITestWalk.anyLabelled("Incomplete answer").exists, "\"Incomplete answer\" stayed after a successful retry.")
        XCTAssertEqual(UITestWalk.count(of: "assistant-answer"), 1, "Exactly one answer exists after the retry.")
        XCTAssertEqual(UITestWalk.count(of: "previous-assistant-answer"), 0, "The retry left an earlier answer.")
        XCTAssertEqual(UITestWalk.text(of: UITestSupport.element("assistant-answer")), UITestWalk.answer)
        XCTAssertTrue(UITestWalk.waitUntilEnabled(UITestSupport.element("retry-same-text")), "The same-text retry is not enabled after an answer.")
        XCTAssertEqual(app.alerts.count, 0, "The retry raised an alert.")
        shot("t2-after-retry")

        UITestWalk.stopSession()
    }

    /// T16. AC-IOS-001, 002, 003, 029, 031, 032, 033, 034, 035, 039, 080.
    @MainActor
    func testCostPauseAndHistory() throws {
        let app = UITestWalk.launchToHome()

        // AC-IOS-001: exactly three tabs, Live selected.
        let titles = app.tabBars.buttons.allElementsBoundByIndex.map(\.label)
        XCTAssertEqual(titles.sorted(), ["History", "Live", "Settings"], "The tab bar has exactly Live, History and Settings.")
        XCTAssertTrue(UITestWalk.waitUntilSelected(UITestSupport.tabButton("Live")), "Live is not the selected tab at launch.")

        // AC-IOS-002: the pairing row, the assistant row and Start are on screen without scrolling, the
        // rows above Start and Start above the tab bar.
        let pairing = UITestWalk.waitFor("pair-pc")
        let assistant = UITestWalk.waitFor("assistant-lab")
        let start = UITestSupport.element("start-session")
        for element in [pairing, assistant, start] {
            XCTAssertTrue(UITestWalk.isOnScreen(element), "\"\(element.identifier)\" is not on screen: \(element.frame)")
        }
        XCTAssertLessThanOrEqual(pairing.frame.maxY, start.frame.minY, "The pairing row is under Start.")
        XCTAssertLessThanOrEqual(assistant.frame.maxY, start.frame.minY, "The assistant row is under Start.")
        let tabTop = app.tabBars.buttons.allElementsBoundByIndex.map { $0.frame.minY }.min() ?? 0
        XCTAssertLessThanOrEqual(start.frame.maxY, tabTop, "Start ends at \(start.frame.maxY), the tabs begin at \(tabTop).")
        XCTAssertTrue(start.isEnabled, "Start is not enabled.")

        // AC-IOS-003: Home shows no transcription cost.
        XCTAssertFalse(UITestSupport.element("live-transcription-cost").exists, "Home shows the live cost.")
        XCTAssertFalse(UITestWalk.anyLabelContaining("USD").exists, "Home shows a cost.")
        shot("t16-home")

        // AC-IOS-002, continued: both rows take a tap where they are.
        pairing.tap()
        UITestWalk.waitFor("saved-pc-state")
        UITestWalk.back()
        UITestWalk.openAssistantLab()
        UITestWalk.back()

        UITestWalk.startSession()

        // AC-IOS-031 and 032: Meta shows a cost, and it leaves zero while audio is streaming.
        UITestWalk.waitFor("live-transcription-cost")
        let movedCost = UITestWalk.anyElement(
            "identifier == %@ AND label != %@ AND label ENDSWITH %@",
            "live-transcription-cost",
            "$0.00000 USD",
            " USD"
        )
        XCTAssertTrue(movedCost.appears(within: 5), "The cost did not leave $0.00000 USD within 5 seconds.")
        // AC-IOS-029: the state reads "Listening".
        XCTAssertTrue(UITestWalk.sessionState("Listening").appears(within: UITestSupport.timeout), "The state does not read \"Listening\".")
        // AC-IOS-034 and 035: no instruction field and no tab bar on the live screen.
        XCTAssertFalse(UITestSupport.element("assistant-instruction").exists, "The live screen has the instruction field.")
        XCTAssertEqual(app.textFields.count + app.textViews.count, 0, "The live screen has a text field.")
        XCTAssertFalse(app.tabBars.firstMatch.exists, "The tab bar is there during a session.")
        shot("t16-listening")

        // AC-IOS-039 and 029: Pause shows "Paused".
        UITestWalk.tap("pause-session")
        XCTAssertTrue(UITestWalk.sessionState("Paused").appears(within: UITestSupport.timeout), "The state does not read \"Paused\".")
        XCTAssertTrue(UITestWalk.waitForLabel("pause-session", equals: "Resume"), "The pause control does not offer Resume.")
        XCTAssertFalse(app.tabBars.firstMatch.exists, "The tab bar is there while paused.")
        // AC-IOS-033: the cost stands still while the elapsed time goes on.
        let pausedCost = UITestSupport.element("live-transcription-cost").label
        XCTAssertTrue(UITestWalk.waitForElapsedToAdvance(in: "Paused"), "The elapsed time stopped while paused.")
        XCTAssertEqual(UITestSupport.element("live-transcription-cost").label, pausedCost, "The cost changed while paused.")
        shot("t16-paused")

        // AC-IOS-039: Resume shows "Listening" again, and the cost moves again.
        UITestWalk.tap("pause-session")
        XCTAssertTrue(UITestWalk.sessionState("Listening").appears(within: UITestSupport.timeout), "The state does not read \"Listening\" after Resume.")
        let resumedCost = UITestWalk.anyElement("identifier == %@ AND label != %@", "live-transcription-cost", pausedCost)
        XCTAssertTrue(resumedCost.appears(within: 10), "The cost did not move after Resume.")

        // AC-IOS-080: the cost the live screen shows at the end is the cost of the History row. The
        // session is paused first, so that the last cost can be read.
        UITestWalk.tap("pause-session")
        XCTAssertTrue(UITestWalk.sessionState("Paused").appears(within: UITestSupport.timeout), "The state does not read \"Paused\" again.")
        let finalCost = UITestSupport.element("live-transcription-cost").label
        XCTAssertTrue(finalCost.hasSuffix(" USD") && finalCost != "$0.00000 USD", "The final cost reads \"\(finalCost)\".")
        UITestWalk.stopSession()

        UITestWalk.openTab("History")
        UITestWalk.waitFor("history-transcription-cost")
        XCTAssertTrue(
            UITestWalk.waitForLabel("history-transcription-cost", contains: finalCost),
            "History shows \"\(UITestSupport.element("history-transcription-cost").label)\", the live screen ended at \(finalCost)."
        )
        shot("t16-history")
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }

    /// The scroll view of the answers.
    @MainActor
    private func answerFeed() -> XCUIElement {
        let scrollView = XCUIApplication().scrollViews.matching(identifier: "assistant-feed").firstMatch
        if scrollView.exists {
            return scrollView
        }
        UITestWalk.note("\"assistant-feed\" is not a scroll view.")
        return UITestWalk.waitFor("assistant-feed")
    }
}
