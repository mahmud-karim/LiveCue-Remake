import SwiftUI

/// Settings, the root of the Settings tab (screens 6): everything that is set once and left alone.
///
/// It cannot be open during a session, so nothing here has a "session active" state.
struct SettingsScreen: View {
    @Environment(SettingsViewModel.self) private var model

    var body: some View {
        ScreenScaffold {
            SettingsTranscriptionPanel()
            SettingsAssistantPanel()
            SettingsDevicesPanel()
            SettingsCostPanel()
            SettingsDisplayPanel()
            SettingsAboutPanel()
            SettingsPrivacyPanel()
        }
        .navigationTitle("Settings")
        .toolbar {
            ToolbarItemGroup(placement: .keyboard) {
                Spacer()
                Button("Done") {
                    FormKeyboard.dismiss()
                }
                .accessibilityIdentifier("finish-instruction")
            }
        }
        .onAppear {
            model.appeared()
        }
        .onDisappear {
            model.disappeared()
        }
    }
}

/// The texts of the Settings screen that no owner publishes (spec 01 section 8.4; architecture 4.13).
private enum SettingsCopy {
    static let instructionPlaceholder = "Optional instruction (e.g. answer briefly)"
    static let instructionCaption = "Applied to every Assist request until you change it."
    /// S-05.
    static let cost = "Live estimates use audio sent; completed streams use Meta's reported processed audio, rounded down to whole seconds per stream. Silence sent to Meta counts as audio. Credits and billing adjustments are not included. Interrupted streams may have incomplete usage. This estimates transcription only, not assistant usage."
    static let pricingTitle = "Meta pricing & usage documentation"
    static let pricingAddress = "https://dev.meta.ai/docs/speech-to-text#pricing"
    static let speechPackages = "On-device speech uses Voz by Desert Ant Labs (desert-ant-core 3.1.0, Desert Ant Labs Source-Available License 1.0) and FluidAudio 0.15.6 (Apache License 2.0)."
    static let usageReporting = "The Voz package reports anonymous usage to its vendor, Desert Ant Labs. LiveCue cannot turn this off."
    /// S-06, with the sentence decisions 6.1 adds.
    static let privacy = "Meta and OpenRouter API keys stay in this iPhone's Keychain and go only to their respective services. Cloud audio goes directly to Meta; cloud Assist text goes directly to OpenRouter. PC-local speech and Codex Assist still use your paired PC. Transcripts and replies are saved in iPhone history. Obtain permission before recording others. The Voz speech package reports anonymous usage to its vendor."
}

/// The provider, what it needs, and what it does with the audio.
private struct SettingsTranscriptionPanel: View {
    @Environment(SettingsViewModel.self) private var model

    var body: some View {
        Panel("Transcription") {
            LCNavigationRow(
                route: Route.transcription,
                title: "Provider",
                value: model.providerName,
                systemImage: "waveform",
                identifier: "speech-provider"
            )
            .disabled(model.providerLockCaption != nil)
            ProviderSetupRows(state: model.setup)
            if let caption = model.providerLockCaption {
                CaptionText(caption)
            }
            CaptionText(model.explanation)
        }
    }
}

/// Assistant Lab and the instruction sent with every Assist.
private struct SettingsAssistantPanel: View {
    @Environment(SettingsViewModel.self) private var model

    var body: some View {
        @Bindable var model = model

        Panel("Assistant") {
            LCNavigationRow(
                route: Route.assistantLab,
                title: "Assistant models & timing",
                systemImage: "slider.horizontal.3",
                identifier: "assistant-lab"
            ) {
                Text(verbatim: model.assistantSubtitle)
            }
            LCTextField(
                "Assistant instructions",
                text: $model.instruction,
                placeholder: SettingsCopy.instructionPlaceholder,
                lines: 2...5,
                identifier: "assistant-instruction"
            )
            CaptionText(SettingsCopy.instructionCaption)
        }
    }
}

/// The paired PC, or the way to pair one.
private struct SettingsDevicesPanel: View {
    @Environment(SettingsViewModel.self) private var model

    var body: some View {
        Panel("Devices") {
            if let subtitle = model.pairingSubtitle {
                LCNavigationRow(
                    route: Route.pairing,
                    title: model.pairingTitle,
                    systemImage: "desktopcomputer",
                    identifier: "pair-pc"
                ) {
                    Text(verbatim: subtitle)
                }
            } else {
                LCNavigationRow(
                    route: Route.pairing,
                    title: model.pairingTitle,
                    systemImage: "desktopcomputer",
                    identifier: "pair-pc"
                )
            }
        }
    }
}

private struct SettingsCostPanel: View {
    var body: some View {
        Panel("Transcription cost") {
            DetailRow("Meta Muse rate", value: "$0.18 / hour")
            DetailRow("PC-local models", value: "$0 API usage")
            CaptionText(SettingsCopy.cost)
            ExternalLinkButton(SettingsCopy.pricingTitle, address: SettingsCopy.pricingAddress)
        }
    }
}

private struct SettingsDisplayPanel: View {
    var body: some View {
        Panel("Display") {
            DetailRow("Text size", value: "Follows your iPhone's text size")
        }
    }
}

/// Appearance, version, and the two statements about the speech packages.
private struct SettingsAboutPanel: View {
    @Environment(SettingsViewModel.self) private var model

    var body: some View {
        Panel("About") {
            DetailRow("Appearance", value: "Daylight")
            DetailRow("Version", value: model.versionText)
            SettingsNote(title: "Speech packages", text: SettingsCopy.speechPackages)
            SettingsNote(title: "Usage reporting", text: SettingsCopy.usageReporting)
        }
    }
}

private struct SettingsPrivacyPanel: View {
    var body: some View {
        Panel("Privacy") {
            CaptionText(SettingsCopy.privacy)
        }
    }
}

/// A caption with a short title above it.
private struct SettingsNote: View {
    let title: String
    let text: String

    @Environment(\.theme) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: theme.space.half) {
            Text(verbatim: title)
                .font(TextRole.caption.font.weight(.semibold))
                .foregroundStyle(theme.ink.primary)
                .fixedSize(horizontal: false, vertical: true)
            CaptionText(text)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }
}
