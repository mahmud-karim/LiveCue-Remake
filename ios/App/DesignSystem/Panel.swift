import SwiftUI

/// A content panel (design system 4.9), with an optional header title and trailing chip.
struct Panel<Content: View>: View {
    private let title: String?
    private let chip: StatusChipModel?
    private let compact: Bool
    private let content: Content

    @Environment(\.theme) private var theme

    init(_ title: String? = nil, chip: StatusChipModel? = nil, compact: Bool = false, @ViewBuilder content: () -> Content) {
        self.title = title
        self.chip = chip
        self.compact = compact
        self.content = content()
    }

    var body: some View {
        // STUB(WP-05)
        VStack(alignment: .leading, spacing: theme.space.groupGap) {
            if let title {
                HStack {
                    Text(title).lcText(.headline)
                    Spacer()
                    if let chip {
                        StatusChip(chip)
                    }
                }
            }
            content
        }
        .padding(theme.space.panelInset)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(theme.surface.panel)
    }
}
