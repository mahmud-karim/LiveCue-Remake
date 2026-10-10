import XCTest

/// The app launches into its three tabs (AC-IOS-001). CI exports the screenshots from an iOS 26 simulator
/// and from an iOS 18 simulator, so the two looks of the shell can be compared by eye.
final class ShellUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    @MainActor
    func testLaunchShowsThreeTabs() throws {
        let app = UITestSupport.launch()

        // Any button titled "Live" will do for the first wait: the tab bar is looked at afterwards.
        XCTAssertTrue(
            app.buttons["Live"].firstMatch.waitForExistence(timeout: UITestSupport.launchTimeout),
            "The Live tab did not appear."
        )
        let live = UITestSupport.tabButton("Live")
        let history = UITestSupport.tabButton("History")
        let settings = UITestSupport.tabButton("Settings")
        XCTAssertTrue(live.exists, "There is no Live tab.")
        XCTAssertTrue(history.exists, "There is no History tab.")
        XCTAssertTrue(settings.exists, "There is no Settings tab.")
        XCTAssertTrue(app.tabBars.firstMatch.exists, "The tabs are not in a tab bar.")
        XCTAssertEqual(app.tabBars.buttons.count, 3, "The tab bar has exactly Live, History and Settings.")

        XCTAssertTrue(UITestSupport.waitUntilSelected(live), "Live is the selected tab at launch.")
        XCTAssertFalse(history.isSelected)
        XCTAssertFalse(settings.isSelected)
        UITestSupport.attachScreenshot(named: "shell-live", to: self)

        history.tap()
        XCTAssertTrue(UITestSupport.waitUntilSelected(history), "Tapping History selects it.")
        XCTAssertFalse(live.isSelected)
        UITestSupport.attachScreenshot(named: "shell-history", to: self)

        settings.tap()
        XCTAssertTrue(UITestSupport.waitUntilSelected(settings), "Tapping Settings selects it.")
        UITestSupport.attachScreenshot(named: "shell-settings", to: self)

        live.tap()
        XCTAssertTrue(UITestSupport.waitUntilSelected(live), "Tapping Live selects it again.")
    }
}
