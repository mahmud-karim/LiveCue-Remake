import SwiftUI

/// A title on the ground, above a panel.
struct SectionTitle: View {
    private let text: String

    init(_ text: String) {
        self.text = text
    }

    var body: some View {
        // STUB(WP-05)
        Text(text)
            .lcText(.headline)
            .accessibilityAddTraits(.isHeader)
    }
}

/// A supporting line in `type.caption`: `ink.secondary` unless a tone is given.
struct CaptionText: View {
    private let text: String
    private let tone: StatusTone?

    @Environment(\.theme) private var theme

    init(_ text: String, tone: StatusTone? = nil) {
        self.text = text
        self.tone = tone
    }

    var body: some View {
        // STUB(WP-05)
        Text(text)
            .lcText(.caption, color: tone.map { theme.status.color(for: $0) } ?? theme.ink.secondary)
    }
}
