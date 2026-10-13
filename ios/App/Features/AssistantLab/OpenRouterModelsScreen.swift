import LiveCueCore
import SwiftUI

/// The searchable OpenRouter model picker `openrouter-model-picker`.
///
/// One choice row per model: its name, then its id and its prices per million tokens. Choosing a model
/// selects it with the default reasoning behaviour and returns.
struct OpenRouterModelsScreen: View {
    @Environment(AssistantLabViewModel.self) private var model
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        @Bindable var model = model

        ScreenScaffold {
            if model.filteredOpenRouterModels.isEmpty {
                EmptyStateView(
                    systemImage: "magnifyingglass",
                    title: "No models",
                    message: "Refresh the catalog or change your search."
                )
            } else {
                Panel {
                    ForEach(model.filteredOpenRouterModels) { option in
                        row(for: option)
                    }
                }
            }
        }
        .navigationTitle("OpenRouter models")
        .navigationBarTitleDisplayMode(.inline)
        .searchable(
            text: $model.searchText,
            placement: .navigationBarDrawer(displayMode: .always),
            prompt: "Search models"
        )
    }

    private func row(for option: AssistantModelOption) -> some View {
        ChoiceRow(
            title: option.name,
            subtitle: subtitle(for: option),
            isSelected: option.id == model.selectedModelID
        ) {
            model.selectOpenRouterModel(option)
            dismiss()
        }
        .accessibilityIdentifier("choose-\(option.id)")
    }

    /// The id, then the prices, each on its own line.
    private func subtitle(for option: AssistantModelOption) -> String {
        let prices = AssistantLabViewModel.priceText(prompt: option.promptPrice, completion: option.completionPrice)
        return "\(option.id)\n\(prices)"
    }
}
