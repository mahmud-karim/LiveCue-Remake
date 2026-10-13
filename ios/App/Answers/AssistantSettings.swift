import Foundation
import LiveCueCore
import Observation

/// The answer selection, the catalogue, the OpenRouter key and the instruction (spec 01 section 3.8;
/// spec 02 section 2.2).
@MainActor @Observable final class AssistantSettings {
    private(set) var configuration: AssistantConfiguration
    /// Saved on every change.
    var instruction: String {
        get {
            access(keyPath: \.instruction)
            return storedInstruction
        }
        set {
            withMutation(keyPath: \.instruction) {
                storedInstruction = newValue
            }
            preferences.assistantInstruction = newValue
        }
    }
    /// Memory only.
    private(set) var catalogue: [AssistantModelOption] = []
    private(set) var catalogueMessage: InlineMessage? = nil
    private(set) var isLoadingCatalogue: Bool = false
    private(set) var hasOpenRouterKey: Bool
    private(set) var openRouterKeyMessage: InlineMessage? = nil
    private(set) var isVerifyingOpenRouterKey: Bool = false
    @ObservationIgnored var onProviderChanged: (@MainActor () -> Void)?

    private let preferences: any PreferencesStoring
    private let secrets: any SecretStoring
    private let pairingStore: any PairingStoring
    private let answers: any AnswerServiceProviding
    private let keyVerifier: any OpenRouterKeyVerifying
    private let activity: SessionActivity

    /// What `instruction` reads and writes. The property itself is computed, so that a change is saved.
    @ObservationIgnored private var storedInstruction: String
    /// The tag of the newest catalogue load. A load that ends with an older tag is stale.
    @ObservationIgnored private var catalogueLoadTag = 0
    /// The service whose catalogue is loading, if one is.
    @ObservationIgnored private var catalogueLoadProvider: AssistantProvider?

    init(
        preferences: any PreferencesStoring,
        secrets: any SecretStoring,
        pairingStore: any PairingStoring,
        answers: any AnswerServiceProviding,
        keyVerifier: any OpenRouterKeyVerifying,
        activity: SessionActivity
    ) {
        self.preferences = preferences
        self.secrets = secrets
        self.pairingStore = pairingStore
        self.answers = answers
        self.keyVerifier = keyVerifier
        self.activity = activity
        storedInstruction = preferences.assistantInstruction
        configuration = preferences.assistantConfiguration
        hasOpenRouterKey = secrets.read(.openRouterAPIKey) != nil
    }

    // MARK: - Selection

    /// Refused while assisting; the same provider does nothing.
    func switchProvider(_ provider: AssistantProvider) {
        guard !activity.isAssisting, provider != configuration.provider else { return }

        // The outgoing service keeps its own model and effort; the incoming one gets its own back.
        preferences.remember(configuration)
        let incoming = preferences.rememberedConfiguration(for: provider) ?? AssistantConfiguration.default(for: provider)
        apply(AssistantConfiguration(model: incoming.model, reasoningEffort: incoming.reasoningEffort, provider: provider))

        // The catalogue belonged to the other service. A load still in flight for it is now stale.
        catalogue = []
        catalogueMessage = nil
        catalogueLoadTag += 1
        catalogueLoadProvider = nil
        isLoadingCatalogue = false

        onProviderChanged?()
    }

    /// Keeps the effort when the option lists it.
    func selectCodexModel(_ option: AssistantModelOption) {
        guard configuration.provider == .codex else { return }
        var changed = configuration
        changed.model = option.id
        changed.reasoningEffort = option.effort(keeping: configuration.reasoningEffort)
        apply(changed)
    }

    func selectReasoningEffort(_ effort: String) {
        guard configuration.provider == .codex else { return }
        var changed = configuration
        changed.reasoningEffort = effort
        apply(changed)
    }

    /// Effort "default".
    func selectOpenRouterModel(_ option: AssistantModelOption) {
        guard configuration.provider == .openrouter else { return }
        var changed = configuration
        changed.model = option.id
        changed.reasoningEffort = AssistantSettings.openRouterEffort
        apply(changed)
    }

    // MARK: - Catalogue

    /// Tagged by provider; a stale result is dropped.
    func loadCatalogue() async {
        let provider = configuration.provider
        // A second load for the same service while one is running is ignored.
        guard catalogueLoadProvider != provider else { return }

        // Nothing is asked of a PC that is not paired (R-29).
        if provider == .codex, pairingStore.load() == nil {
            catalogueMessage = InlineMessage(AssistantSettings.pairFirstMessage)
            return
        }

        catalogueLoadTag += 1
        let tag = catalogueLoadTag
        catalogueLoadProvider = provider
        isLoadingCatalogue = true

        let service = answers.service(for: provider)
        let loaded: Result<[AssistantModelOption], any Error>
        do {
            loaded = .success(try await service.loadCatalogue())
        } catch {
            loaded = .failure(error)
        }

        // The service changed, or a newer load began: this result is stale and says nothing.
        guard tag == catalogueLoadTag else { return }
        catalogueLoadProvider = nil
        isLoadingCatalogue = false

        switch loaded {
        case .success(let options):
            catalogue = options
            catalogueMessage = InlineMessage(AssistantSettings.loadedMessage(for: provider))
        case .failure(let error):
            // Cancellation is never shown. The rows that were loaded before stay.
            if let text = AssistantSettings.catalogueFailureText(for: error, provider: provider) {
                catalogueMessage = InlineMessage(text, isError: true)
            }
        }
    }

    /// The Assist pipeline stores what it fetched.
    func adoptCatalogue(_ options: [AssistantModelOption]) {
        catalogue = options
    }

    // MARK: - OpenRouter key

    @discardableResult
    func saveOpenRouterKey(_ text: String) -> Bool {
        switch APIKeyShape.check(text) {
        case .empty:
            openRouterKeyMessage = InlineMessage(AssistantSettings.invalidKeyMessage, isError: true)
            return false
        case .containsWhitespace:
            openRouterKeyMessage = InlineMessage(AssistantSettings.text(of: OpenRouterError.malformedKey), isError: true)
            return false
        case .valid:
            break
        }

        do {
            try secrets.save(text, for: .openRouterAPIKey)
        } catch {
            openRouterKeyMessage = InlineMessage(AssistantSettings.couldNotSaveMessage(for: error), isError: true)
            return false
        }
        hasOpenRouterKey = true
        openRouterKeyMessage = InlineMessage(AssistantSettings.keySavedMessage)
        return true
    }

    func verifyOpenRouterKey() async {
        guard !isVerifyingOpenRouterKey else { return }
        guard let key = secrets.read(.openRouterAPIKey) else {
            hasOpenRouterKey = false
            openRouterKeyMessage = InlineMessage(AssistantSettings.text(of: OpenRouterError.missingKey), isError: true)
            return
        }

        isVerifyingOpenRouterKey = true
        do {
            try await keyVerifier.verify(key: key)
            openRouterKeyMessage = InlineMessage(AssistantSettings.keyVerifiedMessage)
        } catch let failure as OpenRouterError {
            openRouterKeyMessage = InlineMessage(AssistantSettings.text(of: failure), isError: true)
        } catch is CancellationError {
            // Never shown.
        } catch {
            openRouterKeyMessage = InlineMessage(AssistantSettings.text(of: OpenRouterError.unavailable), isError: true)
        }
        isVerifyingOpenRouterKey = false
    }

    func removeOpenRouterKey() {
        do {
            try secrets.remove(.openRouterAPIKey)
        } catch {
            openRouterKeyMessage = InlineMessage(AssistantSettings.couldNotRemoveMessage(for: error), isError: true)
            return
        }
        hasOpenRouterKey = false
        openRouterKeyMessage = InlineMessage(AssistantSettings.keyRemovedMessage)
    }

    // MARK: - Internals

    /// Every change of the selection is saved at once, as the current selection and under the key of
    /// its own service.
    private func apply(_ changed: AssistantConfiguration) {
        configuration = changed
        preferences.assistantConfiguration = changed
        preferences.remember(changed)
    }

    // MARK: - Texts (spec 01 section 8.2)

    private static let openRouterEffort = "default"
    private static let pairFirstMessage = "Pair your PC first."
    private static let invalidKeyMessage = "Enter a valid API key."
    private static let keySavedMessage = "Key saved securely on this iPhone."
    private static let keyVerifiedMessage = "Key verified. No inference request was made."
    private static let keyRemovedMessage = "Key removed."

    private static func loadedMessage(for provider: AssistantProvider) -> String {
        switch provider {
        case .codex:
            return "From this PC's Codex catalog. Access still depends on your subscription."
        case .openrouter:
            return "Direct from OpenRouter. Choose a model; API charges are separate from your Codex subscription."
        }
    }

    /// Nil for a cancellation, which is never shown.
    private static func catalogueFailureText(for error: any Error, provider: AssistantProvider) -> String? {
        if error is CancellationError {
            return nil
        }
        switch provider {
        case .codex:
            let failure = (error as? RelayFailure) ?? RelayFailure.local(.network)
            return RelayMessages.catalogueFailure(failure)
        case .openrouter:
            let failure = (error as? OpenRouterError) ?? OpenRouterError.unavailable
            return text(of: failure)
        }
    }

    private static func text(of failure: OpenRouterError) -> String {
        failure.errorDescription ?? ""
    }

    private static func couldNotSaveMessage(for error: any Error) -> String {
        "Could not save the key in Keychain (status \(status(of: error))). No insecure fallback was used."
    }

    private static func couldNotRemoveMessage(for error: any Error) -> String {
        "Keychain could not remove this key (status \(status(of: error))). Try again."
    }

    /// The Keychain status a store reported; -1 for an error that carries none.
    private static func status(of error: any Error) -> Int32 {
        (error as? SecretStoreFailure)?.status ?? -1
    }
}
