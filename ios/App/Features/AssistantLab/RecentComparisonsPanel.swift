import SwiftUI

/// "Recent comparisons": the newest timed answers, each opening its run details.
struct RecentComparisonsPanel: View {
    @Environment(AssistantLabViewModel.self) private var model

    var body: some View {
        Panel("Recent comparisons") {
            if model.comparisons.isEmpty {
                CaptionText("Assist once to start collecting results.")
            } else {
                ForEach(model.comparisons) { row in
                    LCNavigationRow(route: .runDetails(turnID: row.id), title: row.model) {
                        ComparisonSubtitle(row: row)
                    }
                }
            }
        }
    }
}

/// The lines under the model of a comparison: what was compared, how long it took, and the question.
/// The question came from a model and is drawn as it is.
private struct ComparisonSubtitle: View {
    let row: ComparisonRow

    @Environment(\.theme) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: theme.space.half) {
            CaptionText(row.detail)
            CaptionText(row.timing)
            if !row.question.isEmpty {
                Text(verbatim: row.question)
                    .lcText(.caption, color: theme.ink.secondary)
                    .lineLimit(2)
            }
        }
    }
}
