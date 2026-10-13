import LiveCueCore
import SwiftUI

/// The "Answer service" panel `assistant-provider`: the two services as choice rows.
///
/// Tapping a row switches the service at once and reloads the catalogue. The panel is one container for
/// assistive technology, and its value is the selected service's name.
struct AnswerServicePanel: View {
    @Environment(AssistantLabViewModel.self) private var model

    var body: some View {
        Panel("Answer service") {
            ChoiceRow(
                title: AssistantProvider.codex.displayName,
                subtitle: "Through your paired PC. Adds a summary when you stop.",
                isSelected: model.provider == .codex
            ) {
                model.selectProvider(.codex)
            }
            .accessibilityIdentifier("assistant-provider-codex")
            ChoiceRow(
                title: AssistantProvider.openrouter.displayName,
                subtitle: "Straight from this iPhone. Answers stream in.",
                isSelected: model.provider == .openrouter
            ) {
                model.selectProvider(.openrouter)
            }
            .accessibilityIdentifier("assistant-provider-openrouter")
        }
        .disabled(model.isLocked)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("assistant-provider")
        .accessibilityValue(model.provider.displayName)
    }
}
