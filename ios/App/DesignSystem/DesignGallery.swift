#if DEBUG
import SwiftUI

// A gallery of the design system for debug builds. It is how the components are looked at: they have no
// screen of their own to be tested through, so a simulator run shows these pages and screenshots them.
//
// Launch with `-design-gallery` and every screen shows a gallery page in place of its content, a new page
// for each screen in the order they first appear: Live, then History, then Settings give pages 1 to 3.
// `-design-gallery-set N` picks the set of pages:
//
//   1  The components as the screens use them: a Home page, a Live session page, a form page.
//   2  Every variant: controls and status elements, lists, the accessibility variants of one sample.
//   3  Home at an accessibility text size; messages with the cluster disabled; lists at an accessibility
//      text size with Increase Contrast.
//   4  Home with Increase Contrast; Live session and the form at an accessibility text size.
//
// None of this is compiled into a release build.

enum DesignGallery {
    static let argument = "-design-gallery"
    static let setArgument = "-design-gallery-set"
    static let pagesPerSet = 3

    /// A set to show without the launch argument. A screenshot run on CI sets it for one commit, because
    /// the existing UI test cannot pass an argument; it is nil in every other commit.
    static let forcedSet: Int? = 4

    /// The set to show, or nil when the gallery was not asked for.
    static let activeSet: Int? = {
        if let forcedSet {
            return forcedSet
        }
        let arguments = ProcessInfo.processInfo.arguments
        guard arguments.contains(argument) else {
            return nil
        }
        if let index = arguments.firstIndex(of: setArgument), index + 1 < arguments.count, let set = Int(arguments[index + 1]) {
            return set
        }
        return 1
    }()

    static var isActive: Bool {
        activeSet != nil
    }

    @MainActor private static var claimedPages = 0

    /// The next page, counted from 0. Each screen claims one when it first appears.
    @MainActor static func claimPage() -> Int {
        let page = claimedPages
        claimedPages += 1
        return page
    }
}

/// What `ScreenScaffold` shows while the gallery is active.
struct DesignGalleryHost: View {
    @State private var page: Int?

    var body: some View {
        ZStack {
            if let page {
                DesignGalleryPage(set: DesignGallery.activeSet ?? 1, index: page % DesignGallery.pagesPerSet)
            } else {
                GroundView()
            }
        }
        .onAppear {
            if page == nil {
                page = DesignGallery.claimPage()
            }
        }
    }
}

private struct DesignGalleryPage: View {
    let set: Int
    let index: Int

    @Environment(\.theme) private var theme

    var body: some View {
        switch (set, index) {
        case (2, 0):
            GalleryControls()
        case (2, 1):
            GalleryLists()
        case (2, 2):
            GalleryVariants()
        case (3, 0):
            GalleryHome().environment(\.dynamicTypeSize, .accessibility3)
        case (3, 1):
            GalleryMessages()
        case (3, 2):
            GalleryLists()
                .environment(\.dynamicTypeSize, .accessibility3)
                .environment(\.theme, contrastTheme)
        case (4, 0):
            GalleryHome().environment(\.theme, contrastTheme)
        case (4, 1):
            GalleryLive().environment(\.dynamicTypeSize, .accessibility3)
        case (4, 2):
            GalleryForms().environment(\.dynamicTypeSize, .accessibility3)
        case (_, 1):
            GalleryLive()
        case (_, 2):
            GalleryForms()
        default:
            GalleryHome()
        }
    }

    private var contrastTheme: Theme {
        theme.resolved(reduceTransparency: true, increaseContrast: true)
    }
}

// MARK: - Set 1: the components as the screens use them

/// Home: mark, name, chip, the readiness panel of value rows, and the Start control above the tab bar.
private struct GalleryHome: View {
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        // Home fits without scrolling at the normal text sizes and scrolls at the accessibility sizes.
        LCScaffoldLayout(scrolls: dynamicTypeSize.isAccessibilitySize, content: content)
            .lcBottomBar {
                SessionStartControl(title: "Start conversation", systemImage: "mic.fill") {}
            }
    }

    private var content: some View {
        Group {
            hero
            Banner(tone: .info, title: "Finish setup", message: "The rows below show what is missing. Tap one to fix it.")
            Panel("System readiness", chip: StatusChipModel("Ready", tone: .success, mark: .check)) {
                rows
            }
        }
    }

    private var hero: some View {
        VStack(spacing: theme.space.s1) {
            BrandMark(height: theme.component.brandMark)
            BrandName()
            Text(verbatim: "A clearer conversation.")
                .lcText(.body, color: theme.ink.secondary)
            Text(verbatim: "Live captions with Meta Muse.\nTap Assist for an answer from your PC.")
                .lcText(.caption, color: theme.ink.secondary)
                .multilineTextAlignment(.center)
            StatusChip("PC online", tone: .success, raised: true)
                .padding(.top, theme.space.s2)
        }
        .frame(maxWidth: .infinity)
    }

    @ViewBuilder
    private var rows: some View {
        LCNavigationRow(route: .transcription, title: "Transcription", value: "Meta Muse · cloud", systemImage: "waveform") {
            CaptionText("Meta connection: Key saved · direct", tone: .success)
        }
        LCNavigationRow(route: .pairing, title: "PC connection", value: "Paired", systemImage: "desktopcomputer") {
            CaptionText("Saved · connected")
        }
        LCNavigationRow(route: .assistantLab, title: "Assistant models & timing", systemImage: "slider.horizontal.3") {
            CaptionText("gpt-5.6-sol")
        }
    }
}

/// Live session: the status strip, a notice, the transcript, the newest answer, and the control cluster.
private struct GalleryLive: View {
    @Environment(\.theme) private var theme
    @State private var isTranscriptOpen = true
    @State private var isTimingOpen = true

    var body: some View {
        LCScaffoldLayout(scrolls: true, content: content)
            .lcBottomBar {
                SessionControlCluster(
                    isPaused: false,
                    isAssisting: false,
                    isTransitioning: false,
                    onPauseResume: {},
                    onAssist: {},
                    onStop: {}
                )
            }
    }

    private var content: some View {
        Group {
            statusPanel
            Banner(tone: .warning, title: "Recording paused", message: "Audio connection failed. Resume to reconnect.")
            transcriptPanel
            answerCard
        }
    }

    private var statusPanel: some View {
        Panel(compact: true) {
            HStack(spacing: theme.space.s3) {
                StatusChip("Listening", tone: .live, mark: .pulsingDot)
                LevelMeter(level: 0.8, state: .listening)
                Spacer(minLength: 0)
                Text(verbatim: "04:12")
                    .lcText(.label, tabular: true)
                IconButton(systemImage: "slider.horizontal.3", label: "Assistant models & timing") {}
            }
            HStack(spacing: theme.space.s2) {
                CaptionText("Meta Muse · cloud · est. transcription")
                Spacer(minLength: 0)
                CaptionText("$0.01260 USD")
            }
        }
    }

    private var transcriptPanel: some View {
        Panel(compact: true) {
            DisclosureRow("Live transcript", isExpanded: $isTranscriptOpen) {
                TranscriptLine("The main risk is the onboarding flow. Design says they need one more week.")
                TranscriptLine("and could the analytics dashboard move to the sprint after", isInterim: true, showsCaret: true)
            }
        }
    }

    private var answerCard: some View {
        Card(prominent: true) {
            HStack(spacing: theme.space.s2) {
                Text(verbatim: "Assistant")
                    .lcText(.micro, color: theme.accent.fill)
                Spacer(minLength: 0)
                StatusChip("Latest", tone: .live, mark: .none)
            }
            Text(verbatim: "Cut the analytics dashboard and the referral banner; both can ship a sprint later.")
                .lcText(.cue)
            HStack(spacing: theme.space.s2) {
                HStack(alignment: .lastTextBaseline, spacing: theme.component.caretGap) {
                    CaptionText("1.48 s · $0.000412")
                    LiveCaret()
                }
                Spacer(minLength: 0)
                IconButton(systemImage: "doc.on.doc", label: "Copy answer") {}
            }
            DisclosureRow("Timing & usage", isExpanded: $isTimingOpen) {
                DetailRow("Tap → answer", value: "1.482 s")
                DetailRow("OpenRouter round trip", value: "1.447 s")
            }
            LCButton("Retry same text with selected model", systemImage: "arrow.clockwise", kind: .quiet) {}
        }
    }
}

/// A form: a secure field with its buttons and chip, labelled fields, and the full-width primary button.
private struct GalleryForms: View {
    @Environment(\.theme) private var theme
    @State private var key = ""
    @State private var address = ""
    @State private var pairingText = ""

    var body: some View {
        LCScaffoldLayout(scrolls: true, content: content)
    }

    private var content: some View {
        Group {
            keyPanel
            pairingPanel
        }
    }

    private var keyPanel: some View {
        Panel("Meta Muse · direct cloud") {
            LCSecureField("", text: $key, placeholder: "Paste Meta API key")
            HStack(spacing: theme.space.controlGap) {
                LCButton("Save key") {}
                Spacer(minLength: 0)
                StatusChip("Key saved", tone: .success, mark: .check)
            }
            LCButton("Test connection (no audio)", kind: .secondary) {}
            LCButton("Remove key", kind: .destructive) {}
            CaptionText("Meta connection verified directly. No microphone audio was sent.", tone: .success)
            LCButton("Open Meta developer dashboard", systemImage: "arrow.up.right.square", kind: .quiet) {}
        }
    }

    private var pairingPanel: some View {
        Panel("Manual pairing") {
            LCTextField("PC address", text: $address, placeholder: "https://pc.example.test", isURL: true)
            LCTextField("Pairing text", text: $pairingText, placeholder: "Paste pairing text", lines: 2...4, monospaced: true)
            LCButton("Verify and pair", size: .large, fullWidth: true) {}
        }
    }
}

// MARK: - Set 2: every variant

/// Buttons in every kind and state, icon buttons, spinners, chips, progress and the level meter.
private struct GalleryControls: View {
    @Environment(\.theme) private var theme

    var body: some View {
        LCScaffoldLayout(scrolls: true, content: content)
            .lcBottomBar {
                SessionStartControl(title: "Connecting…", systemImage: "mic.fill", isBusy: true) {}
            }
    }

    private var content: some View {
        Group {
            buttonPanel
            statusPanel
        }
    }

    private var buttonPanel: some View {
        Panel("Buttons") {
            LCButton("Scan PC QR code", systemImage: "qrcode", size: .large, fullWidth: true) {}
            HStack(spacing: theme.space.controlGap) {
                LCButton("Save key") {}
                LCButton("Reconnect", systemImage: "arrow.clockwise", kind: .secondary) {}
            }
            HStack(spacing: theme.space.controlGap) {
                LCButton("Test connection", kind: .secondary, isBusy: true, busyTitle: "Connecting…") {}
                LCButton("Remove key", kind: .destructive) {}
            }
            HStack(spacing: theme.space.controlGap) {
                LCButton("Save key") {}
                    .disabled(true)
                LCButton("Refresh status", systemImage: "arrow.clockwise", kind: .quiet) {}
            }
            iconButtons
        }
    }

    private var iconButtons: some View {
        HStack(spacing: theme.space.controlGap) {
            IconButton(systemImage: "doc.on.doc", label: "Copy", style: .raised) {}
            IconButton(systemImage: "slider.horizontal.3", label: "Settings") {}
            IconButton(systemImage: "xmark", label: "Close", style: .chrome) {}
            IconButton(systemImage: "trash", label: "Remove", style: .raised, isBusy: true) {}
            Spinner()
            Spinner(size: theme.component.smallSpinner)
        }
    }

    private var statusPanel: some View {
        Panel("Status", chip: StatusChipModel("Setup", tone: .warning)) {
            HStack(spacing: theme.space.s2) {
                StatusChip("Listening", tone: .live, mark: .pulsingDot)
                StatusChip("Ready", tone: .success, mark: .check)
                StatusChip("Paused", tone: .warning, mark: .pause)
            }
            HStack(spacing: theme.space.s2) {
                StatusChip("Needs attention", tone: .danger, mark: .alert)
                StatusChip("PC offline", tone: .neutral, mark: .ring)
            }
            HStack(spacing: theme.space.s2) {
                StatusChip("Connecting…", tone: .live, mark: .spinner)
                StatusChip("Latest", tone: .live, mark: .none)
                StatusChip("Online", tone: .success)
            }
            LCProgressBar(value: 0.42)
            LCProgressBar(value: 0.8, failed: true)
            HStack(spacing: theme.space.s4) {
                LevelMeter(level: 1, state: .listening)
                LevelMeter(level: 0, state: .idle)
                LevelMeter(level: 0, state: .paused)
            }
        }
    }
}

/// Choice rows, a plain row, detail rows, a card and a banner.
private struct GalleryLists: View {
    @Environment(\.theme) private var theme

    var body: some View {
        LCScaffoldLayout(scrolls: true, content: content)
            .lcBottomBar {
                SessionControlCluster(
                    isPaused: true,
                    isAssisting: true,
                    isTransitioning: false,
                    onPauseResume: {},
                    onAssist: {},
                    onStop: {}
                )
            }
    }

    private var content: some View {
        Group {
            servicePanel
            SectionTitle("Pinned source")
            detailPanel
            Banner(tone: .danger, title: "Assist could not finish", message: "OpenRouter rate-limited this request.")
        }
    }

    private var servicePanel: some View {
        Panel("Answer service") {
            ChoiceRow(title: "PC · Codex", subtitle: "Through your paired PC. Adds a summary when you stop.", isSelected: true) {}
            ChoiceRow(title: "OpenRouter · direct", subtitle: "Straight from this iPhone. Answers stream in.", isSelected: false) {}
            LCNavigationRow(route: .reasoning, title: "Reasoning", value: "Low")
        }
    }

    private var detailPanel: some View {
        Panel {
            DetailRow("Status", value: "Ready")
            DetailRow("Package", value: "FluidAudio 0.15.6")
            DetailRow("Estimated cost", value: "$0.01260 USD", valueTone: .warning)
            Card {
                Text(verbatim: "One more week for the onboarding flow.")
                    .lcText(.body)
            }
        }
    }
}

/// The toast, a banner, the small mark with a progress bar, and the empty state, above a cluster that is
/// disabled because an Assist is running.
private struct GalleryMessages: View {
    @Environment(\.theme) private var theme

    var body: some View {
        LCScaffoldLayout(scrolls: true, content: content)
            .lcBottomBar {
                SessionControlCluster(
                    isPaused: false,
                    isAssisting: true,
                    isTransitioning: false,
                    onPauseResume: {},
                    onAssist: {},
                    onStop: {}
                )
            }
    }

    private var content: some View {
        Group {
            LCToastTile(message: "Copied")
                .frame(maxWidth: .infinity)
            Banner(tone: .success, title: "Paired", message: "Your pairing is saved on this iPhone.")
            Panel("Model Library") {
                HStack(spacing: theme.space.s3) {
                    BrandMark(height: theme.component.compactBrandMark)
                    BrandName(role: .headline)
                    Spacer(minLength: 0)
                    StatusChip("Recommended", tone: .live, mark: .none)
                }
                LCProgressBar(value: 0.64)
                CaptionText("64% setup · 01:12 elapsed")
            }
            EmptyStateView(systemImage: "clock", title: "No conversations yet", message: "Completed sessions will appear here.")
        }
    }
}

/// One sample three times: with Reduce Transparency, with Increase Contrast, at an accessibility text size.
private struct GalleryVariants: View {
    @Environment(\.theme) private var theme

    var body: some View {
        LCScaffoldLayout(scrolls: true, content: content)
            .lcBottomBar {
                SessionControlCluster(
                    isPaused: false,
                    isAssisting: false,
                    isTransitioning: true,
                    onPauseResume: {},
                    onAssist: {},
                    onStop: {}
                )
            }
    }

    private var content: some View {
        Group {
            SectionTitle("Reduce Transparency")
            GallerySample()
                .environment(\.theme, theme.resolved(reduceTransparency: true, increaseContrast: false))
            SectionTitle("Increase Contrast")
            GallerySample()
                .environment(\.theme, theme.resolved(reduceTransparency: true, increaseContrast: true))
            SectionTitle("Accessibility text size")
            GallerySample()
                .environment(\.dynamicTypeSize, .accessibility2)
        }
    }
}

private struct GallerySample: View {
    @Environment(\.theme) private var theme

    var body: some View {
        Panel("Saved PC", chip: StatusChipModel("Connected", tone: .success, mark: .check)) {
            LCNavigationRow(route: .pairing, title: "PC connection", value: "Paired", systemImage: "desktopcomputer") {
                CaptionText("Saved · connected")
            }
            HStack(spacing: theme.space.controlGap) {
                LCButton("Reconnect") {}
                LCButton("Remove", kind: .destructive) {}
                StatusChip("Paused", tone: .warning)
            }
        }
    }
}
#endif
