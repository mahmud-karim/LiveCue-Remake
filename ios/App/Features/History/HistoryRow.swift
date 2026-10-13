import SwiftUI

/// One stored conversation in History: title, date, counts and its usage label.
///
/// The row is one button. An identifier inside a button's label is not exposed, so the row is laid out
/// twice with the same geometry, as `LCNavigationRow` does: the button's label shows everything but the
/// usage label, which only holds its place there, and an overlay shows the usage label alone. That keeps
/// `history-transcription-cost` and `history-local-transcription` reachable.
struct HistoryRow: View {
    private let content: HistoryRowContent
    private let action: @MainActor () -> Void

    @Environment(\.theme) private var theme

    init(content: HistoryRowContent, action: @escaping @MainActor () -> Void) {
        self.content = content
        self.action = action
    }

    var body: some View {
        Button(action: action) {
            layout(showsUsage: false)
        }
        .buttonStyle(LCRowButtonStyle())
        .overlay {
            if content.usage != nil {
                layout(showsUsage: true)
                    .allowsHitTesting(false)
            }
        }
    }

    private func layout(showsUsage: Bool) -> some View {
        HStack(alignment: .top, spacing: theme.space.s3) {
            VStack(alignment: .leading, spacing: theme.space.half) {
                facts
                    .opacity(showsUsage ? 0 : 1)
                    .accessibilityHidden(showsUsage)
                usageLine
                    .opacity(showsUsage ? 1 : 0)
                    .accessibilityHidden(!showsUsage)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            LCGlyph("chevron.right", size: .small)
                .foregroundStyle(theme.ink.secondary)
                .padding(.top, theme.space.s1)
                .opacity(showsUsage ? 0 : 1)
        }
        .padding(.vertical, theme.space.s3)
        .frame(minHeight: theme.size.twoLineRow)
    }

    /// Title, date and counts.
    private var facts: some View {
        VStack(alignment: .leading, spacing: theme.space.half) {
            Text(verbatim: content.title)
                .lcText(.headline)
                .lineLimit(2)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
            CaptionText(content.dateText)
            CaptionText(content.countsText)
        }
    }

    @ViewBuilder
    private var usageLine: some View {
        switch content.usage {
        case .meta(let text)?:
            CaptionText(text)
                .accessibilityIdentifier("history-transcription-cost")
        case .local(let text)?:
            CaptionText(text)
                .accessibilityIdentifier("history-local-transcription")
        case nil:
            EmptyView()
        }
    }
}
