import Accessibility
import Observation
import SwiftUI

/// The one toast of the app. A toast is visible for 4 s; a new toast replaces the current one.
@MainActor @Observable final class ToastCenter {
    /// How long a toast stays (design system 4.11).
    static let visibleSeconds: TimeInterval = 4

    private(set) var message: String? = nil

    private let clock: any AppClock
    @ObservationIgnored private var dismissal: Task<Void, Never>?
    /// Counts the toasts shown, so that an older toast's timer never hides a newer toast.
    @ObservationIgnored private var generation = 0

    init(clock: any AppClock) {
        self.clock = clock
    }

    func show(_ text: String) {
        dismissal?.cancel()
        generation += 1
        message = text

        let shown = generation
        let sleeper = clock
        dismissal = Task { [weak self] in
            do {
                try await sleeper.sleep(seconds: ToastCenter.visibleSeconds)
            } catch {
                return
            }
            self?.hide(shown)
        }
    }

    private func hide(_ shown: Int) {
        if shown == generation {
            message = nil
        }
    }
}

/// Shows the current toast. It reads `ToastCenter` from the environment and is placed by `RootView`.
///
/// A toast is a short confirmation that needs no decision. It appears at the top of the screen, is
/// announced politely, and never takes a touch away from what is under it. It is not glass.
struct ToastOverlay: View {
    @Environment(ToastCenter.self) private var toasts
    @Environment(\.theme) private var theme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init() {}

    var body: some View {
        ZStack(alignment: .top) {
            if let message = toasts.message {
                LCToastTile(message: message)
                    .transition(transition)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .padding(.horizontal, theme.space.screenInset)
        .padding(.top, theme.space.s2)
        .animation(toasts.message == nil ? theme.motion.fast : theme.motion.base, value: toasts.message)
        .allowsHitTesting(false)
        .onChange(of: toasts.message) { _, message in
            if let message {
                AccessibilityNotification.Announcement(message).post()
            }
        }
    }

    /// It fades and moves in from its edge; with Reduce Motion it only fades.
    private var transition: AnyTransition {
        if reduceMotion {
            return .opacity
        }
        return .opacity.combined(with: .offset(y: -theme.component.toastTravel))
    }
}

/// The tile of a toast: `surface.inverse` with a check and the text in `ink.onInverse`.
struct LCToastTile: View {
    let message: String

    @Environment(\.theme) private var theme

    var body: some View {
        let shadow = theme.shadow.raised

        HStack(spacing: theme.space.inlineGap) {
            LCGlyph("checkmark", size: .regular)
            Text(verbatim: message)
                .font(TextRole.label.font)
                .fixedSize(horizontal: false, vertical: true)
        }
        .foregroundStyle(theme.ink.onInverse)
        .padding(.horizontal, theme.space.s4)
        .padding(.vertical, theme.space.s3)
        .frame(minHeight: theme.component.toast)
        .background {
            RoundedRectangle(cornerRadius: theme.radius.card, style: .continuous)
                .fill(theme.surface.inverse)
                .shadow(color: shadow.color, radius: shadow.radius, y: shadow.y)
        }
        .accessibilityElement(children: .combine)
    }
}
