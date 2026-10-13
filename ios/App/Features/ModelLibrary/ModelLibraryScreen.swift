import LiveCueCore
import SwiftUI

/// Model Library (screens 11): download and load the on-device model of the current provider.
///
/// It lists exactly one model, and nothing for a provider that streams audio.
struct ModelLibraryScreen: View {
    @Environment(ModelLibraryViewModel.self) private var model

    var body: some View {
        ScreenScaffold {
            CaptionText(ModelLibraryCopy.intro)
            if let entry = model.model {
                SectionTitle("English models")
                ModelEntryPanel(entry: entry)
                SectionTitle("Pinned source")
                Panel {
                    DetailRow("Status", value: model.statusLine)
                    DetailRow("Package", value: model.packageLabel)
                }
            }
        }
        .navigationTitle("Model Library")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            model.appeared()
        }
        .onDisappear {
            model.disappeared()
        }
    }
}

/// The texts of this screen (spec 01 section 1.10; architecture 4.13).
private enum ModelLibraryCopy {
    static let intro = "Prepare the model for this tab once. The download is cached on your iPhone. Initial preparation can take longer than subsequent loads."
    static let vozUsage = "The Voz package reports anonymous usage to its vendor, Desert Ant Labs. LiveCue cannot turn this off."
    static let separateSteps = "Downloading and model preparation are separate steps; preparation can continue after the download reaches 100%."
}

/// The model: its name and chips, what it is, the two buttons, and its progress block while it is being
/// prepared or after its preparation failed.
private struct ModelEntryPanel: View {
    let entry: TranscriptionModel

    @Environment(ModelLibraryViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        Panel {
            header
            CaptionText(model.descriptionText)
            FormButtonRow { fullWidth in
                // Busy and unavailable while a preparation runs.
                LCButton(model.mainButtonTitle, fullWidth: fullWidth, isBusy: model.isPreparing, action: prepare)
                    .disabled(model.isPreparing)
                if model.isLoaded {
                    LCButton("Unload", kind: .secondary, fullWidth: fullWidth, action: unload)
                        .disabled(model.isPreparing)
                }
            }
            if model.showsVozUsageSentence {
                CaptionText(ModelLibraryCopy.vozUsage)
            }
            if let block = model.block {
                ModelPreparationBlockView(block: block)
            }
        }
    }

    /// The name, then the chips: beside it, or below it at the accessibility text sizes.
    @ViewBuilder
    private var header: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.s2) {
                name
                chips
            }
        } else {
            HStack(alignment: .center, spacing: theme.space.s2) {
                name
                Spacer(minLength: 0)
                chips
            }
        }
    }

    private var name: some View {
        Text(verbatim: entry.displayName)
            .lcText(.headline)
            .fixedSize(horizontal: false, vertical: true)
    }

    @ViewBuilder
    private var chips: some View {
        if entry.recommended {
            StatusChip("Recommended", tone: .live, mark: .none)
        }
        if model.isLoaded {
            StatusChip("Ready", tone: .success, mark: .check)
        }
    }

    private func prepare() {
        Task {
            await model.prepare()
        }
    }

    private func unload() {
        model.unload()
    }
}

/// The bar, the percentage and the elapsed time, the status line, the counter and the note about the
/// two steps.
private struct ModelPreparationBlockView: View {
    let block: PreparationBlock

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        VStack(alignment: .leading, spacing: theme.space.s2) {
            LCProgressBar(value: block.fraction, failed: block.failed)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Setup progress")
                .accessibilityValue(block.percentText)
                .accessibilityIdentifier("model-download-progress")
            figures
            Text(verbatim: block.statusLine)
                .lcText(.body)
                .fixedSize(horizontal: false, vertical: true)
            if let counter = block.counterText {
                CaptionText(counter)
            }
            CaptionText(ModelLibraryCopy.separateSteps)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// "N% setup" and "MM:SS elapsed": on one line, or one under the other when the text is large.
    @ViewBuilder
    private var figures: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.half) {
                percent
                elapsed
            }
        } else {
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s2) {
                percent
                Spacer(minLength: 0)
                elapsed
            }
        }
    }

    private var percent: some View {
        Text(verbatim: block.percentText)
            .lcText(.label, tabular: true)
    }

    private var elapsed: some View {
        Text(verbatim: block.elapsedText)
            .lcText(.caption, color: theme.ink.secondary, tabular: true)
    }
}
