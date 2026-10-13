import SwiftUI

/// Speech benchmark (screens 12): a manual word-error-rate worksheet for comparing speech models.
struct BenchmarkScreen: View {
    var body: some View {
        ScreenScaffold {
            Panel("Purpose") {
                CaptionText(BenchmarkCopy.purpose)
            }
            BenchmarkWorksheetPanel()
            BenchmarkRatePanel()
        }
        .navigationTitle("Speech benchmark")
        .navigationBarTitleDisplayMode(.inline)
    }
}

/// The texts of this screen (screens 12; decisions 6.1).
private enum BenchmarkCopy {
    static let purpose = "Record the same short phrase with different models, then compare processing time and word error rate."
    static let interpretation = "Lower is better. Test in the same room and speak the same phrase for a fair comparison."
}

/// The two texts that are compared, and the button that fills the second one.
private struct BenchmarkWorksheetPanel: View {
    @Environment(BenchmarkViewModel.self) private var model

    var body: some View {
        @Bindable var model = model

        Panel {
            LCTextField("Reference phrase", text: $model.reference, lines: 2...5)
            LCTextField("Model transcript", text: $model.hypothesis, lines: 2...8)
            LCButton("Use latest transcript", systemImage: "text.alignleft", kind: .secondary, action: useLatest)
                .disabled(!model.canUseLatest)
            CaptionText(model.latestCaption)
        }
    }

    private func useLatest() {
        model.useLatestTranscript()
    }
}

/// The rate, updated as either text changes.
private struct BenchmarkRatePanel: View {
    @Environment(BenchmarkViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        Panel {
            figure
                .accessibilityElement(children: .combine)
            CaptionText(BenchmarkCopy.interpretation)
        }
    }

    /// The title and the rate: on one line, or one under the other when the text is large.
    @ViewBuilder
    private var figure: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.s1) {
                title
                rate
            }
        } else {
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s3) {
                title
                Spacer(minLength: 0)
                rate
            }
        }
    }

    private var title: some View {
        Text(verbatim: "Word error rate")
            .lcText(.headline)
            .fixedSize(horizontal: false, vertical: true)
    }

    private var rate: some View {
        Text(verbatim: model.rateText)
            .lcText(.display, tabular: true)
            .lineLimit(1)
    }
}
