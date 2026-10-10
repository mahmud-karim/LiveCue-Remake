import SwiftUI
import UIKit

extension DynamicTypeSize {
    /// Icon tiles grow with the text up to this size and then stop, so that at the largest sizes the
    /// width goes to the words (design system 6.1).
    static let lcTileLimit = DynamicTypeSize.accessibility1

    /// `length` scaled the way body text is scaled at this text size: the same at the default size, larger
    /// at larger sizes. For the fixed dimensions that sit beside text (status dots, spinners), so that
    /// they keep their proportion to it.
    func lcScaled(_ length: CGFloat) -> CGFloat {
        UIFontMetrics.default.scaledValue(for: length, compatibleWith: lcTraits)
    }

    /// The side of an icon tile whose side is `length` at the default text size.
    func lcScaledTile(_ length: CGFloat) -> CGFloat {
        min(self, DynamicTypeSize.lcTileLimit).lcScaled(length)
    }

    /// The cap height of body text at this text size: the height of the live caret (design system 4.18).
    var lcBodyCapHeight: CGFloat {
        UIFont.preferredFont(forTextStyle: .body, compatibleWith: lcTraits).capHeight
    }

    private var lcTraits: UITraitCollection {
        UITraitCollection(preferredContentSizeCategory: UIContentSizeCategory(self))
    }
}
