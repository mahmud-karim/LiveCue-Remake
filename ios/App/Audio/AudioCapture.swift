import AVFoundation

/// `AVAudioEngine` capture, interruptions and route loss, for every provider.
///
/// A new engine is made for every start, so each start takes the hardware as it is then. The tap hands
/// its buffers to an `AudioTapConverter`, which owns the conversion and the two streams; this class only
/// starts and stops things, and it does so on the main actor. Nothing here touches the microphone before
/// `start`, so building one at launch asks for no permission.
///
/// A start that cannot go on says why: every failure carries what the session reported at that moment
/// (`AudioStartReport`), and `AudioInputDiagnosis` turns it into the text the user reads.
@MainActor final class AudioCapture: AudioCapturing {
    private var engine: AVAudioEngine?
    private var converter: AudioTapConverter?
    private var observers: [any NSObjectProtocol] = []
    /// True between a successful activation of the audio session and its release.
    private var holdsSession = false

    init() {}

    var isRunning: Bool {
        converter != nil
    }

    func start(_ request: AudioCaptureRequest) throws -> AudioCaptureStreams {
        // One capture at a time. An earlier one ends here; the session it activated is reused.
        if isRunning {
            stop(releaseSession: false)
        }

        let session = AVAudioSession.sharedInstance()
        do {
            try AudioSessionPolicy.activate(session)
        } catch {
            let code = (error as NSError).code
            let report = AudioSessionPolicy.report(session, stage: .session(code: code), format: nil, converterBuilt: false)
            releaseSession()
            throw AudioCaptureFailure.unusable(report)
        }
        holdsSession = true

        // The route can take a moment to settle after the activation. An input without a usable format
        // is read again with a new engine, after a short pause. The pause blocks this start, which is
        // synchronous by contract; it happens only on the way to a failure, and lasts 0.4 s at most.
        var found: (engine: AVAudioEngine, format: AVAudioFormat, converter: AudioTapConverter)?
        var lastFormat: AVAudioFormat?
        for attempt in 1...AudioInputDiagnosis.attempts {
            let candidate = AVAudioEngine()
            // The format the hardware delivers. A tap installed with any other format raises an exception.
            let format = candidate.inputNode.outputFormat(forBus: 0)
            let usable = AudioSessionPolicy.isUsable(format)
            let converter = usable ? AudioTapConverter(inputFormat: format, request: request) : nil
            lastFormat = format
            if let converter {
                found = (candidate, format, converter)
                break
            }
            guard case .retry(let seconds) = AudioInputDiagnosis.step(afterAttempt: attempt, formatUsable: usable, converterBuilt: false) else {
                break
            }
            Thread.sleep(forTimeInterval: seconds)
        }
        guard let found else {
            let report = AudioSessionPolicy.report(session, stage: .input, format: lastFormat, converterBuilt: false)
            releaseSession()
            throw AudioCaptureFailure.unusable(report)
        }
        let engine = found.engine
        let input = engine.inputNode
        let format = found.format
        let converter = found.converter

        input.installTap(
            onBus: 0,
            bufferSize: AudioSessionPolicy.tapBufferFrames,
            format: format,
            block: AudioTapConverter.makeTapBlock(for: converter)
        )
        engine.prepare()
        do {
            try engine.start()
        } catch {
            let code = (error as NSError).code
            let report = AudioSessionPolicy.report(session, stage: .engine(code: code), format: format, converterBuilt: true)
            input.removeTap(onBus: 0)
            converter.finish()
            releaseSession()
            throw AudioCaptureFailure.unusable(report)
        }

        // Interruptions and route loss are observed for every provider (IOS-B5). The observers report
        // straight into the event stream; nothing resumes by itself.
        let center = NotificationCenter.default
        observers = [
            center.addObserver(
                forName: AVAudioSession.interruptionNotification,
                object: session,
                queue: nil,
                using: AudioSessionPolicy.interruptionObserver(reportingTo: converter)
            ),
            center.addObserver(
                forName: AVAudioSession.routeChangeNotification,
                object: session,
                queue: nil,
                using: AudioSessionPolicy.routeChangeObserver(reportingTo: converter)
            )
        ]

        self.engine = engine
        self.converter = converter
        return converter.streams
    }

    func stop(releaseSession release: Bool) {
        for observer in observers {
            NotificationCenter.default.removeObserver(observer)
        }
        observers = []

        if let engine {
            engine.inputNode.removeTap(onBus: 0)
            engine.stop()
        }
        engine = nil

        // After the tap is gone no buffer arrives; what is queued stays readable.
        converter?.finish()
        converter = nil

        if release {
            releaseSession()
        }
    }

    /// Deactivates the audio session if this capture activated it, notifying other apps.
    private func releaseSession() {
        guard holdsSession else { return }
        holdsSession = false
        AudioSessionPolicy.deactivate(AVAudioSession.sharedInstance())
    }
}
