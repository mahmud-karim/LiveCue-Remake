import SwiftUI

/// PC speech model (screens 8): start, stop and watch the speech model on the PC.
struct PCSpeechModelScreen: View {
    @Environment(PCSpeechModelViewModel.self) private var model

    /// S-04. The apostrophe is the typographic one of the spec.
    private static let caption = "Start opens Docker Desktop if needed. Stop unloads only LiveCue’s speech model; weights stay installed and Docker remains open. One PC model runs at a time."
    private static let otherModelNote = "Start switches to your selected model when it is idle."

    var body: some View {
        ScreenScaffold {
            Panel(model.title) {
                PCModelStateLine()
                PCModelMessageLine(text: model.message)
                if let other = model.otherLoadedModel {
                    Banner(tone: .info, title: other, message: PCSpeechModelScreen.otherModelNote)
                }
                PCModelButtons()
                CaptionText(PCSpeechModelScreen.caption)
            }
        }
        .navigationTitle("PC speech model")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            model.appeared()
        }
        .onDisappear {
            model.disappeared()
        }
    }
}

/// The message of the PC model: the PC's own text, or the phone's text for a failure.
///
/// The text can come from the relay, so it is drawn as it is: nothing in it is read as formatting or
/// becomes a link.
struct PCModelMessageLine: View {
    private let text: String

    init(text: String) {
        self.text = text
    }

    var body: some View {
        Text(verbatim: text)
            .lcText(.body)
            .frame(maxWidth: .infinity, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
            .accessibilityIdentifier("pc-model-message")
    }
}

/// A spinner while changing, the state, the "In use" chip, and the elapsed seconds of a change.
private struct PCModelStateLine: View {
    @Environment(PCSpeechModelViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.s2) {
                state
                trailing
            }
        } else {
            HStack(alignment: .center, spacing: theme.space.s3) {
                state
                Spacer(minLength: 0)
                trailing
            }
        }
    }

    private var state: some View {
        HStack(alignment: .center, spacing: theme.space.s2) {
            if model.isChanging {
                Spinner()
            }
            Text(verbatim: model.stateText)
                .lcText(.title, color: model.isError ? theme.status.danger : theme.ink.primary)
                .accessibilityIdentifier("pc-model-state")
        }
    }

    @ViewBuilder
    private var trailing: some View {
        if model.isInUse {
            StatusChip("In use", tone: .neutral, mark: .ring)
        }
        if let elapsed = model.elapsedText {
            Text(verbatim: elapsed)
                .lcText(.title, color: theme.ink.secondary, tabular: true)
        }
    }
}

/// "Start model" and "Stop model", then "Refresh status".
private struct PCModelButtons: View {
    @Environment(PCSpeechModelViewModel.self) private var model
    @Environment(\.theme) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: theme.space.s1) {
            FormButtonRow { fullWidth in
                LCButton("Start model", systemImage: "play.fill", fullWidth: fullWidth, action: start)
                    .disabled(!model.canStart)
                    .accessibilityIdentifier("start-pc-model")
                LCButton("Stop model", systemImage: "stop.fill", kind: .secondary, fullWidth: fullWidth, action: stop)
                    .disabled(!model.canStop)
                    .accessibilityIdentifier("stop-pc-model")
            }
            LCButton("Refresh status", systemImage: "arrow.clockwise", kind: .quiet, action: refresh)
                .accessibilityIdentifier("refresh-pc-model")
        }
    }

    private func start() {
        Task {
            await model.start()
        }
    }

    private func stop() {
        Task {
            await model.stop()
        }
    }

    private func refresh() {
        Task {
            await model.refresh()
        }
    }
}
