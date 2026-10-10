import SwiftUI

/// The in-app mark (design system 8.2): 68 tall, or 48 in compact height. Hidden from accessibility.
struct BrandMark: View {
    private let height: CGFloat

    @Environment(\.theme) private var theme

    init(height: CGFloat) {
        self.height = height
    }

    var body: some View {
        // STUB(WP-05)
        Image(systemName: "waveform")
            .resizable()
            .scaledToFit()
            .frame(height: height)
            .foregroundStyle(theme.accent.fill)
            .accessibilityHidden(true)
    }
}

/// The name, two-tone (design system 8.1): "Live" in ink and "Cue" in the accent, as one text.
struct BrandName: View {
    private let role: TextRole

    @Environment(\.theme) private var theme

    init(role: TextRole = .display) {
        self.role = role
    }

    var body: some View {
        // STUB(WP-05)
        name
            .font(role.font)
            .accessibilityLabel("LiveCue")
    }

    private var name: Text {
        let live: Text = Text("Live").foregroundStyle(theme.ink.primary)
        let cue: Text = Text("Cue").foregroundStyle(theme.accent.fill)
        return Text("\(live)\(cue)")
    }
}
