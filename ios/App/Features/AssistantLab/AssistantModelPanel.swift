import LiveCueCore
import SwiftUI

/// The model panel of Assistant Lab: the Codex models and reasoning row, or the OpenRouter model row.
struct AssistantModelPanel: View {
    @Environment(AssistantLabViewModel.self) private var model
    @Environment(\.theme) private var theme

    var body: some View {
        Panel(title) {
            if model.showsPairFirst {
                pairFirst
            } else {
                selection
                rows
            }
        }
        .disabled(model.isLocked)
    }

    private var title: String {
        switch model.provider {
        case .codex: return "PC assistant"
        case .openrouter: return "OpenRouter assistant"
        }
    }

    /// Codex with nothing paired: the message and the way to the Pairing screen, nothing else.
    @ViewBuilder
    private var pairFirst: some View {
        CaptionText("Pair your PC first.")
        NavigationLink(value: Route.pairing) {
            HStack(spacing: theme.space.inlineGap) {
                LCGlyph("desktopcomputer", size: .regular)
                Text(verbatim: "Pair Windows PC")
            }
        }
        .buttonStyle(LCButtonStyle(kind: .quiet))
    }

    /// The selected model id, and for Codex the effort under it.
    private var selection: some View {
        VStack(alignment: .leading, spacing: theme.space.s1) {
            Text(verbatim: model.selectedModelID)
                .font(TextRole.headline.font.monospaced())
                .foregroundStyle(theme.ink.primary)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityIdentifier("selected-assistant-model")
            if let caption = model.reasoningCaption {
                CaptionText(caption)
            }
        }
    }

    @ViewBuilder
    private var rows: some View {
        switch model.provider {
        case .codex:
            ForEach(model.codexModels) { option in
                ChoiceRow(title: option.name, isSelected: option.id == model.selectedModelID) {
                    model.selectCodexModel(option)
                }
                .accessibilityIdentifier("choose-\(option.id)")
            }
            if model.showsReasoningRow {
                LCNavigationRow(
                    route: .reasoning,
                    title: "Reasoning",
                    value: model.reasoningValue,
                    identifier: "reasoning-picker"
                )
            }
        case .openrouter:
            LCNavigationRow(
                route: .openRouterModels,
                title: model.openRouterModelRowTitle,
                identifier: "openrouter-model-picker"
            )
            CaptionText("Uses the selected model's default reasoning behavior.")
        }
    }
}
