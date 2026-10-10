import Foundation

/// One entry of an answer-model catalogue.
public struct AssistantModelOption: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var name: String
    public var reasoningEfforts: [String]
    public var structuredOutputs: Bool?
    public var promptPrice: String?
    public var completionPrice: String?

    public init(
        id: String,
        name: String,
        reasoningEfforts: [String],
        structuredOutputs: Bool? = nil,
        promptPrice: String? = nil,
        completionPrice: String? = nil
    ) {
        self.id = id
        self.name = name
        self.reasoningEfforts = reasoningEfforts
        self.structuredOutputs = structuredOutputs
        self.promptPrice = promptPrice
        self.completionPrice = completionPrice
    }

    /// The effort to use after selecting this option: `current` when listed, else the first listed, else "low".
    public func effort(keeping current: String) -> String {
        // STUB(WP-02)
        current
    }
}

extension Array where Element == AssistantModelOption {
    /// True when an option has this id and lists this effort. The model check of every Assist.
    public func supports(_ configuration: AssistantConfiguration) -> Bool {
        // STUB(WP-02)
        false
    }
}
