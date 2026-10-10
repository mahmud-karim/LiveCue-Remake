import Foundation

/// The selected answer model, its reasoning effort and the service that runs it.
public struct AssistantConfiguration: Codable, Equatable, Sendable {
    public var model: String
    public var reasoningEffort: String
    public var provider: AssistantProvider

    public init(model: String = "gpt-5.6-sol", reasoningEffort: String = "low", provider: AssistantProvider = .codex) {
        self.model = model
        self.reasoningEffort = reasoningEffort
        self.provider = provider
    }

    /// `provider` absent means `.codex`; it is always written.
    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        model = try container.decode(String.self, forKey: .model)
        reasoningEffort = try container.decode(String.self, forKey: .reasoningEffort)
        provider = try container.decodeIfPresent(AssistantProvider.self, forKey: .provider) ?? .codex
    }

    /// codex: gpt-5.6-sol / low. openrouter: "" / default.
    public static func `default`(for provider: AssistantProvider) -> AssistantConfiguration {
        switch provider {
        case .codex:
            return AssistantConfiguration()
        case .openrouter:
            return AssistantConfiguration(model: "", reasoningEffort: "default", provider: .openrouter)
        }
    }
}
