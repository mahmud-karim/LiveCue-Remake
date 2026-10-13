import LiveCueCore
import SwiftUI

/// One saved answer, with its timing disclosure, "Copy answer" and the same-text retry.
///
/// The latest answer is the prominent cue card: the chip "Latest", the answer in `type.cue`, and the
/// retry. An earlier answer is a plain card with its total time in the header and the answer in
/// `type.body`. Everything a model wrote (question, answer, details) is drawn as it is: nothing in it is
/// read as formatting or as a link.
struct AnswerCard: View {
    private let turn: AssistantTurn
    private let isLatest: Bool
    private let canRetry: Bool
    private let onRetry: @MainActor () -> Void
    private let onCopy: @MainActor () -> Void

    @Environment(\.theme) private var theme

    @State private var showsDetails = false
    @State private var showsTiming = false

    init(
        turn: AssistantTurn,
        isLatest: Bool,
        canRetry: Bool,
        onRetry: @escaping @MainActor () -> Void,
        onCopy: @escaping @MainActor () -> Void
    ) {
        self.turn = turn
        self.isLatest = isLatest
        self.canRetry = canRetry
        self.onRetry = onRetry
        self.onCopy = onCopy
    }

    var body: some View {
        Card(prominent: isLatest) {
            header
            content
            if hasTiming {
                DisclosureRow("Timing & usage", isExpanded: $showsTiming, identifier: timingIdentifier) {
                    TimingView(turn: turn)
                }
            }
            if isLatest {
                LCButton(
                    "Retry same text with selected model",
                    systemImage: "arrow.clockwise",
                    kind: .quiet,
                    action: onRetry
                )
                .disabled(!canRetry)
                .accessibilityIdentifier("retry-same-text")
            }
        }
    }

    // MARK: - Parts

    @ViewBuilder
    private var header: some View {
        if isLatest {
            CueCardHeader {
                StatusChip("Latest", tone: .live, mark: .none)
            }
        } else {
            CueCardHeader {
                if let total = AnswerFormat.total(of: turn) {
                    Text(verbatim: total)
                        .lcText(.caption, color: theme.ink.secondary, tabular: true)
                }
            }
        }
    }

    /// The question and the answer, then the detail toggle and the footer directly under them: one
    /// child of the card, so that its controls, which keep their full targets, sit close to the text.
    private var content: some View {
        VStack(alignment: .leading, spacing: 0) {
            texts
            if !turn.details.isEmpty {
                LCButton(
                    showsDetails ? "Hide detail" : "More detail",
                    systemImage: showsDetails ? "chevron.up" : "chevron.down",
                    kind: .quiet,
                    action: toggleDetails
                )
                if showsDetails {
                    AnswerDetailsText(details: turn.details)
                        .padding(.bottom, theme.space.s2)
                }
            }
            if hasTiming {
                footer
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var texts: some View {
        VStack(alignment: .leading, spacing: theme.space.s2) {
            if let question = AnswerFormat.visibleQuestion(of: turn) {
                Text(verbatim: question)
                    .lcText(.label, color: theme.ink.secondary)
                    .lineLimit(2)
            }
            Text(verbatim: turn.answer)
                .lcText(isLatest ? .cue : .body)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
                .accessibilityIdentifier(isLatest ? "assistant-answer" : "previous-assistant-answer")
        }
    }

    /// The total time and the reported cost on the latest card (an earlier card has its time in the
    /// header and shows only the cost here), then "Copy answer".
    private var footer: some View {
        HStack(alignment: .center, spacing: theme.space.s2) {
            if let line = footerLine {
                Text(verbatim: line)
                    .lcText(.caption, color: theme.ink.secondary, tabular: true)
            }
            Spacer(minLength: 0)
            IconButton(systemImage: "doc.on.doc", label: "Copy answer", action: onCopy)
        }
    }

    // MARK: - Values

    private var hasTiming: Bool {
        turn.performance != nil
    }

    private var footerLine: String? {
        if isLatest {
            return AnswerFormat.totalAndCost(of: turn)
        }
        return AnswerFormat.reportedCost(of: turn).map { AnswerFormat.dollars($0) }
    }

    private var timingIdentifier: String {
        isLatest ? "timing-disclosure" : "previous-timing"
    }

    private func toggleDetails() {
        withAnimation(theme.motion.base) {
            showsDetails.toggle()
        }
    }
}

/// The details of an answer, drawn as plain text.
struct AnswerDetailsText: View {
    let details: String

    @Environment(\.theme) private var theme

    var body: some View {
        Text(verbatim: details)
            .lcText(.body, color: theme.ink.secondary)
            .fixedSize(horizontal: false, vertical: true)
            .textSelection(.enabled)
    }
}
