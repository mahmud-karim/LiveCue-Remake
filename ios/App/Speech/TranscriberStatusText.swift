import Foundation
import LiveCueCore

/// The status line texts of architecture 4.7. Numbers are formatted with the POSIX locale, so the
/// decimal separator is always ".".
enum TranscriberStatusText {
    /// "Connecting to <display name>…"
    static func connecting(_ provider: SpeechProvider) -> String {
        "Connecting to \(provider.displayName)…"
    }

    /// "Loading <short name> on PC · <N> s. Microphone starts when ready."
    static func loading(_ provider: SpeechProvider, seconds: Int) -> String {
        "Loading \(provider.shortName) on PC · \(max(0, seconds)) s. Microphone starts when ready."
    }

    /// "Meta direct · 0.00 s setup" / "Connected · 0.00 s setup"
    static func connected(_ provider: SpeechProvider, setupSeconds: Double) -> String {
        let setup = fixed(setupSeconds, digits: 2)
        if provider == .meta {
            return "Meta direct · \(setup) s setup"
        }
        return "Connected · \(setup) s setup"
    }

    /// "First words · 0.00 s from stream start"
    static func firstWords(seconds: Double) -> String {
        "First words · \(fixed(seconds, digits: 2)) s from stream start"
    }

    /// line + " · Final 0.00 s"
    static func appendingFinal(_ line: String, seconds: Double) -> String {
        line + " · Final \(fixed(seconds, digits: 2)) s"
    }

    /// "Last buffer processing: N ms"
    static func lastBuffer(milliseconds: Double) -> String {
        "Last buffer processing: \(fixed(milliseconds, digits: 0)) ms"
    }

    /// "0.0 s audio → 0.00 s transcription"
    static func batch(audioSeconds: Double, transcriptionSeconds: Double) -> String {
        "\(fixed(audioSeconds, digits: 1)) s audio → \(fixed(transcriptionSeconds, digits: 2)) s transcription"
    }

    static let recordingOnDevice = "Recording on this iPhone"
    static let disconnected = "Disconnected · resume to retry"
    static let finishing = "Finishing the transcript…"

    // The capture states of spec 01 section 8.2.
    static let listening = "Listening"
    static let transcribing = "Transcribing…"
    static let paused = "Paused"
    static let ready = "Ready"

    /// A time or a duration is never negative, and a value that is not a number shows as zero.
    private static func fixed(_ value: Double, digits: Int) -> String {
        let shown = value.isFinite ? max(0, value) : 0
        return String(format: "%.\(digits)f", locale: Locale(identifier: "en_US_POSIX"), shown)
    }
}

/// What a transcriber says when it fails: the reason for the session controller and the text for the
/// user. No text here comes from the system or from a speech package.
enum TranscriberFailureText {
    static let noMicrophone = TranscriberFailure(.noMicrophone, message: "No usable microphone.")

    static let microphoneInterrupted = TranscriberFailure(
        .microphoneInterrupted,
        message: "Microphone interrupted. Resume when your call or other audio finishes."
    )

    static let routeChanged = TranscriberFailure(
        .routeChanged,
        message: "Microphone route changed. Resume to use the current microphone."
    )

    /// A streaming provider: the hand-off queue of 64 buffers is full.
    static let uploadOverflow = TranscriberFailure(
        .uploadOverflow,
        message: "Network cannot keep up. Recording paused; resume to retry."
    )

    /// An on-device provider: the hand-off queue of 128 buffers is full.
    static let processingOverflow = TranscriberFailure(
        .processingOverflow,
        message: "Transcription could not keep up. Recording paused; resume to continue."
    )

    /// An on-device recogniser threw. Its own text is never shown during a session.
    static let processingFailed = TranscriberFailure(
        .processingFailed,
        message: "Transcription stopped on this iPhone. Resume to continue."
    )

    /// Voz: thirty minutes of audio are waiting.
    static let pendingAudioLimit = TranscriberFailure(
        .pendingAudioLimit,
        message: "Pending recording reached 30 minutes and was transcribed. Resume to keep recording."
    )

    /// A streaming provider did not end the stream cleanly on Pause or Stop.
    static let finalizeFailed = TranscriberFailure(
        .stream,
        message: "Could not finalize the transcript. Some final words may be missing."
    )

    /// "Prepare Voz first." / "Prepare Parakeet first."
    static func notPrepared(_ provider: SpeechProvider) -> TranscriberFailure {
        TranscriberFailure(.notPrepared, message: "Prepare \(provider.shortName) first.")
    }

    /// The failure a capture event stands for; nil for a level.
    /// - Parameter onDevice: an overflow means "the network" for a streaming provider and "this iPhone"
    ///   for an on-device one.
    static func capture(_ event: AudioCaptureEvent, onDevice: Bool) -> TranscriberFailure? {
        switch event {
        case .level:
            return nil
        case .overflow:
            return onDevice ? processingOverflow : uploadOverflow
        case .interrupted:
            return microphoneInterrupted
        case .routeLost:
            return routeChanged
        }
    }
}
