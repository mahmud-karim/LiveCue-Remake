import SwiftUI

/// The in-app mark (design system 8.2): 68 tall, or 48 in compact height. Hidden from accessibility.
///
/// A speech bubble holding three voice bars, with no tile behind it. From 64 points up it is the glass
/// bubble with its gradient, gloss, rim and `shadow.accent`; below that it is flat: the accent with white
/// bars.
struct BrandMark: View {
    private let height: CGFloat

    @Environment(\.theme) private var theme

    init(height: CGFloat) {
        self.height = height
    }

    var body: some View {
        ZStack {
            if height >= theme.component.detailedBrandMark {
                detailedBubble
            } else {
                BrandBubbleShape().fill(theme.accent.fill)
            }
            BrandBarsShape().fill(theme.brand.bars)
        }
        .frame(width: height * BrandArtwork.aspectRatio, height: height)
        .accessibilityHidden(true)
    }

    @ViewBuilder
    private var detailedBubble: some View {
        let shadow = theme.shadow.accent

        BrandBubbleShape()
            .fill(bubbleFill)
            .shadow(color: shadow.color, radius: shadow.radius, y: shadow.y)
        BrandGlossShape()
            .fill(glossFill)
            .clipShape(BrandBubbleShape())
        // Twice the rim's width, clipped to the outline: the half that is left lies inside it.
        BrandBubbleShape()
            .stroke(rimStroke, lineWidth: BrandArtwork.scaled(BrandArtwork.rim * 2, toHeight: height))
            .clipShape(BrandBubbleShape())
    }

    private var bubbleFill: LinearGradient {
        LinearGradient(
            stops: [
                Gradient.Stop(color: theme.brand.bubbleTop, location: 0),
                Gradient.Stop(color: theme.brand.bubbleMiddle, location: theme.brand.bubbleMiddleStop),
                Gradient.Stop(color: theme.brand.bubbleBottom, location: 1)
            ],
            startPoint: .top,
            endPoint: .bottom
        )
    }

    private var glossFill: LinearGradient {
        LinearGradient(
            colors: [theme.brand.glossTop, theme.brand.glossTop.opacity(0)],
            startPoint: BrandArtwork.glossStart,
            endPoint: BrandArtwork.glossEnd
        )
    }

    private var rimStroke: LinearGradient {
        LinearGradient(colors: [theme.brand.rimTop, theme.brand.rimBottom], startPoint: .top, endPoint: .bottom)
    }
}

/// The name, two-tone (design system 8.1): "Live" in ink and "Cue" in the accent, as one text.
///
/// It is two-tone only in the roles that are large text (20 points and up), where the accent may sit on
/// the ground. In a smaller role the whole name is ink.
struct BrandName: View {
    private let role: TextRole

    @Environment(\.theme) private var theme

    init(role: TextRole = .display) {
        self.role = role
    }

    var body: some View {
        name
            .font(role.font)
            .accessibilityLabel("LiveCue")
    }

    private var name: Text {
        let live: Text = Text("Live").foregroundStyle(theme.ink.primary)
        let cue: Text = Text("Cue").foregroundStyle(isLargeText ? theme.accent.fill : theme.ink.primary)
        return Text("\(live)\(cue)")
    }

    private var isLargeText: Bool {
        switch role {
        case .display, .title, .cue:
            return true
        case .headline, .transcript, .body, .label, .caption, .micro, .mono:
            return false
        }
    }
}
