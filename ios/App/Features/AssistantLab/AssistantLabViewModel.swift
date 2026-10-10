import Foundation
import LiveCueCore
import Observation

/// One row of "Recent comparisons".
struct ComparisonRow: Equatable, Identifiable {
    /// The turn's id.
    let id: UUID
    let model: String
    /// "<speech id> · <effort> · same text" or "… · fresh transcript"
    let detail: String
    /// "0.00 s total · <service name>"
    let timing: String
    let question: String
}

/// Assistant Lab and its sub-screens (screens 5).
@MainActor @Observable final class AssistantLabViewModel {
    var keyDraft: String = ""
    var searchText: String = ""

    init(
        assistant: AssistantSettings,
        relay: RelayMonitor,
        activity: SessionActivity,
        history: HistoryModel,
        session: SessionController
    ) {
        // STUB(WP-10)
    }

    var provider: AssistantProvider { .codex } // STUB(WP-10)
    /// An Assist is running: panels 1 to 3 are disabled.
    var isLocked: Bool { false } // STUB(WP-10)
    var selectedModelID: String { "" } // STUB(WP-10)
    /// "Reasoning: <effort>", Codex only.
    var reasoningCaption: String? { nil } // STUB(WP-10)
    var codexModels: [AssistantModelOption] { [] } // STUB(WP-10)
    /// The selected model is in the catalogue.
    var showsReasoningRow: Bool { false } // STUB(WP-10)
    /// Capitalised.
    var reasoningValue: String { "" } // STUB(WP-10)
    /// The selected model's, in catalogue order.
    var reasoningEfforts: [String] { [] } // STUB(WP-10)
    /// Codex and not paired.
    var showsPairFirst: Bool { false } // STUB(WP-10)
    /// "Choose a model" / "Change model"
    var openRouterModelRowTitle: String { "" } // STUB(WP-10)
    /// OpenRouter with no key saved.
    var keyPanelFirst: Bool { false } // STUB(WP-10)
    var hasKey: Bool { false } // STUB(WP-10)
    /// "Key saved" (success) / "No key saved" (neutral)
    var keyStateChip: StatusChipModel { StatusChipModel("", tone: .neutral, mark: .none) } // STUB(WP-10)
    var keyMessage: InlineMessage? { nil } // STUB(WP-10)
    var isVerifyingKey: Bool { false } // STUB(WP-10)
    /// "Refresh PC model list" / "Refresh OpenRouter models"
    var refreshTitle: String { "" } // STUB(WP-10)
    var isLoadingCatalogue: Bool { false } // STUB(WP-10)
    var catalogueMessage: InlineMessage? { nil } // STUB(WP-10)
    /// S-11 / S-12
    var explanation: String { "" } // STUB(WP-10)
    /// The 50 newest timed turns of the active and stored sessions.
    var comparisons: [ComparisonRow] { [] } // STUB(WP-10)
    /// Name or id contains `searchText`, case-insensitive.
    var filteredOpenRouterModels: [AssistantModelOption] { [] } // STUB(WP-10)

    /// `assistant.loadCatalogue()`
    func appeared() async {
        // STUB(WP-10)
    }

    /// Switch, then reload the catalogue.
    func selectProvider(_ provider: AssistantProvider) {
        // STUB(WP-10)
    }

    func selectCodexModel(_ option: AssistantModelOption) {
        // STUB(WP-10)
    }

    func selectEffort(_ effort: String) {
        // STUB(WP-10)
    }

    func selectOpenRouterModel(_ option: AssistantModelOption) {
        // STUB(WP-10)
    }

    /// Clears the draft on success.
    func saveKey() {
        // STUB(WP-10)
    }

    func verifyKey() async {
        // STUB(WP-10)
    }

    func removeKey() {
        // STUB(WP-10)
    }

    func refresh() async {
        // STUB(WP-10)
    }

    /// Run details.
    func turn(id: UUID) -> AssistantTurn? {
        // STUB(WP-10)
        nil
    }

    /// "None (no reasoning)" for "none", else capitalised.
    static func effortLabel(_ effort: String) -> String {
        // STUB(WP-10)
        effort
    }

    /// "Input $0.000 / 1M · output $0.000 / 1M"; "Unknown" per missing price.
    static func priceText(prompt: String?, completion: String?) -> String {
        // STUB(WP-10)
        ""
    }
}
