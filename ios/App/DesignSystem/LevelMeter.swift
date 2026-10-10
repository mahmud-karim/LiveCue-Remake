import SwiftUI

/// The audio level visualiser, size S of design system 4.21. Hidden from accessibility.
///
/// Five rounded bars that answer one question: can it hear me? While listening they follow `level` (0 to
/// 1), rising fast and falling slowly, and are redrawn at most 24 times a second. A silent bar is a dot.
/// Idle and paused meters are flat and do not move. With Reduce Motion the meter still shows the level,
/// because that is information, but changes four times a second.
struct LevelMeter: View {
    private let level: Float
    private let state: LevelMeterState

    @Environment(\.theme) private var theme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// The level the bars are drawn at. It follows `level` on the meter's own clock.
    @State private var shownLevel: CGFloat = 0

    init(level: Float, state: LevelMeterState) {
        self.level = level
        self.state = state
    }

    var body: some View {
        TimelineView(.animation(minimumInterval: interval, paused: state != .listening)) { timeline in
            bars
                .onChange(of: timeline.date) { _, _ in
                    follow()
                }
        }
        .onChange(of: state) { _, newState in
            if newState != .listening {
                shownLevel = 0
            }
        }
        .accessibilityHidden(true)
    }

    private var bars: some View {
        HStack(alignment: .center, spacing: theme.component.meterGap) {
            ForEach(Array(theme.component.meterWeights.enumerated()), id: \.offset) { _, weight in
                Capsule()
                    .fill(color)
                    .frame(width: theme.component.meterBar, height: height(weight: weight))
            }
        }
        .frame(height: theme.component.meter)
    }

    private var interval: Double {
        reduceMotion ? theme.component.meterCalmInterval : theme.component.meterInterval
    }

    private var color: Color {
        switch state {
        case .listening: return theme.status.live
        case .idle: return theme.ink.secondary
        case .paused: return theme.status.warning
        }
    }

    /// Minimum plus the range times level times the bar's weight. The minimum is the bar's width.
    private func height(weight: CGFloat) -> CGFloat {
        let minimum = theme.component.meterBar
        guard state == .listening else {
            return minimum
        }
        return minimum + (theme.component.meter - minimum) * shownLevel * weight
    }

    private func follow() {
        let target = CGFloat(min(max(level, 0), 1))
        guard target != shownLevel else {
            return
        }
        withAnimation(animation(toward: target)) {
            shownLevel = target
        }
    }

    private func animation(toward target: CGFloat) -> Animation {
        if reduceMotion {
            return theme.motion.base
        }
        return target > shownLevel ? theme.component.meterAttack : theme.component.meterRelease
    }
}
