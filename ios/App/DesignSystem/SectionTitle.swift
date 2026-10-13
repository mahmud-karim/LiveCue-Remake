import SwiftUI

/// A title on the ground, above a panel (design system 4.9): `type.label` in `ink.primary`, 8 above the
/// panel it names and a little further from what is above it.
struct SectionTitle: View {
    private let text: String

    @Environment(\.theme) private var theme

    init(_ text: String) {
        self.text = text
    }

    var body: some View {
        Text(verbatim: text)
            .lcText(.label)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.horizontal, theme.space.s1)
            // The column puts 12 between its children; this makes it 18 above and 8 below.
            .padding(.top, theme.space.s1 + theme.space.half)
            .padding(.bottom, -theme.space.s1)
            .accessibilityAddTraits(.isHeader)
    }
}

/// A supporting line in `type.caption`: `ink.secondary` unless a tone is given.
///
/// A toned caption belongs on a panel or a denser surface; directly on the ground only ink passes the
/// contrast rule (design system C-1).
struct CaptionText: View {
    private let text: String
    private let tone: StatusTone?

    @Environment(\.theme) private var theme

    init(_ text: String, tone: StatusTone? = nil) {
        self.text = text
        self.tone = tone
    }

    var body: some View {
        Text(verbatim: text)
            .lcText(.caption, color: tone.map { theme.status.color(for: $0) } ?? theme.ink.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }
}
