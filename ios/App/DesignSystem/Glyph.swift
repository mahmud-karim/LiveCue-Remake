import SwiftUI

/// The icon sizes of design system 2.18, each as the Dynamic Type text style that draws an SF Symbol at
/// about that many points across. An icon is a symbol in a text style, never a fixed size, so it grows
/// with the text beside it.
enum LCGlyphSize: Sendable {
    /// 14: the mark of a chip.
    case chip
    /// 16: chevrons and other small marks.
    case small
    /// 20: buttons and rows.
    case regular
    /// 24: session controls.
    case large
    /// 28: the tile of an empty state.
    case tile

    var font: Font {
        switch self {
        case .chip: return .caption
        case .small: return .footnote
        case .regular: return .body
        case .large: return .title3
        case .tile: return .title2
        }
    }
}

/// One SF Symbol: monochrome, in the colour of the text around it, hidden from accessibility because the
/// control or the text beside it carries the meaning.
struct LCGlyph: View {
    private let systemImage: String
    private let size: LCGlyphSize
    private let weight: Font.Weight

    init(_ systemImage: String, size: LCGlyphSize = .regular, weight: Font.Weight = .semibold) {
        self.systemImage = systemImage
        self.size = size
        self.weight = weight
    }

    var body: some View {
        Image(systemName: systemImage)
            .font(size.font.weight(weight))
            .symbolRenderingMode(.monochrome)
            .contentTransition(.symbolEffect(.replace))
            .accessibilityHidden(true)
    }
}
