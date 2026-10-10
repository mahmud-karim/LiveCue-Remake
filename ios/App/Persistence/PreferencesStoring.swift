import Foundation
import LiveCueCore

@MainActor protocol PreferencesStoring: AnyObject {
    var assistantInstruction: String { get set }
    /// Unknown stored values read as `.meta`.
    var speechProvider: SpeechProvider { get set }
    /// The current selection; default codex / gpt-5.6-sol / low.
    var assistantConfiguration: AssistantConfiguration { get set }
    func rememberedConfiguration(for provider: AssistantProvider) -> AssistantConfiguration?
    /// Writes the key of the configuration's own provider.
    func remember(_ configuration: AssistantConfiguration)
    /// Removes the three configuration keys.
    func resetAssistantConfiguration()
}
