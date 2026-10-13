import CoreGraphics

/// The layout numbers of the live session screen that the design gives as plain values (screens 4.1).
enum LiveLayout {
    /// The expanded transcript is this tall at most.
    static let transcriptMaximumHeight: CGFloat = 180
    /// The expanded transcript takes this part of the screen height at most.
    static let transcriptHeightFraction: CGFloat = 0.24
    /// In compact height the status line replaces the provider line for this many seconds after a change.
    static let statusHoldSeconds = 4
    /// How far above the end of the transcript the reader still counts as "at the bottom": about a line.
    static let transcriptFollowSlack: CGFloat = 24

    /// The newest line of the expanded transcript is in view, give or take a line.
    static func reachesBottom(offset: CGFloat, visibleHeight: CGFloat, contentHeight: CGFloat) -> Bool {
        offset + visibleHeight >= contentHeight - transcriptFollowSlack
    }

    /// min(180, 24% of the screen height); the maximum while the height is not measured yet.
    static func transcriptHeight(containerHeight: CGFloat) -> CGFloat {
        guard containerHeight > 0 else {
            return transcriptMaximumHeight
        }
        return min(transcriptMaximumHeight, containerHeight * transcriptHeightFraction)
    }
}
