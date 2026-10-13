import SwiftUI

/// The "System readiness" panel of Home: three rows that each fix what they report.
struct HomeReadinessPanel: View {
    @Environment(HomeViewModel.self) private var model

    var body: some View {
        Panel("System readiness", chip: model.readinessChip) {
            LCNavigationRow(
                route: .transcription,
                title: "Transcription",
                value: model.transcriptionValue,
                valueTone: transcriptionValueTone,
                systemImage: "waveform",
                identifier: "transcription-settings"
            ) {
                HomeTranscriptionSubtitle(detail: model.transcriptionDetail)
            }
            LCNavigationRow(
                route: .pairing,
                title: model.pairingTitle,
                value: model.pairingValue,
                systemImage: "desktopcomputer",
                identifier: "pair-pc"
            ) {
                CaptionText(model.pairingSubtitle, tone: pairingTone)
            }
            LCNavigationRow(
                route: .assistantLab,
                title: "Assistant models & timing",
                systemImage: "slider.horizontal.3",
                identifier: "assistant-lab"
            ) {
                CaptionText(model.assistantSubtitle, tone: assistantTone)
            }
        }
    }

    private var transcriptionValueTone: StatusTone? {
        model.transcriptionValueNeedsAttention ? .warning : nil
    }

    private var pairingTone: StatusTone? {
        model.pairingNeedsAttention ? .warning : nil
    }

    private var assistantTone: StatusTone? {
        model.assistantNeedsAttention ? .warning : nil
    }
}

/// The subtitle of the Transcription row: what the selected provider still needs, with the state text as
/// its own element so that it can be found by its identifier.
private struct HomeTranscriptionSubtitle: View {
    let detail: HomeTranscriptionDetail

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        switch detail {
        case .meta(let state, let isReady):
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s1) {
                CaptionText("Meta connection:")
                CaptionText(state, tone: isReady ? .success : .warning)
                    .accessibilityIdentifier("home-meta-state")
            }
        case .pcModel(let label, let canStart, let isChanging):
            HStack(alignment: .center, spacing: theme.space.s1) {
                CaptionText("PC speech model:")
                if isChanging {
                    Spinner(size: dynamicTypeSize.lcScaled(theme.component.smallSpinner))
                        .foregroundStyle(theme.status.warning)
                }
                CaptionText(label, tone: canStart ? .success : .warning)
                    .accessibilityIdentifier("home-pc-model-state")
            }
        case .onDevice(let text):
            CaptionText(text)
        }
    }
}
