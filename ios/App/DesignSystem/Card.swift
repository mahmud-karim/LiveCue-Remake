import SwiftUI

/// A card (design system 4.9). A plain card is a quiet tile inside a panel. The prominent card is the
/// brightest surface of a screen, for the newest answer: at most one per screen, on the ground or in a
/// panel.
struct Card<Content: View>: View {
    private let prominent: Bool
    private let content: Content

    @Environment(\.theme) private var theme

    init(prominent: Bool = false, @ViewBuilder content: () -> Content) {
        self.prominent = prominent
        self.content = content()
    }

    var body: some View {
        LCSurfaceStack(content)
            .padding(theme.space.cardInset)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background {
                if prominent {
                    LCRaisedSurface(shape: shape)
                } else {
                    LCInsetSurface(shape: shape)
                }
            }
    }

    private var shape: RoundedRectangle {
        RoundedRectangle(cornerRadius: theme.radius.card, style: .continuous)
    }
}
