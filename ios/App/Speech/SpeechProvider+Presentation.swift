import Foundation
import LiveCueCore

/// Texts that name a speech provider on screen. The separator is a middle dot (U+00B7) with a space on
/// each side; the ellipsis is the single character U+2026.
extension SpeechProvider {
    var shortName: String {
        switch self {
        case .meta: return "Meta Muse"
        case .nemotron: return "Nemotron 3.5"
        case .qwen3: return "Qwen3 1.7B"
        case .voz: return "Voz"
        case .parakeet: return "Parakeet"
        case .apple: return "Apple speech"
        }
    }

    /// The row title on the Transcription screen.
    var pickerTitle: String {
        switch self {
        case .meta: return "Meta Muse · live cloud"
        case .nemotron: return "Nemotron 3.5 · PC live"
        case .qwen3: return "Qwen3 1.7B · PC live"
        case .voz: return "Voz · iPhone on Assist"
        case .parakeet: return "Parakeet · iPhone live"
        case .apple: return "Apple · iPhone live"
        }
    }

    /// The first description line on Home.
    var homeCaption: String {
        switch self {
        case .meta: return "Live captions with Meta Muse."
        case .nemotron, .qwen3: return "Live captions on your PC's GPU."
        case .voz, .parakeet, .apple: return "Transcription on your iPhone."
        }
    }

    /// The transcript placeholder while listening.
    var listeningPlaceholder: String {
        switch self {
        case .voz: return "Recording locally. Tap Assist to transcribe."
        case .meta, .nemotron, .qwen3, .parakeet, .apple: return "Listening for speech…"
        }
    }

    /// The transcript placeholder while connecting.
    var connectingPlaceholder: String {
        switch self {
        case .nemotron, .qwen3: return "Listening starts when the PC model is ready."
        case .meta, .voz, .parakeet, .apple: return "Listening starts when the connection is ready."
        }
    }

    /// The pinned package of an on-device provider; for Apple, that it is part of the system.
    var packageLabel: String? {
        switch self {
        case .voz: return "Desert Ant 3.1.0"
        case .parakeet: return "FluidAudio 0.15.6"
        case .apple: return "Built into iOS"
        case .meta, .nemotron, .qwen3: return nil
        }
    }

    /// S-01, S-02 or S-03 of spec 01 section 8.4.
    func explanation(answers: AssistantProvider) -> String {
        switch self {
        case .meta:
            return "Audio streams directly from this iPhone to Meta. No PC or model download. Cloud usage is billed by Meta. Recording stops if the connection fails; resume to reconnect."
        case .nemotron, .qwen3:
            return "Audio streams to the model on your PC's GPU. No speech API charges. Keep Docker Desktop and LiveCue Desktop running. Switching models may take a minute; recording starts only when ready."
        case .voz, .parakeet, .apple:
            switch answers {
            case .codex:
                return "Audio is transcribed on this iPhone. Only text is sent to your PC."
            case .openrouter:
                return "Audio is transcribed on this iPhone. Only text is sent to OpenRouter when you tap Assist."
            }
        }
    }
}
