import XCTest

/// Launches the app, checks the placeholder screen and keeps a screenshot of it.
///
/// CI exports the screenshot from the result bundle, once from an iOS 26 simulator and once from an
/// iOS 18 simulator, so the two glass paths can be compared by eye.
final class PlaceholderUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    @MainActor
    func testPlaceholderShowsTheNameAndAWorkingGlassButton() throws {
        let app = XCUIApplication()
        app.launchArguments = ["-ui-testing"]
        app.launch()

        // The title carries the header trait, so it is looked up by identifier alone, whatever its type.
        let title = app.descendants(matching: .any).matching(identifier: "placeholder.title").firstMatch
        XCTAssertTrue(title.waitForExistence(timeout: 60), "The placeholder title did not appear.")
        XCTAssertEqual(title.label, "LiveCue")

        XCTAssertEqual(text("placeholder.version", in: app).label, "Version 1.0.0 (1)")

        // The app must take the native path exactly when the system has Liquid Glass.
        // A UI test runs inside the simulator, so this is the simulator's iOS version.
        let systemHasLiquidGlass = ProcessInfo.processInfo.operatingSystemVersion.majorVersion >= 26
        XCTAssertEqual(
            text("placeholder.glassPath", in: app).label,
            systemHasLiquidGlass ? "Native Liquid Glass" : "Material fallback"
        )

        let button = app.buttons.matching(identifier: "placeholder.glassButton").firstMatch
        XCTAssertTrue(button.waitForExistence(timeout: 10), "The glass button did not appear.")
        XCTAssertEqual(button.label, "Tap the glass")
        XCTAssertTrue(button.isHittable, "The glass button cannot be tapped.")
        XCTAssertEqual(text("placeholder.tapCount", in: app).label, "Taps: 0")

        // Fails with "add failed (-34018)" when the test build was not signed; see ios.yml.
        let keychain = text("placeholder.keychain", in: app)
        XCTAssertTrue(keychain.waitForExistence(timeout: 10), "The Keychain check did not report.")
        XCTAssertEqual(keychain.label, "Keychain: available")

        let screenshot = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        screenshot.name = "placeholder"
        screenshot.lifetime = .keepAlways
        add(screenshot)

        button.tap()

        let countedTap = NSPredicate(format: "identifier == %@ AND label == %@", "placeholder.tapCount", "Taps: 1")
        XCTAssertTrue(
            app.staticTexts.matching(countedTap).firstMatch.waitForExistence(timeout: 10),
            "Tapping the glass button did not raise the tap count."
        )
    }

    @MainActor
    private func text(_ identifier: String, in app: XCUIApplication) -> XCUIElement {
        app.staticTexts.matching(identifier: identifier).firstMatch
    }
}
