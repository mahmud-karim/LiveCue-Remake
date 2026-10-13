import LiveCueCore
import SwiftUI

/// Assistant Lab (screens 5).
///
/// Where answers come from, which model produces them, and how past answers performed. The catalogue of
/// the current service is loaded every time the screen appears. While an Assist runs, the service, the
/// model and the key cannot be changed.
struct AssistantLabScreen: View {
    @Environment(AssistantLabViewModel.self) private var model

    var body: some View {
        ScreenScaffold {
            if model.isLocked {
                CaptionText("Available when the current answer finishes.")
            }
            AnswerServicePanel()
            assistantPanels
            AssistantCataloguePanel()
            RecentComparisonsPanel()
        }
        .navigationTitle("Assistant Lab")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            await model.appeared()
        }
    }

    /// The model panel and, for OpenRouter, the key panel. Without a key the key panel comes first,
    /// because the key comes first.
    @ViewBuilder
    private var assistantPanels: some View {
        if model.keyPanelFirst {
            OpenRouterKeyPanel()
            AssistantModelPanel()
        } else {
            AssistantModelPanel()
            if model.provider == .openrouter {
                OpenRouterKeyPanel()
            }
        }
    }
}

/// The panel without a title: refresh, the catalogue message and the explanation of the service.
private struct AssistantCataloguePanel: View {
    @Environment(AssistantLabViewModel.self) private var model

    var body: some View {
        Panel {
            LCButton(
                model.refreshTitle,
                systemImage: "arrow.clockwise",
                kind: .quiet,
                isBusy: model.isLoadingCatalogue,
                action: refresh
            )
            if let message = model.catalogueMessage {
                if message.isError {
                    Banner(tone: .danger, title: "Model list not loaded", message: message.text)
                } else {
                    CaptionText(message.text)
                }
            }
            CaptionText(model.explanation)
        }
    }

    private func refresh() {
        Task { await model.refresh() }
    }
}
