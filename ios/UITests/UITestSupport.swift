import XCTest

/// What every UI test uses: the launch helper, lookup by identifier, wait helpers and the screenshot
/// attachment. Tests wait for a condition, never with a sleep.
enum UITestSupport {
    /// How long the first element may take to appear after a launch. A cold simulator and a UI-test
    /// runner that has just started can take most of a minute.
    static let launchTimeout: TimeInterval = 120
    /// How long anything else may take.
    static let timeout: TimeInterval = 15

    /// Launches the app with `-ui-testing` plus `arguments` and returns it. An app that is already
    /// running is terminated first, so this is also the relaunch.
    @MainActor @discardableResult
    static func launch(_ arguments: [String] = []) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments = ["-ui-testing"] + arguments
        app.launch()
        return app
    }

    /// The element with this accessibility identifier, whatever its type: a chip or a banner is not
    /// always a static text.
    @MainActor
    static func element(_ identifier: String) -> XCUIElement {
        XCUIApplication().descendants(matching: .any).matching(identifier: identifier).firstMatch
    }

    /// Waits for the element with this identifier and fails the test when it does not appear.
    @MainActor @discardableResult
    static func waitFor(
        _ identifier: String,
        timeout: TimeInterval = 15,
        file: StaticString = #filePath,
        line: UInt = #line
    ) -> XCUIElement {
        let found = element(identifier)
        XCTAssertTrue(found.waitForExistence(timeout: timeout), "\"\(identifier)\" did not appear.", file: file, line: line)
        return found
    }

    /// True once the element with this identifier has this label.
    @MainActor
    static func waitForLabel(_ identifier: String, equals label: String, timeout: TimeInterval = 15) -> Bool {
        let predicate = NSPredicate(format: "identifier == %@ AND label == %@", identifier, label)
        return XCUIApplication().descendants(matching: .any).matching(predicate).firstMatch.waitForExistence(timeout: timeout)
    }

    /// True once the element with this identifier has a label that contains this text.
    @MainActor
    static func waitForLabel(_ identifier: String, contains text: String, timeout: TimeInterval = 15) -> Bool {
        let predicate = NSPredicate(format: "identifier == %@ AND label CONTAINS %@", identifier, text)
        return XCUIApplication().descendants(matching: .any).matching(predicate).firstMatch.waitForExistence(timeout: timeout)
    }

    /// True once no element has this identifier.
    @MainActor
    static func waitForDisappearance(of identifier: String, timeout: TimeInterval = 15) -> Bool {
        wait(for: element(identifier), toSatisfy: "exists == false", timeout: timeout)
    }

    /// True once the element is the selected one of its group: a tab, a choice row.
    @MainActor
    static func waitUntilSelected(_ element: XCUIElement, timeout: TimeInterval = 15) -> Bool {
        wait(for: element, toSatisfy: "isSelected == true", timeout: timeout)
    }

    /// The tab bar button with this title.
    @MainActor
    static func tabButton(_ title: String) -> XCUIElement {
        let app = XCUIApplication()
        let inTabBar = app.tabBars.buttons[title]
        if inTabBar.exists {
            return inTabBar
        }
        return app.buttons[title].firstMatch
    }

    /// Keeps a screenshot of the whole screen with the test's result, under `name`. CI exports it.
    @MainActor
    static func attachScreenshot(named name: String, to testCase: XCTestCase) {
        let attachment = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        testCase.add(attachment)
    }

    /// Waits until `format`, a predicate over the element, holds.
    @MainActor
    private static func wait(for element: XCUIElement, toSatisfy format: String, timeout: TimeInterval) -> Bool {
        let expectation = XCTNSPredicateExpectation(predicate: NSPredicate(format: format), object: element)
        return XCTWaiter().wait(for: [expectation], timeout: timeout) == .completed
    }
}
