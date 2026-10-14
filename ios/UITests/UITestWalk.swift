import XCTest

/// Steps, lookups and waits that the UI tests of section 6.3 share, on top of `UITestSupport`.
///
/// The tests cannot read the app's types, so the fixture texts they compare with are repeated here.
/// Predicates over every element compare the label only, or compare a value for equality: a value is not
/// always text.
enum UITestWalk {
    // MARK: - Fixture texts (architecture 3.2.10)

    static let segment = "What is the main advantage of local transcription?"
    static let partialAnswer = "Your audio stays on the iPhone,"
    static let answer = "Your audio stays on the iPhone, so nothing you say leaves the device to be transcribed."
    static let pairingAddress = "https://pc.example.test:11443/"
    /// The title the fixture summary gives a conversation that ended with Codex answers.
    static let summaryTitle = "Local transcription"

    @MainActor static var app: XCUIApplication {
        XCUIApplication()
    }

    /// A line in the test log for something that is worth knowing after a run and is not a failure.
    static func note(_ text: String) {
        print("WP12-NOTE: \(text)")
    }

    // MARK: - Lookup

    /// The first element of any type that satisfies the predicate.
    @MainActor
    static func anyElement(_ format: String, _ arguments: String...) -> XCUIElement {
        app.descendants(matching: .any).matching(NSPredicate(format: format, argumentArray: arguments)).firstMatch
    }

    /// The first element that shows exactly this text. A selectable text may carry it as its value.
    @MainActor
    static func anyShowing(_ text: String) -> XCUIElement {
        anyElement("label == %@ OR value == %@", text, text)
    }

    @MainActor
    static func anyLabelled(_ label: String) -> XCUIElement {
        anyElement("label == %@", label)
    }

    @MainActor
    static func anyLabelContaining(_ text: String) -> XCUIElement {
        anyElement("label CONTAINS %@", text)
    }

    @MainActor
    static func anyLabelStarting(_ text: String) -> XCUIElement {
        anyElement("label BEGINSWITH %@", text)
    }

    @MainActor
    static func button(_ label: String) -> XCUIElement {
        app.buttons[label].firstMatch
    }

    /// What the element shows: its label, or its value when it has no label.
    @MainActor
    static func text(of element: XCUIElement) -> String {
        let label = element.label
        if !label.isEmpty {
            return label
        }
        return element.value as? String ?? ""
    }

    /// How many elements carry this identifier, counted by the places they are in.
    @MainActor
    static func count(of identifier: String) -> Int {
        let matches = app.descendants(matching: .any).matching(identifier: identifier).allElementsBoundByIndex
        var frames: [CGRect] = []
        for match in matches {
            let frame = match.frame
            if !frames.contains(where: { $0.equalTo(frame) }) {
                frames.append(frame)
            }
        }
        return frames.count
    }

    /// True when the element lies inside the screen.
    @MainActor
    static func isOnScreen(_ element: XCUIElement) -> Bool {
        let frame = element.frame
        return !frame.isEmpty && app.frame.contains(frame)
    }

    // MARK: - Waits
    //
    // Every wait here looks once before it waits. XCTest's own waits check for the first time after a
    // second, and a walk through the app is hundreds of waits for something that is already there.

    /// Waits for the element with this identifier and fails the test when it does not appear.
    @MainActor @discardableResult
    static func waitFor(
        _ identifier: String,
        timeout: TimeInterval = 15,
        file: StaticString = #filePath,
        line: UInt = #line
    ) -> XCUIElement {
        let found = UITestSupport.element(identifier)
        XCTAssertTrue(found.appears(within: timeout), "\"\(identifier)\" did not appear.", file: file, line: line)
        return found
    }

    /// True once the element with this identifier has this label.
    @MainActor
    static func waitForLabel(_ identifier: String, equals label: String, timeout: TimeInterval = 15) -> Bool {
        anyElement("identifier == %@ AND label == %@", identifier, label).appears(within: timeout)
    }

    /// True once the element with this identifier has a label that contains this text.
    @MainActor
    static func waitForLabel(_ identifier: String, contains text: String, timeout: TimeInterval = 15) -> Bool {
        anyElement("identifier == %@ AND label CONTAINS %@", identifier, text).appears(within: timeout)
    }

    /// True once no element has this identifier.
    @MainActor
    static func waitForDisappearance(of identifier: String, timeout: TimeInterval = 15) -> Bool {
        waitUntilGone(UITestSupport.element(identifier), timeout: timeout)
    }

    /// True once the element is the selected one of its group: a tab, a choice row.
    @MainActor
    static func waitUntilSelected(_ element: XCUIElement, timeout: TimeInterval = 15) -> Bool {
        if element.exists && element.isSelected {
            return true
        }
        return wait(for: element, toSatisfy: "exists == true AND isSelected == true", timeout: timeout)
    }

    /// Waits until `format`, a predicate over the element, holds.
    @MainActor
    static func wait(for element: XCUIElement, toSatisfy format: String, timeout: TimeInterval = 15) -> Bool {
        let expectation = XCTNSPredicateExpectation(predicate: NSPredicate(format: format), object: element)
        return XCTWaiter().wait(for: [expectation], timeout: timeout) == .completed
    }

    @MainActor
    static func waitUntilEnabled(_ element: XCUIElement, timeout: TimeInterval = 15) -> Bool {
        if element.exists && element.isEnabled {
            return true
        }
        return wait(for: element, toSatisfy: "exists == true AND isEnabled == true", timeout: timeout)
    }

    @MainActor
    static func waitUntilDisabled(_ element: XCUIElement, timeout: TimeInterval = 15) -> Bool {
        if element.exists && !element.isEnabled {
            return true
        }
        return wait(for: element, toSatisfy: "exists == true AND isEnabled == false", timeout: timeout)
    }

    @MainActor
    static func waitUntilGone(_ element: XCUIElement, timeout: TimeInterval = 15) -> Bool {
        if !element.exists {
            return true
        }
        return wait(for: element, toSatisfy: "exists == false", timeout: timeout)
    }

    /// True once the element with this identifier shows exactly this text, as its label or as its value.
    @MainActor
    static func waitForText(_ identifier: String, equals text: String, timeout: TimeInterval = 15) -> Bool {
        anyElement("identifier == %@ AND (label == %@ OR value == %@)", identifier, text, text)
            .appears(within: timeout)
    }

    /// True once the element with this identifier has this accessibility value. A row that shows its
    /// value as part of its label is accepted too.
    @MainActor
    static func waitForValue(_ identifier: String, equals value: String, timeout: TimeInterval = 15) -> Bool {
        anyElement("identifier == %@ AND (value == %@ OR label CONTAINS %@)", identifier, value, value)
            .appears(within: timeout)
    }

    /// True once no tab bar is on screen.
    @MainActor
    static func waitForNoTabBar(timeout: TimeInterval = 15) -> Bool {
        waitUntilGone(app.tabBars.firstMatch, timeout: timeout)
    }

    // MARK: - Launch and navigation

    /// Launches with `-ui-testing` plus `arguments` and waits for Home.
    @MainActor @discardableResult
    static func launchToHome(
        _ arguments: [String] = [],
        file: StaticString = #filePath,
        line: UInt = #line
    ) -> XCUIApplication {
        let app = UITestSupport.launch(arguments)
        waitFor("start-session", timeout: UITestSupport.launchTimeout, file: file, line: line)
        return app
    }

    /// Waits for the element with this identifier and taps it.
    @MainActor
    static func tap(_ identifier: String, file: StaticString = #filePath, line: UInt = #line) {
        waitFor(identifier, file: file, line: line).tap()
    }

    /// Waits for the button with this label and taps it.
    @MainActor
    static func tapButton(_ label: String, file: StaticString = #filePath, line: UInt = #line) {
        let found = button(label)
        XCTAssertTrue(found.appears(within: UITestSupport.timeout), "There is no \"\(label)\" button.", file: file, line: line)
        found.tap()
    }

    @MainActor
    static func openTab(_ title: String, file: StaticString = #filePath, line: UInt = #line) {
        let anyButton = app.buttons[title].firstMatch
        XCTAssertTrue(anyButton.appears(within: UITestSupport.timeout), "There is no \(title) tab.", file: file, line: line)
        UITestSupport.tabButton(title).tap()
    }

    /// The back button of the navigation bar.
    @MainActor
    static func back(file: StaticString = #filePath, line: UInt = #line) {
        let button = app.navigationBars.buttons.firstMatch
        XCTAssertTrue(button.appears(within: UITestSupport.timeout), "There is no back button.", file: file, line: line)
        button.tap()
    }

    @MainActor
    static func openSettings(file: StaticString = #filePath, line: UInt = #line) {
        openTab("Settings", file: file, line: line)
        waitFor("speech-provider", file: file, line: line)
    }

    /// Settings, then the Transcription screen with its seven providers.
    @MainActor
    static func openTranscription(file: StaticString = #filePath, line: UInt = #line) {
        openSettings(file: file, line: line)
        tap("speech-provider", file: file, line: line)
        waitFor("provider-meta", file: file, line: line)
    }

    /// Chooses a provider on the Transcription screen and waits until its row is the selected one.
    @MainActor
    static func selectProvider(_ identifier: String, file: StaticString = #filePath, line: UInt = #line) {
        tap(identifier, file: file, line: line)
        XCTAssertTrue(
            waitUntilSelected(UITestSupport.element(identifier)),
            "\"\(identifier)\" did not become the selected provider.",
            file: file,
            line: line
        )
    }

    /// From Home: Assistant Lab.
    @MainActor
    static func openAssistantLab(file: StaticString = #filePath, line: UInt = #line) {
        tap("assistant-lab", file: file, line: line)
        waitFor("assistant-provider", file: file, line: line)
    }

    // MARK: - PC speech model

    /// From the Transcription screen with a PC-local provider selected: the PC speech model screen.
    @MainActor
    static func openPCSpeechModel(file: StaticString = #filePath, line: UInt = #line) {
        tapButton("PC speech model", file: file, line: line)
        waitFor("pc-model-state", file: file, line: line)
    }

    /// On the PC speech model screen: "Start model", once the status is known and the control is enabled.
    @MainActor
    static func tapStartPCModel(file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(waitForText("pc-model-state", equals: "Stopped"), "The PC model is not reported stopped.", file: file, line: line)
        let start = waitFor("start-pc-model", file: file, line: line)
        XCTAssertTrue(waitUntilEnabled(start), "\"Start model\" is not enabled for a stopped model.", file: file, line: line)
        start.tap()
    }

    // MARK: - Session

    /// From Home: taps Start and waits for the live screen.
    @MainActor
    static func startSession(file: StaticString = #filePath, line: UInt = #line) {
        let start = waitFor("start-session", timeout: UITestSupport.launchTimeout, file: file, line: line)
        XCTAssertTrue(waitUntilEnabled(start), "Start is not enabled.", file: file, line: line)
        start.tap()
        waitFor("assist-button", file: file, line: line)
    }

    /// Taps Stop and waits for Home.
    @MainActor
    static func stopSession(file: StaticString = #filePath, line: UInt = #line) {
        let stop = waitFor("end-session", file: file, line: line)
        XCTAssertTrue(waitUntilEnabled(stop), "Stop is not enabled.", file: file, line: line)
        stop.tap()
        waitFor("start-session", file: file, line: line)
    }

    /// The element that reads the session's state: its label is the chip text, its value the elapsed
    /// time. The status line can show the same word, and has its own identifier.
    @MainActor
    static func sessionState(_ text: String) -> XCUIElement {
        anyElement("label == %@ AND identifier != %@", text, "transcriber-status")
    }

    /// Leaves a session listening for `seconds` and returns true when it stayed listening: nothing may
    /// pause it by itself. A fixture stream reports its first second of audio after one second, and a
    /// stream that is closed before that has no usage to show, which History marks as incomplete. A test
    /// that pauses or stops a stream lets it run this long first.
    @MainActor
    static func staysListening(for seconds: TimeInterval = 2.5) -> Bool {
        !sessionState("Paused").waitForExistence(timeout: seconds)
    }

    /// Pause, Assist and Stop exist and are enabled.
    @MainActor
    static func assertSessionControlsEnabled(file: StaticString = #filePath, line: UInt = #line) {
        for identifier in ["pause-session", "assist-button", "end-session"] {
            let control = UITestSupport.element(identifier)
            XCTAssertTrue(waitUntilEnabled(control), "\"\(identifier)\" cannot be used.", file: file, line: line)
            XCTAssertTrue(isOnScreen(control), "\"\(identifier)\" is not on screen.", file: file, line: line)
        }
    }

    /// Taps Assist and waits until the session shows `answers` answers and the control reads "Assist" again.
    @MainActor
    static func assist(reaching answers: Int, file: StaticString = #filePath, line: UInt = #line) {
        let button = waitFor("assist-button", file: file, line: line)
        XCTAssertTrue(waitForLabel("assist-button", equals: "Assist"), "Assist is still busy.", file: file, line: line)
        XCTAssertTrue(waitUntilEnabled(button), "Assist is not enabled.", file: file, line: line)
        button.tap()
        if answers <= 1 {
            waitFor("assistant-answer", file: file, line: line)
        } else {
            let earlier = app.descendants(matching: .any).matching(identifier: "previous-assistant-answer")
            XCTAssertTrue(
                earlier.element(boundBy: answers - 2).appears(within: UITestSupport.timeout),
                "Answer \(answers) did not arrive.",
                file: file,
                line: line
            )
        }
        XCTAssertTrue(waitForLabel("assist-button", equals: "Assist"), "Assist did not finish.", file: file, line: line)
    }

    /// Opens the timing of the latest answer.
    @MainActor
    static func openLatestTiming(file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertFalse(UITestSupport.element("response-timing").exists, "The timing of a new answer starts closed.", file: file, line: line)
        tap("timing-disclosure", file: file, line: line)
        waitFor("response-timing", file: file, line: line)
    }

    /// Closes the timing of the latest answer, so that what is below it comes back into reach.
    @MainActor
    static func closeLatestTiming(file: StaticString = #filePath, line: UInt = #line) {
        tap("timing-disclosure", file: file, line: line)
        XCTAssertTrue(waitForDisappearance(of: "response-timing"), "The timing did not close.", file: file, line: line)
    }

    // MARK: - Alerts

    /// Waits for an alert whose message contains `text` and returns it.
    @MainActor @discardableResult
    static func waitForAlert(containing text: String, file: StaticString = #filePath, line: UInt = #line) -> XCUIElement {
        let alert = app.alerts.firstMatch
        XCTAssertTrue(alert.appears(within: UITestSupport.timeout), "No alert appeared.", file: file, line: line)
        let message = alert.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", text)).firstMatch
        XCTAssertTrue(message.appears(within: 5), "The alert does not mention \"\(text)\".", file: file, line: line)
        return alert
    }

    /// Presses the alert's button and waits until the alert is gone.
    @MainActor
    static func close(_ alert: XCUIElement, with button: String = "OK", file: StaticString = #filePath, line: UInt = #line) {
        alert.buttons[button].firstMatch.tap()
        XCTAssertTrue(waitUntilGone(alert), "The alert did not close.", file: file, line: line)
    }

    // MARK: - Typing

    /// Gives the field with this identifier the keyboard and types. The field is tapped at a point inside
    /// it and the text goes to whatever has the keyboard: on iOS 26 a secure field is not reported as
    /// hittable.
    @MainActor
    static func enter(
        _ text: String,
        into identifier: String,
        at offset: CGVector = CGVector(dx: 0.5, dy: 0.5),
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let field = waitFor(identifier, file: file, line: line)
        field.coordinate(withNormalizedOffset: offset).tap()
        // A simulator with a hardware keyboard shows none on screen, so this only gives focus time to settle.
        _ = app.keyboards.firstMatch.appears(within: 5)
        app.typeText(text)
    }

    /// Types a made-up key into a key field and presses its "Save key". The key is typed once more when
    /// the first try did not reach the field.
    @MainActor
    static func saveKey(
        _ key: String,
        field: String,
        save: String,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        let saveButton = waitFor(save, file: file, line: line)
        enter(key, into: field, file: file, line: line)
        if !waitUntilEnabled(saveButton, timeout: 5) {
            note("The key did not reach \"\(field)\" at the first try.")
            enter(key, into: field, file: file, line: line)
        }
        XCTAssertTrue(waitUntilEnabled(saveButton, timeout: 5), "\"\(save)\" is not enabled after typing a key.", file: file, line: line)
        // AC-IOS-108 and 118: what was typed is not readable anywhere.
        XCTAssertTrue(nothingShows(key), "The typed key can be read on screen.", file: file, line: line)
        saveButton.tap()
    }

    /// True when no element shows `secret`, as part of its label or as its value.
    @MainActor
    static func nothingShows(_ secret: String) -> Bool {
        let predicate = NSPredicate(format: "label CONTAINS %@ OR value == %@", secret, secret)
        return app.descendants(matching: .any).matching(predicate).count == 0
    }

    // MARK: - History

    /// The History row of a conversation with these counts, for example "1 transcript segments · 1 assists".
    @MainActor
    static func historyRow(_ counts: String) -> XCUIElement {
        anyLabelContaining(counts)
    }

    // MARK: - One consistent look at the screen

    /// The element with this identifier in a snapshot of the whole app. Two readings from one snapshot
    /// belong to the same moment, which two queries do not.
    @MainActor
    static func find(_ identifier: String, in snapshot: any XCUIElementSnapshot) -> (any XCUIElementSnapshot)? {
        if snapshot.identifier == identifier {
            return snapshot
        }
        for child in snapshot.children {
            if let found = find(identifier, in: child) {
                return found
            }
        }
        return nil
    }
}

extension XCUIElement {
    /// `waitForExistence`, with a look first: true at once when the element is already there.
    @MainActor
    func appears(within timeout: TimeInterval) -> Bool {
        exists || waitForExistence(timeout: timeout)
    }
}
