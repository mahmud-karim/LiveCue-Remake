import Foundation

/// `AVAudioEngine` capture, interruptions and route loss.
@MainActor final class AudioCapture: AudioCapturing {
    init() {}

    var isRunning: Bool {
        // STUB(WP-07)
        false
    }

    func start(_ request: AudioCaptureRequest) throws -> AudioCaptureStreams {
        // STUB(WP-07)
        throw AudioCaptureFailure.couldNotStart
    }

    func stop(releaseSession: Bool) {
        // STUB(WP-07)
    }
}
