import SwiftUI

/// What a status chip shows: a text, a tone and a mark.
struct StatusChipModel: Equatable, Sendable {
    let text: String
    let tone: StatusTone
    let mark: ChipMark

    init(_ text: String, tone: StatusTone, mark: ChipMark = .dot) {
        self.text = text
        self.tone = tone
        self.mark = mark
    }
}

/// A status chip (design system 4.12): one accessibility element whose label is the text.
struct StatusChip: View {
    private let model: StatusChipModel
    private let raised: Bool

    @Environment(\.theme) private var theme

    init(_ model: StatusChipModel, raised: Bool = false) {
        self.model = model
        self.raised = raised
    }

    init(_ text: String, tone: StatusTone, mark: ChipMark = .dot, raised: Bool = false) {
        self.init(StatusChipModel(text, tone: tone, mark: mark), raised: raised)
    }

    var body: some View {
        // STUB(WP-05)
        Text(model.text)
            .lcText(.caption, color: theme.status.color(for: model.tone))
    }
}
