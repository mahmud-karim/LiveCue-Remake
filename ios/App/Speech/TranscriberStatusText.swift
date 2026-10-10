import Foundation
import LiveCueCore

/// The status line texts of architecture 4.7. Numbers are formatted with the POSIX locale, so the
/// decimal separator is always ".".
enum TranscriberStatusText {
    /// "Connecting to <display name>…"
    static func connecting(_ provider: SpeechProvider) -> String {
        // STUB(WP-07)
        ""
    }

    /// "Loading <short name> on PC · <N> s. Microphone starts when ready."
    static func loading(_ provider: SpeechProvider, seconds: Int) -> String {
        // STUB(WP-07)
        ""
    }

    /// "Meta direct · 0.00 s setup" / "Connected · 0.00 s setup"
    static func connected(_ provider: SpeechProvider, setupSeconds: Double) -> String {
        // STUB(WP-07)
        ""
    }

    /// "First words · 0.00 s from stream start"
    static func firstWords(seconds: Double) -> String {
        // STUB(WP-07)
        ""
    }

    /// line + " · Final 0.00 s"
    static func appendingFinal(_ line: String, seconds: Double) -> String {
        // STUB(WP-07)
        line
    }

    /// "Last buffer processing: N ms"
    static func lastBuffer(milliseconds: Double) -> String {
        // STUB(WP-07)
        ""
    }

    /// "0.0 s audio → 0.00 s transcription"
    static func batch(audioSeconds: Double, transcriptionSeconds: Double) -> String {
        // STUB(WP-07)
        ""
    }

    static let recordingOnDevice = "Recording on this iPhone"
    static let disconnected = "Disconnected · resume to retry"
    static let finishing = "Finishing the transcript…"
}
