import Foundation

enum SessionPhase: Equatable, Sendable {
    case idle, preflight, connecting, listening, pausing, paused, resuming, ending
}

enum AssistStage: Equatable, Sendable {
    case transcribing, thinking, checkingModel, replying

    /// The ellipsis is the single character U+2026.
    var label: String {
        switch self {
        case .transcribing: return "Transcribing…"
        case .thinking: return "Thinking…"
        case .checkingModel: return "Checking model…"
        case .replying: return "Replying…"
        }
    }
}

/// The banner of the live screen: why the session is paused.
struct SessionNotice: Equatable, Sendable {
    enum Kind: Equatable, Sendable {
        case recordingPaused, finalizeFailed
    }

    let kind: Kind
    let message: String

    var title: String {
        switch kind {
        case .recordingPaused: return "Recording paused"
        case .finalizeFailed: return "Paused"
        }
    }
}
