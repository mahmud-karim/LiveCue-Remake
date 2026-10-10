import SwiftUI

/// An indeterminate progress ring (design system 4.13): three quarters of a circle in the colour of the
/// text around it, one turn every `motion.spin`.
///
/// It keeps turning with Reduce Motion, because it is the only sign of work. It is hidden from
/// accessibility: the label beside it, or the control it sits in, says what is in progress.
struct Spinner: View {
    private let size: CGFloat

    @Environment(\.theme) private var theme

    init(size: CGFloat = 20) {
        self.size = size
    }

    var body: some View {
        TimelineView(.animation) { timeline in
            Circle()
                .trim(from: 0, to: theme.component.spinnerArc)
                .stroke(.foreground, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(angle(at: timeline.date))
        }
        .padding(lineWidth / 2)
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }

    private var lineWidth: CGFloat {
        size > theme.component.smallSpinner ? theme.component.spinnerStroke : theme.component.smallSpinnerStroke
    }

    private func angle(at date: Date) -> Angle {
        let turn = theme.component.spinSeconds
        let elapsed = date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: turn)
        return Angle(radians: 2 * Double.pi * elapsed / turn)
    }
}
