import XCTest

/// History: a conversation is listed with its counts, opens its detail, and is deleted only after the
/// confirmation.
final class HistoryUITests: XCTestCase {
    private static let counts = "1 transcript segments · 1 assists"

    override func setUpWithError() throws {
        continueAfterFailure = false
        executionTimeAllowance = 600
    }

    /// AC-IOS-083 as changed (the swipe asks first: Cancel keeps the row, Delete removes it), 075.
    @MainActor
    func testDeleteAsksForConfirmation() throws {
        let app = UITestWalk.launchToHome()

        // AC-IOS-082: nothing yet.
        UITestWalk.openTab("History")
        XCTAssertTrue(UITestWalk.anyLabelContaining("No conversations yet").waitForExistence(timeout: UITestSupport.timeout), "An empty History does not say so.")
        shot("history-empty")

        // One conversation with one answer.
        UITestWalk.openTab("Live")
        UITestWalk.startSession()
        UITestWalk.assist(reaching: 1)
        UITestWalk.stopSession()

        // AC-IOS-075: the row reads "1 transcript segments · 1 assists".
        UITestWalk.openTab("History")
        let row = UITestWalk.historyRow(HistoryUITests.counts)
        XCTAssertTrue(row.waitForExistence(timeout: UITestSupport.timeout), "History does not read \"\(HistoryUITests.counts)\".")
        XCTAssertFalse(UITestWalk.anyLabelContaining("No conversations yet").exists, "History still reads empty.")
        UITestSupport.waitFor("history-transcription-cost")
        // The fixture summary of a Codex conversation renames it. Not one of this test's criteria.
        if !UITestWalk.anyLabelContaining(UITestWalk.summaryTitle).waitForExistence(timeout: 5) {
            UITestWalk.note("The fixture summary did not rename the conversation in History.")
        }
        shot("history-one-conversation")

        // The row opens the conversation's detail.
        row.tap()
        XCTAssertTrue(UITestWalk.anyLabelContaining("Transcription usage").waitForExistence(timeout: UITestSupport.timeout), "The row did not open the conversation.")
        XCTAssertTrue(UITestWalk.anyShowing(UITestWalk.answer).exists, "The detail does not show the answer.")
        shot("session-detail")
        app.swipeUp()
        shot("session-detail-lower")
        UITestWalk.back()
        XCTAssertTrue(row.waitForExistence(timeout: UITestSupport.timeout), "History lost the conversation.")

        // AC-IOS-083: the swipe offers Delete, Delete asks, and Cancel keeps the row.
        revealDelete(on: row).tap()
        var alert = app.alerts["Delete this conversation?"]
        XCTAssertTrue(alert.waitForExistence(timeout: UITestSupport.timeout), "Delete did not ask for confirmation.")
        XCTAssertTrue(row.exists, "The row went away before the confirmation.")
        shot("history-delete-confirmation")
        UITestWalk.close(alert, with: "Cancel")
        XCTAssertTrue(row.waitForExistence(timeout: UITestSupport.timeout), "Cancel removed the conversation.")
        XCTAssertFalse(UITestWalk.anyLabelContaining("No conversations yet").exists, "Cancel emptied History.")

        // AC-IOS-083: confirming removes it.
        revealDelete(on: row).tap()
        alert = app.alerts["Delete this conversation?"]
        XCTAssertTrue(alert.waitForExistence(timeout: UITestSupport.timeout), "Delete did not ask for confirmation the second time.")
        UITestWalk.close(alert, with: "Delete")
        XCTAssertTrue(UITestWalk.waitUntilGone(row), "The conversation is still listed after the confirmed delete.")
        XCTAssertTrue(UITestWalk.anyLabelContaining("No conversations yet").waitForExistence(timeout: UITestSupport.timeout), "History is not empty after the delete.")
        shot("history-after-delete")
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }

    /// The Delete action of the row's swipe. The row is swiped unless the action is already showing.
    @MainActor
    private func revealDelete(on row: XCUIElement) -> XCUIElement {
        let delete = XCUIApplication().buttons["Delete"].firstMatch
        if !delete.exists {
            row.swipeLeft()
        }
        if !delete.waitForExistence(timeout: 5) {
            // A second, slower swipe from inside the row, for a list that did not take the first.
            UITestWalk.note("The first swipe did not show Delete.")
            let from = row.coordinate(withNormalizedOffset: CGVector(dx: 0.9, dy: 0.5))
            let to = row.coordinate(withNormalizedOffset: CGVector(dx: 0.1, dy: 0.5))
            from.press(forDuration: 0.1, thenDragTo: to)
        }
        XCTAssertTrue(delete.waitForExistence(timeout: UITestSupport.timeout), "The swipe did not offer Delete.")
        return delete
    }
}
