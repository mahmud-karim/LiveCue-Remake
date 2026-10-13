import Accessibility
import SwiftUI

/// The status panel of the live screen: state chip, level, elapsed time, provider and status lines.
///
/// Three lines (screens 4.2). In compact height the provider and status lines share one line, and the
/// status line takes the provider's place for a few seconds after it changes. At the accessibility text
/// sizes every item gets a line of its own.
struct LiveStatusPanel: View {
    private let isCompact: Bool

    @Environment(LiveSessionViewModel.self) private var model
    @Environment(SessionController.self) private var session
    @Environment(AppModel.self) private var app
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    /// The elapsed second at which the status line last changed (compact height only).
    @State private var statusChangedAt: Int? = nil

    init(isCompact: Bool = false) {
        self.isCompact = isCompact
    }

    var body: some View {
        Panel(compact: true) {
            VStack(alignment: .leading, spacing: theme.space.s1) {
                // The sliders button keeps its full target; the line takes only the height of the chip
                // row, so the panel stays a strip (screens 4.1).
                lineA
                    .padding(.vertical, -theme.space.s1)
                lines
            }
        }
        .onChange(of: model.statusLine) { _, _ in
            statusChangedAt = session.elapsedSeconds
        }
        .onChange(of: model.stateChip) { _, chip in
            // A change of state is announced politely (screens 4.8).
            AccessibilityNotification.Announcement(AttributedString(chip.text)).post()
        }
    }

    // MARK: - Line A

    @ViewBuilder
    private var lineA: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.s1) {
                HStack(alignment: .center, spacing: theme.space.s3) {
                    StatusChip(model.stateChip)
                    LevelMeter(level: model.level, state: model.meterState)
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(model.stateChip.text)
                .accessibilityValue(model.elapsedSpokenText)
                HStack(alignment: .center, spacing: theme.space.s3) {
                    elapsed
                        .accessibilityHidden(true)
                    Spacer(minLength: 0)
                    slidersButton
                }
            }
        } else {
            HStack(alignment: .center, spacing: theme.space.s3) {
                HStack(alignment: .center, spacing: theme.space.s3) {
                    StatusChip(model.stateChip)
                    LevelMeter(level: model.level, state: model.meterState)
                    Spacer(minLength: 0)
                    elapsed
                }
                // One element: "Listening, 4 minutes 12 seconds".
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(model.stateChip.text)
                .accessibilityValue(model.elapsedSpokenText)
                slidersButton
            }
        }
    }

    private var elapsed: some View {
        Text(verbatim: model.elapsedText)
            .lcText(.label, tabular: true)
    }

    private var slidersButton: some View {
        IconButton(systemImage: "slider.horizontal.3", label: "Assistant models & timing") {
            app.sessionPath.append(Route.assistantLab)
        }
        .disabled(model.isAssisting)
        .accessibilityIdentifier("assistant-lab")
    }

    // MARK: - Lines B and C

    @ViewBuilder
    private var lines: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.s1) {
                providerText
                costText
                statusText
                modelText
            }
        } else if isCompact {
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s2) {
                if showsStatusInCompact {
                    statusText
                } else {
                    providerText
                }
                Spacer(minLength: 0)
                modelText
            }
        } else {
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s2) {
                providerText
                Spacer(minLength: 0)
                costText
            }
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s2) {
                statusText
                Spacer(minLength: 0)
                modelText
            }
        }
    }

    private var providerText: some View {
        Text(verbatim: model.providerLine)
            .lcText(.caption, color: theme.ink.secondary)
            .lineLimit(lineLimit)
    }

    @ViewBuilder
    private var costText: some View {
        if let cost = model.costText {
            Text(verbatim: cost)
                .lcText(.caption, color: theme.ink.secondary, tabular: true)
                .lineLimit(1)
                .layoutPriority(1)
                .accessibilityIdentifier("live-transcription-cost")
        }
    }

    /// The status line. It may take two lines: the PC loading text is longer than one.
    private var statusText: some View {
        Text(verbatim: model.statusLine)
            .lcText(.caption, color: theme.ink.secondary)
            .lineLimit(statusLineLimit)
            .fixedSize(horizontal: false, vertical: true)
            .accessibilityIdentifier("transcriber-status")
    }

    private var modelText: some View {
        Text(verbatim: model.assistantModelText)
            .lcText(.caption, color: theme.ink.secondary)
            .lineLimit(1)
            .layoutPriority(1)
    }

    // MARK: - Values

    private var lineLimit: Int? {
        dynamicTypeSize.isAccessibilitySize ? nil : 1
    }

    private var statusLineLimit: Int? {
        dynamicTypeSize.isAccessibilitySize ? nil : 2
    }

    /// The status line is shown for a few seconds after it changed, then the provider line returns.
    private var showsStatusInCompact: Bool {
        guard let statusChangedAt else {
            return false
        }
        return session.elapsedSeconds - statusChangedAt < LiveLayout.statusHoldSeconds
    }
}
