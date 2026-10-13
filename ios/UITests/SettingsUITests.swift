import XCTest

/// T14 and T15: what Settings discloses, and the instruction that lives only there.
final class SettingsUITests: XCTestCase {
    private static let instruction = "Answer briefly"
    private static let instructionPlaceholder = "Optional instruction (e.g. answer briefly)"

    override func setUpWithError() throws {
        continueAfterFailure = false
        executionTimeAllowance = 600
    }

    /// T14. AC-IOS-089, 090 as changed (the theme is "Daylight"), 098.
    @MainActor
    func testAppearanceAndCloudDisclosure() throws {
        let app = UITestWalk.launchToHome()
        UITestWalk.openSettings()
        // The first health check of the fixture relay turns the pairing row to "connected".
        _ = UITestWalk.anyLabelled("Saved · connected").appears(within: UITestSupport.timeout)

        // AC-IOS-089: with Meta, Settings says where the audio goes.
        XCTAssertTrue(
            UITestWalk.anyLabelContaining("Audio streams directly from this iPhone to Meta").exists,
            "Settings does not say that audio streams directly to Meta."
        )
        // AC-IOS-096: the Meta API key row reads "Saved on iPhone" with the seeded key.
        XCTAssertTrue(UITestWalk.waitForValue("meta-key-settings", equals: "Saved on iPhone"), "The Meta API key row does not read \"Saved on iPhone\".")
        shot("t14-settings")

        // AC-IOS-090: the Appearance row names the theme.
        XCTAssertTrue(
            UITestWalk.anyElement("label BEGINSWITH %@ AND label CONTAINS %@", "Appearance", "Daylight").exists,
            "Settings has no Appearance row that reads \"Daylight\"."
        )
        // AC-IOS-098: the marketing version is shown.
        let version = UITestWalk.anyElement("label BEGINSWITH %@ AND label MATCHES %@", "Version", "(?s).*[0-9]+\\.[0-9]+.*")
        XCTAssertTrue(version.exists, "Settings shows no version number.")
        XCTAssertFalse(UITestWalk.anyElement("label BEGINSWITH %@ AND label CONTAINS %@", "Version", "Unknown").exists, "The version reads \"Unknown\".")
        // The two statements about the speech packages (architecture 4.13).
        XCTAssertTrue(UITestWalk.anyLabelContaining("reports anonymous usage to its vendor, Desert Ant Labs").exists, "The usage statement is missing.")

        app.swipeUp()
        shot("t14-settings-middle")
        app.swipeUp()
        shot("t14-settings-about")
    }

    /// T15. AC-IOS-034, 091, 092.
    @MainActor
    func testInstructionPersists() throws {
        var app = UITestWalk.launchToHome()
        UITestWalk.openSettings()

        // What an earlier run left in the field is removed first; the caret goes to the end of the text.
        let field = UITestWalk.waitFor("assistant-instruction")
        let left = field.value as? String ?? ""
        let end = CGVector(dx: 0.9, dy: 0.9)
        if left.isEmpty || left == SettingsUITests.instructionPlaceholder {
            UITestWalk.enter(SettingsUITests.instruction, into: "assistant-instruction", at: end)
        } else {
            let deletes = String(repeating: XCUIKeyboardKey.delete.rawValue, count: left.count)
            UITestWalk.enter(deletes + SettingsUITests.instruction, into: "assistant-instruction", at: end)
        }
        XCTAssertTrue(
            UITestWalk.waitForText("assistant-instruction", equals: SettingsUITests.instruction),
            "The field reads \"\(UITestWalk.text(of: field))\" after typing."
        )
        shot("t15-instruction-typed")

        // AC-IOS-092: the keyboard's "Done" closes the keyboard.
        var done = UITestSupport.element("finish-instruction")
        if !done.appears(within: 5) {
            UITestWalk.note("The keyboard's Done button is not found by \"finish-instruction\".")
            done = app.toolbars.buttons["Done"].firstMatch
        }
        XCTAssertTrue(done.appears(within: 5), "The keyboard has no \"Done\".")
        done.tap()
        XCTAssertTrue(UITestWalk.waitUntilGone(app.keyboards.firstMatch), "The keyboard stayed after \"Done\".")
        XCTAssertTrue(UITestWalk.waitUntilGone(done), "\"Done\" stayed after it was pressed.")
        shot("t15-instruction-done")

        // AC-IOS-091: the same instruction is shown after a relaunch.
        app = UITestWalk.launchToHome()
        UITestWalk.openSettings()
        XCTAssertTrue(
            UITestWalk.waitForText("assistant-instruction", equals: SettingsUITests.instruction),
            "The field reads \"\(UITestWalk.text(of: UITestSupport.element("assistant-instruction")))\" after the relaunch."
        )
        shot("t15-instruction-after-relaunch")

        // AC-IOS-034: the instruction lives only in Settings; the live screen has no field for it.
        XCTAssertEqual(UITestWalk.count(of: "assistant-instruction"), 1, "Settings has more than one instruction field.")
        UITestWalk.openTab("Live")
        XCTAssertFalse(UITestSupport.element("assistant-instruction").exists, "Home has an instruction field.")
        UITestWalk.startSession()
        XCTAssertFalse(UITestSupport.element("assistant-instruction").exists, "The live screen has an instruction field.")
        XCTAssertEqual(app.textFields.count + app.textViews.count, 0, "The live screen has a text field.")
        UITestWalk.assist(reaching: 1)
        XCTAssertFalse(UITestSupport.element("assistant-instruction").exists, "The live screen has an instruction field after an answer.")
        UITestWalk.stopSession()
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }
}
