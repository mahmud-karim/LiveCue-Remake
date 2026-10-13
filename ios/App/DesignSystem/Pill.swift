import SwiftUI

/// `radius.pill` (design system 2.12): a capsule while a control keeps its nominal height.
///
/// `radius` is half that nominal height. Once larger text makes the control taller, the corners stop
/// growing there, so several lines of text stay inside the shape instead of running into the long curve
/// a true capsule would have at that height.
struct LCPill: InsettableShape {
    var radius: CGFloat
    private var inset: CGFloat = 0

    init(radius: CGFloat) {
        self.radius = radius
    }

    func path(in rect: CGRect) -> Path {
        let box = rect.insetBy(dx: inset, dy: inset)
        guard box.width > 0, box.height > 0 else {
            return Path()
        }
        let corner = max(0, min(radius - inset, min(box.width, box.height) / 2))
        return Path(roundedRect: box, cornerRadius: corner, style: .circular)
    }

    func inset(by amount: CGFloat) -> LCPill {
        var pill = self
        pill.inset += amount
        return pill
    }
}
