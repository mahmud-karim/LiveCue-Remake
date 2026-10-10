import Foundation

/// The five speech providers, in catalogue order.
public enum SpeechProvider: String, Codable, CaseIterable, Sendable {
    case meta, nemotron, qwen3, voz, parakeet

    /// Spec 02 section 7.1. The separator is a middle dot (U+00B7) with a space on each side.
    public var displayName: String {
        switch self {
        case .meta: return "Meta Muse · cloud"
        case .nemotron: return "Nemotron 3.5 · PC"
        case .qwen3: return "Qwen3 1.7B · PC"
        case .voz: return "Voz · iPhone"
        case .parakeet: return "Parakeet · iPhone"
        }
    }

    public var isCloud: Bool {
        self == .meta
    }

    public var isPCLocal: Bool {
        self == .nemotron || self == .qwen3
    }

    public var isOnDevice: Bool {
        self == .voz || self == .parakeet
    }

    public var streamsAudio: Bool {
        isCloud || isPCLocal
    }

    /// 24000 for meta, otherwise 16000.
    public var sampleRate: Int {
        self == .meta ? 24_000 : 16_000
    }

    /// `.meta` for nil or any unknown raw value.
    public static func stored(_ rawValue: String?) -> SpeechProvider {
        guard let rawValue, let provider = SpeechProvider(rawValue: rawValue) else { return .meta }
        return provider
    }
}
