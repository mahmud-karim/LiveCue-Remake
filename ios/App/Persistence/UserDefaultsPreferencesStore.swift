import Foundation
import LiveCueCore

/// Preferences in `UserDefaults` (architecture 4.10).
///
/// | Key | Value |
/// |---|---|
/// | `assistantInstruction` | text; "" when absent |
/// | `speechProvider` | the provider's raw value; `meta` when absent or unknown |
/// | `assistantConfiguration` | JSON `{model, reasoningEffort, provider}`; codex / gpt-5.6-sol / low when absent |
/// | `assistantConfiguration.codex`, `assistantConfiguration.openrouter` | the same JSON, per answer service |
///
/// Every read goes to `defaults` and every write is made at once, so nothing is kept in this object.
@MainActor final class UserDefaultsPreferencesStore: PreferencesStoring {
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    var assistantInstruction: String {
        get { defaults.string(forKey: StorageNames.instructionKey) ?? "" }
        set { defaults.set(newValue, forKey: StorageNames.instructionKey) }
    }

    var speechProvider: SpeechProvider {
        get { SpeechProvider.stored(defaults.string(forKey: StorageNames.speechProviderKey)) }
        set { defaults.set(newValue.rawValue, forKey: StorageNames.speechProviderKey) }
    }

    var assistantConfiguration: AssistantConfiguration {
        get { configuration(forKey: StorageNames.configurationKey) ?? AssistantConfiguration() }
        set { store(newValue, forKey: StorageNames.configurationKey) }
    }

    func rememberedConfiguration(for provider: AssistantProvider) -> AssistantConfiguration? {
        configuration(forKey: UserDefaultsPreferencesStore.configurationKey(for: provider))
    }

    func remember(_ configuration: AssistantConfiguration) {
        store(configuration, forKey: UserDefaultsPreferencesStore.configurationKey(for: configuration.provider))
    }

    func resetAssistantConfiguration() {
        defaults.removeObject(forKey: StorageNames.configurationKey)
        for provider in AssistantProvider.allCases {
            defaults.removeObject(forKey: UserDefaultsPreferencesStore.configurationKey(for: provider))
        }
    }

    // MARK: - Keys and coding

    /// `assistantConfiguration.codex` and `assistantConfiguration.openrouter`.
    static func configurationKey(for provider: AssistantProvider) -> String {
        StorageNames.configurationKey + "." + provider.rawValue
    }

    /// Nil when the key is absent or does not hold a configuration.
    private func configuration(forKey key: String) -> AssistantConfiguration? {
        guard let data = defaults.data(forKey: key) else { return nil }
        return try? LiveCueJSON.decode(AssistantConfiguration.self, from: data)
    }

    private func store(_ configuration: AssistantConfiguration, forKey key: String) {
        guard let data = try? LiveCueJSON.encode(configuration) else { return }
        defaults.set(data, forKey: key)
    }
}
