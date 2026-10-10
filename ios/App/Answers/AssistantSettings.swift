import Foundation
import LiveCueCore
import Observation

/// The answer selection, the catalogue, the OpenRouter key and the instruction (spec 01 section 3.8;
/// spec 02 section 2.2).
@MainActor @Observable final class AssistantSettings {
    private(set) var configuration: AssistantConfiguration
    /// Saved on every change.
    var instruction: String
    /// Memory only.
    private(set) var catalogue: [AssistantModelOption] = []
    private(set) var catalogueMessage: InlineMessage? = nil
    private(set) var isLoadingCatalogue: Bool = false
    private(set) var hasOpenRouterKey: Bool
    private(set) var openRouterKeyMessage: InlineMessage? = nil
    private(set) var isVerifyingOpenRouterKey: Bool = false
    @ObservationIgnored var onProviderChanged: (@MainActor () -> Void)?

    init(
        preferences: any PreferencesStoring,
        secrets: any SecretStoring,
        pairingStore: any PairingStoring,
        answers: any AnswerServiceProviding,
        keyVerifier: any OpenRouterKeyVerifying,
        activity: SessionActivity
    ) {
        // STUB(WP-09)
        configuration = preferences.assistantConfiguration
        instruction = preferences.assistantInstruction
        hasOpenRouterKey = secrets.read(.openRouterAPIKey) != nil
    }

    /// Refused while assisting; the same provider does nothing.
    func switchProvider(_ provider: AssistantProvider) {
        // STUB(WP-09)
    }

    /// Keeps the effort when the option lists it.
    func selectCodexModel(_ option: AssistantModelOption) {
        // STUB(WP-09)
    }

    func selectReasoningEffort(_ effort: String) {
        // STUB(WP-09)
    }

    /// Effort "default".
    func selectOpenRouterModel(_ option: AssistantModelOption) {
        // STUB(WP-09)
    }

    /// Tagged by provider; a stale result is dropped.
    func loadCatalogue() async {
        // STUB(WP-09)
    }

    /// The Assist pipeline stores what it fetched.
    func adoptCatalogue(_ options: [AssistantModelOption]) {
        // STUB(WP-09)
    }

    @discardableResult
    func saveOpenRouterKey(_ text: String) -> Bool {
        // STUB(WP-09)
        false
    }

    func verifyOpenRouterKey() async {
        // STUB(WP-09)
    }

    func removeOpenRouterKey() {
        // STUB(WP-09)
    }
}
