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

/// Assistant Lab and its sub-screens (screens 5). Apart from the two drafts it stores nothing: every
/// property is read from the state owners.
@MainActor @Observable final class AssistantLabViewModel {
    var keyDraft: String = ""
    var searchText: String = ""

    private let assistant: AssistantSettings
    private let relay: RelayMonitor
    private let activity: SessionActivity
    private let history: HistoryModel
    private let session: SessionController

    init(
        assistant: AssistantSettings,
        relay: RelayMonitor,
        activity: SessionActivity,
        history: HistoryModel,
        session: SessionController
    ) {
        self.assistant = assistant
        self.relay = relay
        self.activity = activity
        self.history = history
        self.session = session
    }

    var provider: AssistantProvider {
        assistant.configuration.provider
    }

    /// An Assist is running: panels 1 to 3 are disabled.
    var isLocked: Bool {
        activity.isAssisting
    }

    var selectedModelID: String {
        assistant.configuration.model
    }

    /// "Reasoning: <effort>", Codex only.
    var reasoningCaption: String? {
        guard provider == .codex else {
            return nil
        }
        return "Reasoning: \(assistant.configuration.reasoningEffort)"
    }

    var codexModels: [AssistantModelOption] {
        provider == .codex ? assistant.catalogue : []
    }

    /// The selected model is in the catalogue.
    var showsReasoningRow: Bool {
        selectedCodexOption != nil
    }

    /// Capitalised.
    var reasoningValue: String {
        AssistantLabViewModel.capitalised(assistant.configuration.reasoningEffort)
    }

    /// The selected model's, in catalogue order.
    var reasoningEfforts: [String] {
        selectedCodexOption?.reasoningEfforts ?? []
    }

    /// Codex and not paired.
    var showsPairFirst: Bool {
        provider == .codex && !relay.isPaired
    }

    /// "Choose a model" / "Change model"
    var openRouterModelRowTitle: String {
        selectedModelID.isEmpty ? "Choose a model" : "Change model"
    }

    /// OpenRouter with no key saved.
    var keyPanelFirst: Bool {
        provider == .openrouter && !hasKey
    }

    var hasKey: Bool {
        assistant.hasOpenRouterKey
    }

    /// "Key saved" (success) / "No key saved" (neutral)
    var keyStateChip: StatusChipModel {
        if hasKey {
            return StatusChipModel("Key saved", tone: .success, mark: .check)
        }
        return StatusChipModel("No key saved", tone: .neutral, mark: .ring)
    }

    var keyMessage: InlineMessage? {
        assistant.openRouterKeyMessage
    }

    var isVerifyingKey: Bool {
        assistant.isVerifyingOpenRouterKey
    }

    /// "Refresh PC model list" / "Refresh OpenRouter models"
    var refreshTitle: String {
        switch provider {
        case .codex: return "Refresh PC model list"
        case .openrouter: return "Refresh OpenRouter models"
        }
    }

    var isLoadingCatalogue: Bool {
        assistant.isLoadingCatalogue
    }

    var catalogueMessage: InlineMessage? {
        assistant.catalogueMessage
    }

    /// S-11 / S-12
    var explanation: String {
        switch provider {
        case .codex: return AssistantLabViewModel.codexExplanation
        case .openrouter: return AssistantLabViewModel.openRouterExplanation
        }
    }

    /// The 50 newest timed turns of the active and stored sessions.
    var comparisons: [ComparisonRow] {
        var seen: Set<UUID> = []
        var timed: [(turn: AssistantTurn, performance: AssistPerformance)] = []
        for turn in allTurns {
            guard let performance = turn.performance, !seen.contains(turn.id) else {
                continue
            }
            seen.insert(turn.id)
            timed.append((turn: turn, performance: performance))
        }
        // Newest first. Answers made at the same instant keep the order they were found in.
        let newestFirst = timed.enumerated().sorted { first, second in
            if first.element.turn.createdAt != second.element.turn.createdAt {
                return first.element.turn.createdAt > second.element.turn.createdAt
            }
            return first.offset < second.offset
        }
        return newestFirst.prefix(AssistantLabViewModel.comparisonLimit).map { entry in
            AssistantLabViewModel.row(for: entry.element.turn, performance: entry.element.performance)
        }
    }

    /// Name or id contains `searchText`, case-insensitive.
    var filteredOpenRouterModels: [AssistantModelOption] {
        guard provider == .openrouter else {
            return []
        }
        let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !query.isEmpty else {
            return assistant.catalogue
        }
        return assistant.catalogue.filter { option in
            option.name.lowercased().contains(query) || option.id.lowercased().contains(query)
        }
    }

    /// `assistant.loadCatalogue()`
    func appeared() async {
        await assistant.loadCatalogue()
    }

    /// Switch, then reload the catalogue.
    func selectProvider(_ provider: AssistantProvider) {
        // The settings refuse a switch while an Assist runs; nothing is reloaded then.
        guard provider != assistant.configuration.provider, !activity.isAssisting else {
            return
        }
        assistant.switchProvider(provider)
        Task { await assistant.loadCatalogue() }
    }

    func selectCodexModel(_ option: AssistantModelOption) {
        assistant.selectCodexModel(option)
    }

    func selectEffort(_ effort: String) {
        assistant.selectReasoningEffort(effort)
    }

    func selectOpenRouterModel(_ option: AssistantModelOption) {
        assistant.selectOpenRouterModel(option)
    }

    /// Clears the draft on success.
    func saveKey() {
        if assistant.saveOpenRouterKey(keyDraft) {
            keyDraft = ""
        }
    }

    func verifyKey() async {
        await assistant.verifyOpenRouterKey()
    }

    func removeKey() {
        assistant.removeOpenRouterKey()
    }

    func refresh() async {
        await assistant.loadCatalogue()
    }

    /// Run details.
    func turn(id: UUID) -> AssistantTurn? {
        allTurns.first { $0.id == id }
    }

    /// "None (no reasoning)" for "none", else capitalised.
    static func effortLabel(_ effort: String) -> String {
        if effort == "none" {
            return "None (no reasoning)"
        }
        return capitalised(effort)
    }

    /// "Input $0.000 / 1M · output $0.000 / 1M"; "Unknown" per missing price.
    static func priceText(prompt: String?, completion: String?) -> String {
        "Input \(perMillion(prompt)) · output \(perMillion(completion))"
    }

    // MARK: - Internals

    /// The option of the selected Codex model, when the loaded catalogue lists it.
    private var selectedCodexOption: AssistantModelOption? {
        guard provider == .codex else {
            return nil
        }
        let model = assistant.configuration.model
        return assistant.catalogue.first { $0.id == model }
    }

    /// The turns of the active session, then those of every stored session.
    private var allTurns: [AssistantTurn] {
        let active = session.session?.assistantTurns ?? []
        return active + history.sessions.flatMap { $0.assistantTurns }
    }

    private static func row(for turn: AssistantTurn, performance: AssistPerformance) -> ComparisonRow {
        let configuration = performance.configuration
        let model = performance.execution?.model ?? configuration.model
        let effort = performance.execution?.reasoningEffort ?? configuration.reasoningEffort
        let source = performance.reusedText ? "same text" : "fresh transcript"
        let total = AnswerFormat.seconds(milliseconds: performance.totalMs, digits: 2)
        return ComparisonRow(
            id: turn.id,
            model: model,
            detail: "\(performance.speechModel) · \(effort) · \(source)",
            timing: "\(total) total · \(configuration.provider.displayName)",
            question: AnswerFormat.visibleQuestion(of: turn) ?? ""
        )
    }

    /// The first letter in upper case; the rest as it is.
    private static func capitalised(_ text: String) -> String {
        guard let first = text.first else {
            return text
        }
        return String(first).uppercased() + text.dropFirst()
    }

    /// "$3.000 / 1M" from a price per token; "Unknown" when it is missing, not a number, negative or
    /// not finite.
    private static func perMillion(_ price: String?) -> String {
        guard let price, let value = Double(price), value.isFinite, value >= 0 else {
            return "Unknown"
        }
        let formatted = String(format: "%.3f", locale: Locale(identifier: "en_US_POSIX"), value * 1_000_000)
        return "$\(formatted) / 1M"
    }

    private static let comparisonLimit = 50

    /// S-11 of spec 01 section 8.4.
    private static let codexExplanation = "Selection applies to your next Assist and session summary. None disables reasoning on supported models; it does not remove network or CLI startup time. Astra and Spark start at Low. No model is guaranteed to be fastest; test the same text."

    /// S-12 of spec 01 section 8.4.
    private static let openRouterExplanation = "Answers bypass the PC and Codex CLI. Models may differ in availability and price. There are no automatic paid retries or fallback models."
}
