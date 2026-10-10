import Foundation

/// The two answer services.
public enum AssistantProvider: String, Codable, CaseIterable, Sendable {
    case codex, openrouter

    /// The separator is a middle dot (U+00B7) with a space on each side.
    public var displayName: String {
        switch self {
        case .codex: return "PC · Codex"
        case .openrouter: return "OpenRouter · direct"
        }
    }
}
