import SwiftUI

/// A determinate progress bar; `value` runs from 0 to 1.
///
/// Design system 4.13: a 6 high track with the accent on it, or `status.danger` when the work failed. It
/// belongs on a panel or a denser surface (rule C-2). Say what is in progress in a label beside it.
struct LCProgressBar: View {
    private let value: Double
    private let failed: Bool

    init(value: Double, failed: Bool = false) {
        self.value = value
        self.failed = failed
    }

    var body: some View {
        ProgressView(value: fraction)
            .progressViewStyle(LCProgressBarStyle(failed: failed))
    }

    private var fraction: Double {
        min(max(value, 0), 1)
    }
}

private struct LCProgressBarStyle: ProgressViewStyle {
    let failed: Bool

    func makeBody(configuration: Configuration) -> some View {
        LCProgressTrack(fraction: configuration.fractionCompleted ?? 0, failed: failed)
    }
}

private struct LCProgressTrack: View {
    let fraction: Double
    let failed: Bool

    @Environment(\.theme) private var theme

    var body: some View {
        Capsule()
            .fill(theme.track.progress)
            .frame(height: theme.component.progressBar)
            .overlay(alignment: .leading) {
                GeometryReader { proxy in
                    Capsule()
                        .fill(failed ? theme.status.danger : theme.accent.fill)
                        .frame(width: proxy.size.width * CGFloat(fraction))
                        .animation(theme.motion.base, value: fraction)
                }
            }
            .frame(maxWidth: .infinity)
    }
}
