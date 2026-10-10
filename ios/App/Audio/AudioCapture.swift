import AVFoundation

/// `AVAudioEngine` capture, interruptions and route loss, for every provider.
///
/// A new engine is made for every start, so each start takes the hardware as it is then. The tap hands
/// its buffers to an `AudioTapConverter`, which owns the conversion and the two streams; this class only
/// starts and stops things, and it does so on the main actor. Nothing here touches the microphone before
/// `start`, so building one at launch asks for no permission.
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
            releaseSession()
            throw AudioCaptureFailure.couldNotStart
        }
        holdsSession = true

        let engine = AVAudioEngine()
        let input = engine.inputNode
        // The format the hardware delivers. A tap installed with any other format raises an exception.
        let format = input.outputFormat(forBus: 0)
        guard AudioSessionPolicy.isUsable(format),
              let converter = AudioTapConverter(inputFormat: format, request: request) else {
            releaseSession()
            throw AudioCaptureFailure.noUsableMicrophone
        }

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
            input.removeTap(onBus: 0)
            converter.finish()
            releaseSession()
            throw AudioCaptureFailure.couldNotStart
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
