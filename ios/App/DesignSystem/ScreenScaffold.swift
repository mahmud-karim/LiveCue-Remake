import SwiftUI

/// The ground behind everything, then one column 16 from the edges with 12 between children; a
/// `ScrollView` unless `scrolls` is false. System backgrounds are hidden.
struct ScreenScaffold<Content: View>: View {
    private let scrolls: Bool
    private let content: Content

    @Environment(\.theme) private var theme

    init(scrolls: Bool = true, @ViewBuilder content: () -> Content) {
        self.scrolls = scrolls
        self.content = content()
    }

    var body: some View {
        // STUB(WP-05)
        ZStack {
            GroundView()
            if scrolls {
                ScrollView {
                    column
                }
            } else {
                column
            }
        }
    }

    private var column: some View {
        VStack(alignment: .leading, spacing: theme.space.panelGap) {
            content
        }
        .frame(maxWidth: .infinity, maxHeight: scrolls ? nil : .infinity, alignment: .topLeading)
        .padding(.horizontal, theme.space.screenInset)
    }
}
