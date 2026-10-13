import Foundation

/// What a capture does when the input is not usable, and what it says when it gives up.
///
/// Nothing here touches the audio session: the values come in as numbers and texts, so the decisions are
/// tested without a microphone. `AudioCapture` reads the values and acts on the answers.
enum AudioInputDiagnosis {
    /// How often the input is read before a start gives up. Each attempt uses a new engine.
    static let attempts = 3
    /// The pause between two attempts: the route can take a moment to settle after the activation.
    static let retryDelaySeconds: TimeInterval = 0.2

    enum Step: Equatable, Sendable {
        /// The input can be tapped.
        case proceed
        case retry(afterSeconds: TimeInterval)
        case giveUp
    }

    /// A format the tap can be installed with: a sample rate and at least one channel.
    static func isUsable(sampleRate: Double, channelCount: Int) -> Bool {
        sampleRate > 0 && channelCount > 0
    }

    /// What follows attempt number `attempt`, counted from 1.
    static func step(afterAttempt attempt: Int, formatUsable: Bool, converterBuilt: Bool) -> Step {
        if formatUsable && converterBuilt {
            return .proceed
        }
        return attempt < attempts ? .retry(afterSeconds: retryDelaySeconds) : .giveUp
    }

    /// iOS gives the app no microphone at all: it says that no input is available, or the route of the
    /// active session has no input. The route of a session that could not be activated says nothing.
    static func givesNoInput(_ report: AudioStartReport) -> Bool {
        if !report.isInputAvailable {
            return true
        }
        if case .session = report.stage {
            return false
        }
        return report.routeInputs.isEmpty
    }

    /// The text for the user: what is wrong, then the figures in brackets, so that a screenshot says
    /// what happened.
    static func message(for report: AudioStartReport) -> String {
        let lead = givesNoInput(report) ? noInputText : noUsableMicrophoneText
        return lead + " " + detail(report)
    }

    /// "(input 0 Hz, 0 ch, route: none)". Port types only: no name of a device or of a person.
    static func detail(_ report: AudioStartReport) -> String {
        var parts: [String] = []
        let figures = "input \(whole(report.sampleRate)) Hz, \(max(0, report.channelCount)) ch"
        switch report.stage {
        case .session(let code):
            parts.append("audio session error \(code)")
        case .input:
            parts.append(figures)
        case .engine(let code):
            parts.append("audio engine error \(code)")
            parts.append(figures)
        }

        let ports = report.routeInputs.prefix(maximumPorts)
        parts.append("route: " + (ports.isEmpty ? "none" : ports.joined(separator: " + ")))
        if !report.isInputAvailable {
            parts.append("input unavailable")
        }
        if report.stage == .input,
           isUsable(sampleRate: report.sampleRate, channelCount: report.channelCount),
           !report.converterBuilt {
            parts.append("no converter")
        }
        return "(" + parts.joined(separator: ", ") + ")"
    }

    static let noUsableMicrophoneText = "No usable microphone."
    static let noInputText = "iOS is giving this app no microphone. In LiveContainer, open the app normally, not in a multitasking window, and check that LiveContainer has microphone access in Settings."

    private static let maximumPorts = 3

    /// A whole number of hertz, with digits that do not follow the device's locale.
    private static func whole(_ value: Double) -> String {
        let shown = value.isFinite ? max(0, value) : 0
        return String(format: "%.0f", locale: Locale(identifier: "en_US_POSIX"), shown)
    }
}
