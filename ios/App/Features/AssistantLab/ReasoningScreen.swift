import SwiftUI

/// The reasoning effort picker `reasoning-picker`.
///
/// The efforts of the selected Codex model, in the order the PC's catalogue lists them. Choosing one
/// selects it and returns.
struct ReasoningScreen: View {
    @Environment(AssistantLabViewModel.self) private var model
    @Environment(AssistantSettings.self) private var assistant
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        ScreenScaffold {
            Panel {
                ForEach(model.reasoningEfforts, id: \.self) { effort in
                    row(for: effort)
                }
            }
        }
        .navigationTitle("Reasoning")
        .navigationBarTitleDisplayMode(.inline)
    }

    private func row(for effort: String) -> some View {
        ChoiceRow(
            title: AssistantLabViewModel.effortLabel(effort),
            isSelected: effort == assistant.configuration.reasoningEffort
        ) {
            model.selectEffort(effort)
            dismiss()
        }
        .accessibilityIdentifier("effort-\(effort)")
    }
}
