import XCTest
@testable import LiveCueRemake

/// The decisions of a capture start whose input is not usable (1.1.0): when it tries again, when it gives
/// up, and what the user then reads. The audio session cannot be faked in CI, so the values are injected.
final class AudioInputDiagnosisTests: XCTestCase {
    private func report(
        _ stage: AudioStartReport.Stage = .input,
        available: Bool = true,
        route: [String] = ["MicrophoneBuiltIn"],
        rate: Double = 0,
        channels: Int = 0,
        converter: Bool = false
    ) -> AudioStartReport {
        AudioStartReport(
            stage: stage,
            isInputAvailable: available,
            routeInputs: route,
            sampleRate: rate,
            channelCount: channels,
            converterBuilt: converter
        )
    }

    private let hint = "iOS is giving this app no microphone. In LiveContainer, open the app normally, not in a multitasking window, and check that LiveContainer has microphone access in Settings."

    // MARK: - Trying again

    func testAnInputIsUsableWithARateAndAChannel() {
        XCTAssertTrue(AudioInputDiagnosis.isUsable(sampleRate: 48_000, channelCount: 1))
        XCTAssertTrue(AudioInputDiagnosis.isUsable(sampleRate: 16_000, channelCount: 2))
        XCTAssertFalse(AudioInputDiagnosis.isUsable(sampleRate: 0, channelCount: 1))
        XCTAssertFalse(AudioInputDiagnosis.isUsable(sampleRate: 48_000, channelCount: 0))
        XCTAssertFalse(AudioInputDiagnosis.isUsable(sampleRate: 0, channelCount: 0))
    }

    func testAnUnusableInputIsReadThreeTimesBeforeGivingUp() {
        XCTAssertEqual(AudioInputDiagnosis.attempts, 3)
        XCTAssertEqual(AudioInputDiagnosis.retryDelaySeconds, 0.2)

        XCTAssertEqual(AudioInputDiagnosis.step(afterAttempt: 1, formatUsable: false, converterBuilt: false), .retry(afterSeconds: 0.2))
        XCTAssertEqual(AudioInputDiagnosis.step(afterAttempt: 2, formatUsable: false, converterBuilt: false), .retry(afterSeconds: 0.2))
        XCTAssertEqual(AudioInputDiagnosis.step(afterAttempt: 3, formatUsable: false, converterBuilt: false), .giveUp)
        XCTAssertEqual(AudioInputDiagnosis.step(afterAttempt: 4, formatUsable: false, converterBuilt: false), .giveUp)
    }

    func testAUsableInputGoesOnAtOnceAndAMissingConverterIsTriedAgain() {
        for attempt in 1...3 {
            XCTAssertEqual(AudioInputDiagnosis.step(afterAttempt: attempt, formatUsable: true, converterBuilt: true), .proceed)
        }
        XCTAssertEqual(AudioInputDiagnosis.step(afterAttempt: 1, formatUsable: true, converterBuilt: false), .retry(afterSeconds: 0.2))
        XCTAssertEqual(AudioInputDiagnosis.step(afterAttempt: 3, formatUsable: true, converterBuilt: false), .giveUp)
    }

    // MARK: - What the user reads

    func testNoInputAtAllGivesTheLiveContainerHint() {
        // iOS says no input is available.
        let unavailable = report(available: false, route: [])
        XCTAssertTrue(AudioInputDiagnosis.givesNoInput(unavailable))
        XCTAssertEqual(
            AudioInputDiagnosis.message(for: unavailable),
            hint + " (input 0 Hz, 0 ch, route: none, input unavailable)"
        )

        // An active session whose route has no input.
        let noRoute = report(available: true, route: [])
        XCTAssertTrue(AudioInputDiagnosis.givesNoInput(noRoute))
        XCTAssertEqual(AudioInputDiagnosis.message(for: noRoute), hint + " (input 0 Hz, 0 ch, route: none)")
        XCTAssertEqual(AudioInputDiagnosis.detail(noRoute), "(input 0 Hz, 0 ch, route: none)")
    }

    func testAnInputThatIsThereButUnusableKeepsTheOldTextWithTheFigures() {
        let silent = report()
        XCTAssertFalse(AudioInputDiagnosis.givesNoInput(silent))
        XCTAssertEqual(
            AudioInputDiagnosis.message(for: silent),
            "No usable microphone. (input 0 Hz, 0 ch, route: MicrophoneBuiltIn)"
        )

        let noChannels = report(route: ["BluetoothHFP"], rate: 16_000, channels: 0)
        XCTAssertEqual(
            AudioInputDiagnosis.message(for: noChannels),
            "No usable microphone. (input 16000 Hz, 0 ch, route: BluetoothHFP)"
        )
    }

    func testAMissingConverterIsNamed() {
        let unconvertible = report(rate: 48_000, channels: 1, converter: false)
        XCTAssertEqual(
            AudioInputDiagnosis.message(for: unconvertible),
            "No usable microphone. (input 48000 Hz, 1 ch, route: MicrophoneBuiltIn, no converter)"
        )
    }

    func testARefusedSessionAndARefusedEngineCarryTheirCodes() {
        // The route of a session that could not be activated says nothing about the microphone.
        let refused = report(.session(code: 561_017_449), route: [])
        XCTAssertFalse(AudioInputDiagnosis.givesNoInput(refused))
        XCTAssertEqual(
            AudioInputDiagnosis.message(for: refused),
            "No usable microphone. (audio session error 561017449, route: none)"
        )

        let refusedWithoutInput = report(.session(code: 561_017_449), available: false, route: [])
        XCTAssertEqual(
            AudioInputDiagnosis.message(for: refusedWithoutInput),
            hint + " (audio session error 561017449, route: none, input unavailable)"
        )

        let engine = report(.engine(code: -10_851), route: ["MicrophoneWired"], rate: 44_100, channels: 2, converter: true)
        XCTAssertEqual(
            AudioInputDiagnosis.message(for: engine),
            "No usable microphone. (audio engine error -10851, input 44100 Hz, 2 ch, route: MicrophoneWired)"
        )
    }

    func testTheDetailNamesPortTypesOnlyAndStaysShort() {
        let many = report(route: ["MicrophoneBuiltIn", "BluetoothHFP", "MicrophoneWired", "USBAudio"], rate: 48_000, channels: 1, converter: true)
        XCTAssertEqual(
            AudioInputDiagnosis.detail(many),
            "(input 48000 Hz, 1 ch, route: MicrophoneBuiltIn + BluetoothHFP + MicrophoneWired)"
        )
        // Figures that are not numbers show as zero.
        let odd = report(rate: .nan, channels: -1)
        XCTAssertEqual(AudioInputDiagnosis.detail(odd), "(input 0 Hz, 0 ch, route: MicrophoneBuiltIn)")
    }

    // MARK: - From the capture to the transcriber's failure

    func testTheTranscriberFailureQuotesTheCaptureOrKeepsTheOldText() {
        let found = report(available: false, route: [])
        XCTAssertEqual(
            TranscriberFailureText.microphone(AudioCaptureFailure.unusable(found)),
            TranscriberFailure(.noMicrophone, message: hint + " (input 0 Hz, 0 ch, route: none, input unavailable)")
        )
        // A capture that says nothing, and any other error, read as before.
        XCTAssertEqual(TranscriberFailureText.microphone(AudioCaptureFailure.noUsableMicrophone), TranscriberFailureText.noMicrophone)
        XCTAssertEqual(TranscriberFailureText.microphone(AudioCaptureFailure.couldNotStart), TranscriberFailureText.noMicrophone)
        XCTAssertEqual(TranscriberFailureText.microphone(CancellationError()), TranscriberFailureText.noMicrophone)
        XCTAssertEqual(TranscriberFailureText.noMicrophone.message, "No usable microphone.")
    }
}
