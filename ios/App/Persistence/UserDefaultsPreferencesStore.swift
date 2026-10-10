import Foundation
import LiveCueCore

/// Preferences in `UserDefaults` (architecture 4.10).
@MainActor final class UserDefaultsPreferencesStore: PreferencesStoring {
    // STUB(WP-06)
    // Values live in memory only until this store reads and writes `defaults`.
    private let defaults: UserDefaults
    private var remembered: [AssistantProvider: AssistantConfiguration] = [:]

    var assistantInstruction: String = ""
    var speechProvider: SpeechProvider = .meta
    var assistantConfiguration = AssistantConfiguration()

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    func rememberedConfiguration(for provider: AssistantProvider) -> AssistantConfiguration? {
        // STUB(WP-06)
        remembered[provider]
    }

    func remember(_ configuration: AssistantConfiguration) {
        // STUB(WP-06)
        remembered[configuration.provider] = configuration
    }

    func resetAssistantConfiguration() {
        // STUB(WP-06)
        remembered.removeAll()
        assistantConfiguration = AssistantConfiguration()
    }
}
