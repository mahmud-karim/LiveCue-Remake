import XCTest

/// T12: a conversation, then the download of an on-device model, then a conversation with Parakeet.
final class OnDeviceUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
        // Two launches, two conversations and a model preparation.
        executionTimeAllowance = 600
    }

    /// T12. AC-IOS-009, 026, 075, 145, 150.
    @MainActor
    func testConversationThenModelDownload() throws {
        UITestWalk.launchToHome()

        // AC-IOS-026: the transcript opens and closes, and Stop stays usable.
        UITestWalk.startSession()
        UITestWalk.tap("expand-transcript")
        UITestWalk.waitFor("live-transcript")
        XCTAssertTrue(UITestWalk.anyShowing(UITestWalk.segment).appears(within: UITestSupport.timeout), "The expanded transcript does not list the segment.")
        XCTAssertTrue(UITestSupport.element("end-session").isEnabled, "Stop cannot be used while the transcript is expanded.")
        UITestWalk.tap("expand-transcript")
        XCTAssertTrue(UITestWalk.waitForDisappearance(of: "live-transcript"), "The transcript did not collapse.")
        XCTAssertTrue(UITestSupport.element("end-session").isEnabled, "Stop cannot be used after the transcript collapsed.")
        UITestWalk.assist(reaching: 1)
        UITestWalk.stopSession()

        // AC-IOS-075: History lists the conversation with its counts.
        UITestWalk.openTab("History")
        XCTAssertTrue(
            UITestWalk.historyRow("1 transcript segments · 1 assists").appears(within: UITestSupport.timeout),
            "History does not read \"1 transcript segments · 1 assists\"."
        )
        shot("t12-history")

        // AC-IOS-095: an on-device provider brings the Model Library and Speech benchmark rows.
        UITestWalk.openTranscription()
        XCTAssertFalse(UITestWalk.button("Model Library").exists, "Meta has a Model Library row.")
        UITestWalk.selectProvider("provider-voz")
        XCTAssertTrue(UITestWalk.button("Model Library").appears(within: UITestSupport.timeout), "Voz has no Model Library row.")
        XCTAssertTrue(UITestWalk.button("Speech benchmark").exists, "Voz has no Speech benchmark row.")
        shot("t12-transcription-on-device")

        // AC-IOS-009: Home offers the download in place of Start, and it opens Model Library.
        UITestWalk.openTab("Live")
        let download = UITestWalk.waitFor("download-model")
        XCTAssertEqual(download.label, "Download a transcription model")
        XCTAssertFalse(UITestSupport.element("start-session").exists, "Home offers Start without a loaded model.")
        shot("t12-home-download")
        download.tap()
        let prepare = UITestWalk.button("Download & use")
        XCTAssertTrue(prepare.appears(within: UITestSupport.timeout), "The download did not open Model Library.")
        shot("t12-model-library")

        // AC-IOS-145: a progress bar while preparing, then "Reload", and Home offers Start.
        prepare.tap()
        XCTAssertTrue(UITestSupport.element("model-download-progress").appears(within: UITestSupport.timeout), "No progress bar appeared.")
        shot("t12-model-library-preparing")
        XCTAssertTrue(UITestWalk.button("Reload").appears(within: 30), "The button does not read \"Reload\" after the preparation.")
        XCTAssertTrue(UITestWalk.waitForDisappearance(of: "model-download-progress"), "The progress bar stayed after the preparation.")
        shot("t12-model-library-ready")
        UITestWalk.back()
        UITestWalk.waitFor("start-session")
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home still offers the download.")
        shot("t12-home-model-ready")

        // A picture of the Speech benchmark screen, which belongs to the on-device providers.
        UITestWalk.openTab("Settings")
        UITestWalk.tapButton("Speech benchmark")
        if UITestWalk.button("Use latest transcript").appears(within: UITestSupport.timeout) {
            shot("t12-speech-benchmark")
        }

        // AC-IOS-150: a Parakeet session shows no cost, and Assist works.
        let app = UITestWalk.launchToHome(["-on-device-ready"])
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home offers the download for a loaded model.")
        UITestWalk.startSession()
        XCTAssertFalse(UITestSupport.element("live-transcription-cost").exists, "A Parakeet session shows a cost.")
        XCTAssertFalse(UITestWalk.anyLabelContaining("USD").exists, "A Parakeet session shows a cost.")
        UITestWalk.assist(reaching: 1)
        XCTAssertEqual(UITestWalk.text(of: UITestSupport.element("assistant-answer")), UITestWalk.answer)
        XCTAssertFalse(UITestSupport.element("assist-error").exists, "The Assist failed in a Parakeet session.")
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared in the Parakeet session.")
        shot("t12-parakeet-session")
        UITestWalk.stopSession()

        UITestWalk.openTab("History")
        XCTAssertTrue(
            UITestWalk.historyRow("1 transcript segments · 1 assists").appears(within: UITestSupport.timeout),
            "History does not list the Parakeet conversation."
        )
        XCTAssertFalse(UITestSupport.element("history-transcription-cost").exists, "A Parakeet conversation has a cost in History.")
    }

    // MARK: - Helpers

    @MainActor
    private func shot(_ name: String) {
        UITestSupport.attachScreenshot(named: name, to: self)
    }
}
