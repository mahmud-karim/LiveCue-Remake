import SwiftUI

/// A card (design system 4.9). The prominent card is the newest answer.
struct Card<Content: View>: View {
    private let prominent: Bool
    private let content: Content

    @Environment(\.theme) private var theme

    init(prominent: Bool = false, @ViewBuilder content: () -> Content) {
        self.prominent = prominent
        self.content = content()
    }

    var body: some View {
        // STUB(WP-05)
        VStack(alignment: .leading, spacing: theme.space.groupGap) {
            content
        }
        .padding(theme.space.cardInset)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(prominent ? theme.surface.raised : theme.surface.inset)
    }
}
