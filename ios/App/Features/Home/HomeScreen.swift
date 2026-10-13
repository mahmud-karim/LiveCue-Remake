import SwiftUI

/// Home, the root of the Live tab (screens 3).
///
/// It fits without scrolling at the normal text sizes, uses the compact rules when the container is
/// lower than the threshold, and scrolls at the accessibility sizes. The Start control floats above the
/// tab bar.
struct HomeScreen: View {
    @Environment(HomeViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    @State private var containerHeight: CGFloat = 0

    var body: some View {
        ScreenScaffold(scrolls: dynamicTypeSize.isAccessibilitySize) {
            HomeHero(isCompact: isCompact)
            if model.showsFirstRunBanner {
                Banner(
                    tone: .info,
                    title: "Finish setup",
                    message: "The rows below show what is missing. Tap one to fix it."
                )
            }
            HomeReadinessPanel()
        }
        .lcBottomBar {
            HomeStartBar()
        }
        .toolbar(.hidden, for: .navigationBar)
        .onContainerHeightChange { height in
            containerHeight = height
        }
        .onAppear {
            model.appeared()
        }
        .onDisappear {
            model.disappeared()
        }
    }

    /// The compact rules also apply at the accessibility sizes (spec 01 section 1.3).
    private var isCompact: Bool {
        dynamicTypeSize.isAccessibilitySize || theme.isCompactHeight(containerHeight)
    }
}

/// The mark, the name, the tagline, the two description lines and the status chip.
private struct HomeHero: View {
    let isCompact: Bool

    @Environment(HomeViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        VStack(alignment: alignment, spacing: theme.space.s1) {
            if !dynamicTypeSize.isAccessibilitySize {
                BrandMark(height: markHeight)
            }
            BrandName(role: isCompact ? .title : .display)
            if !isCompact {
                Text(verbatim: "A clearer conversation.")
                    .lcText(.body, color: theme.ink.secondary)
            }
            description
            StatusChip(model.statusChip, raised: true)
                .padding(.top, theme.space.s2)
        }
        .frame(maxWidth: .infinity, alignment: frameAlignment)
    }

    /// Two lines, read as one sentence pair.
    private var description: some View {
        VStack(alignment: alignment, spacing: 0) {
            Text(verbatim: model.captionLine1)
            Text(verbatim: model.captionLine2)
        }
        .lcText(.caption, color: theme.ink.secondary)
        .multilineTextAlignment(textAlignment)
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityElement(children: .combine)
    }

    private var markHeight: CGFloat {
        isCompact ? theme.component.compactBrandMark : theme.component.brandMark
    }

    /// Centred, and leading at the accessibility sizes.
    private var alignment: HorizontalAlignment {
        dynamicTypeSize.isAccessibilitySize ? .leading : .center
    }

    private var frameAlignment: Alignment {
        dynamicTypeSize.isAccessibilitySize ? .leading : .center
    }

    private var textAlignment: TextAlignment {
        dynamicTypeSize.isAccessibilitySize ? .leading : .center
    }
}

/// The Start control in its states, or the link to Model Library.
private struct HomeStartBar: View {
    @Environment(HomeViewModel.self) private var model
    @Environment(AppModel.self) private var app
    @Environment(\.theme) private var theme

    var body: some View {
        switch model.startControl {
        case .download:
            SessionStartControl(title: "Download a transcription model", systemImage: "arrow.down.circle") {
                app.livePath.append(Route.modelLibrary)
            }
            .accessibilityIdentifier("download-model")
        case .start(let isBusy, let isEnabled, let caption):
            VStack(spacing: theme.space.s2) {
                if let caption {
                    CaptionText(caption)
                        .multilineTextAlignment(.center)
                }
                SessionStartControl(
                    title: isBusy ? "Connecting…" : "Start conversation",
                    systemImage: "mic.fill",
                    isBusy: isBusy
                ) {
                    Task { await model.start() }
                }
                .disabled(!isEnabled)
                .accessibilityIdentifier("start-session")
                .accessibilityHint("Starts listening")
            }
        }
    }
}
