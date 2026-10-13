import LiveCueCore
import SwiftUI

/// Transcription (screens 7): the provider list and the selected provider's setup rows.
///
/// Choosing a provider stays here, so the second panel can show what the new provider needs.
struct TranscriptionScreen: View {
    @Environment(TranscriptionViewModel.self) private var model

    var body: some View {
        ScreenScaffold {
            TranscriptionProviderPanel()
            Panel(model.selectedTitle) {
                ProviderSetupRows(state: model.setup)
                CaptionText(model.explanation)
            }
        }
        .navigationTitle("Transcription")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            model.appeared()
        }
        .onDisappear {
            model.disappeared()
        }
    }
}

/// The six providers as choice rows. A provider this iPhone cannot run says why and cannot be chosen.
private struct TranscriptionProviderPanel: View {
    @Environment(TranscriptionViewModel.self) private var model

    private static let caption = "PC models run one at a time on your GPU. Start a new conversation to compare them. Neither sends audio to Meta."

    var body: some View {
        Panel {
            ForEach(model.providers, id: \.self) { (provider: SpeechProvider) in
                ChoiceRow(
                    title: provider.pickerTitle,
                    subtitle: model.unavailableReason(for: provider),
                    isSelected: provider == model.selected
                ) {
                    model.select(provider)
                }
                .disabled(model.lockCaption != nil || model.unavailableReason(for: provider) != nil)
                .accessibilityIdentifier(TranscriptionProviderPanel.identifier(of: provider))
            }
            CaptionText(TranscriptionProviderPanel.caption)
            if let caption = model.lockCaption {
                CaptionText(caption)
            }
        }
    }

    /// `provider-meta`, `provider-nemotron`, `provider-qwen3`, `provider-voz`, `provider-parakeet`,
    /// `provider-apple`
    private static func identifier(of provider: SpeechProvider) -> String {
        "provider-" + provider.rawValue
    }
}
