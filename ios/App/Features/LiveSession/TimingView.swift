import LiveCueCore
import SwiftUI

/// The timing of one answer, identifier `response-timing`.
///
/// A container that assistive technology reads as "Response timing". It shows nothing for an answer
/// without a timing record.
struct TimingView: View {
    private let turn: AssistantTurn

    @Environment(\.theme) private var theme

    init(turn: AssistantTurn) {
        self.turn = turn
    }

    var body: some View {
        if let content = TimingViewContent(turn: turn) {
            VStack(alignment: .leading, spacing: theme.space.s2) {
                CaptionText(content.caption)
                TimingRows(rows: leadingRows(of: content))
                if let usage = content.usageCaption {
                    CaptionText(usage)
                }
                if let warning = content.warning {
                    CaptionText(warning, tone: .warning)
                }
                TimingRows(rows: trailingRows(of: content))
                CaptionText(content.footerCaption)
                CaptionText(content.footnote)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .accessibilityElement(children: .contain)
            .accessibilityLabel("Response timing")
            .accessibilityIdentifier("response-timing")
        }
    }

    /// Every row but the live chunk one, which comes after the usage caption and the warning (screens 4.6).
    private func leadingRows(of content: TimingViewContent) -> [TimingRow] {
        content.rows.filter { $0.id != TimingViewContent.lastChunkRowID }
    }

    private func trailingRows(of content: TimingViewContent) -> [TimingRow] {
        content.rows.filter { $0.id == TimingViewContent.lastChunkRowID }
    }
}

/// Detail rows that touch, as they do inside a panel.
private struct TimingRows: View {
    let rows: [TimingRow]

    var body: some View {
        if !rows.isEmpty {
            VStack(alignment: .leading, spacing: 0) {
                ForEach(rows) { row in
                    DetailRow(row.key, value: row.value)
                }
            }
        }
    }
}
