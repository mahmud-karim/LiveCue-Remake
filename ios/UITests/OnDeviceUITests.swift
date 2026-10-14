import XCTest

/// T12: a conversation, then the download of an on-device model, then a conversation with Parakeet. With
/// it, the providers added since: Apple speech (D-28) and Fluid Speech Mini (D-29).
final class OnDeviceUITests: XCTestCase {
    override func setUpWithError() throws {
        continueAfterFailure = false
        // Two launches, two conversations and a model preparation.
        executionTimeAllowance = 600
    }

    /// D-28: Apple speech is the sixth provider. In the test seam its language asset is installed, so
    /// choosing it needs no download and Home offers Start.
    @MainActor
    func testAppleSpeechIsChosenAndReadyWithoutADownload() throws {
        let app = UITestWalk.launchToHome()

        UITestWalk.openTranscription()
        XCTAssertTrue(UITestSupport.element("provider-apple").appears(within: UITestSupport.timeout), "Apple speech is not listed.")
        UITestWalk.selectProvider("provider-apple")
        XCTAssertFalse(UITestSupport.element("provider-meta").isSelected, "Meta stayed selected.")
        XCTAssertTrue(UITestWalk.button("Model Library").appears(within: UITestSupport.timeout), "Apple speech has no Model Library row.")
        XCTAssertFalse(UITestSupport.element("meta-key-settings").exists, "The Meta API key row stayed for Apple speech.")
        shot("d28-transcription-apple")

        // Ready as it is: Start, not the download link.
        UITestWalk.openTab("Live")
        UITestWalk.waitFor("start-session")
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home offers a download for Apple speech.")
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared.")
        shot("d28-home-apple")
    }

    /// D-29: Fluid Speech Mini is the seventh provider. Chosen, it needs its model, which the test seam
    /// prepares without a download. With `-fluid-ready` the model is loaded at launch: a conversation
    /// shows its text and no cost, and Assist works.
    @MainActor
    func testFluidSpeechMiniIsChosenPreparedAndUsed() throws {
        UITestWalk.launchToHome()

        UITestWalk.openTranscription()
        XCTAssertTrue(UITestSupport.element("provider-fluid").appears(within: UITestSupport.timeout), "Fluid Speech Mini is not listed.")
        UITestWalk.selectProvider("provider-fluid")
        XCTAssertFalse(UITestSupport.element("provider-meta").isSelected, "Meta stayed selected.")
        XCTAssertTrue(UITestWalk.button("Model Library").appears(within: UITestSupport.timeout), "Fluid Speech Mini has no Model Library row.")
        XCTAssertFalse(UITestSupport.element("meta-key-settings").exists, "The Meta API key row stayed for Fluid Speech Mini.")
        shot("d29-transcription-fluid")

        // Home offers the download in place of Start, and it opens Model Library with the model's entry.
        UITestWalk.openTab("Live")
        let download = UITestWalk.waitFor("download-model")
        XCTAssertFalse(UITestSupport.element("start-session").exists, "Home offers Start without a loaded model.")
        download.tap()
        let prepare = UITestWalk.button("Download & use")
        XCTAssertTrue(prepare.appears(within: UITestSupport.timeout), "The download did not open Model Library.")
        XCTAssertTrue(UITestWalk.anyLabelContaining("Live Fluid Speech Mini").exists, "Model Library does not name Fluid Speech Mini.")
        shot("d29-model-library-fluid")

        // A progress bar while preparing, then "Reload", and Home offers Start.
        prepare.tap()
        XCTAssertTrue(UITestSupport.element("model-download-progress").appears(within: UITestSupport.timeout), "No progress bar appeared.")
        XCTAssertTrue(UITestWalk.button("Reload").appears(within: 30), "The button does not read \"Reload\" after the preparation.")
        XCTAssertTrue(UITestWalk.waitForDisappearance(of: "model-download-progress"), "The progress bar stayed after the preparation.")
        UITestWalk.back()
        UITestWalk.waitFor("start-session")
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home still offers the download.")

        // The seam's `-fluid-ready`: the model is loaded at launch, and the conversation shows its text.
        let app = UITestWalk.launchToHome(["-fluid-ready"])
        XCTAssertFalse(UITestSupport.element("download-model").exists, "Home offers the download for a loaded model.")
        UITestWalk.startSession()
        UITestWalk.tap("expand-transcript")
        UITestWalk.waitFor("live-transcript")
        XCTAssertTrue(UITestWalk.anyShowing(UITestWalk.segment).appears(within: UITestSupport.timeout), "The transcript does not show the text.")
        UITestWalk.tap("expand-transcript")
        XCTAssertTrue(UITestWalk.waitForDisappearance(of: "live-transcript"), "The transcript did not collapse.")
        XCTAssertFalse(UITestSupport.element("live-transcription-cost").exists, "A Fluid Speech Mini session shows a cost.")
        XCTAssertFalse(UITestWalk.anyLabelContaining("USD").exists, "A Fluid Speech Mini session shows a cost.")
        UITestWalk.assist(reaching: 1)
        XCTAssertEqual(UITestWalk.text(of: UITestSupport.element("assistant-answer")), UITestWalk.answer)
        XCTAssertFalse(UITestSupport.element("assist-error").exists, "The Assist failed in a Fluid Speech Mini session.")
        XCTAssertEqual(app.alerts.count, 0, "An alert appeared in the Fluid Speech Mini session.")
        shot("d29-fluid-session")
        UITestWalk.stopSession()
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
