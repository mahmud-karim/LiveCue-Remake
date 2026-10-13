import LiveCueCore
import SwiftUI

/// The transcript panel of the live screen: a two-line preview, or the whole transcript when expanded.
///
/// The header is a disclosure row. Collapsed, the panel shows the newest text in two lines (one in
/// compact height, three at the accessibility text sizes). Expanded, it lists every final segment and
/// then the interim text in a scroll area of a fixed height, which stays on the newest line while the
/// reader is at the bottom.
struct LiveTranscriptPanel: View {
    private let isCompact: Bool
    private let expandedHeight: CGFloat

    @Environment(LiveSessionViewModel.self) private var model
    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(isCompact: Bool = false, expandedHeight: CGFloat = LiveLayout.transcriptMaximumHeight) {
        self.isCompact = isCompact
        self.expandedHeight = expandedHeight
    }

    var body: some View {
        @Bindable var model = model

        Panel(compact: true) {
            // One child of the panel, so that the preview sits directly under the header.
            VStack(alignment: .leading, spacing: 0) {
                DisclosureRow(
                    "Live transcript",
                    isExpanded: $model.isTranscriptExpanded,
                    identifier: "expand-transcript",
                    collapsedLabel: "Expand live transcript",
                    expandedLabel: "Collapse live transcript"
                ) {
                    LiveTranscriptScroll(height: expandedHeight)
                }
                // The header keeps its full target and reaches into the panel's padding, so the
                // collapsed panel stays a strip (screens 4.1).
                .padding(.top, -theme.space.s2)
                if !model.isTranscriptExpanded {
                    preview
                        .lineLimit(previewLines, reservesSpace: true)
                        .truncationMode(.head)
                        .accessibilityIdentifier("transcript-preview")
                        .padding(.top, -theme.space.s1)
                }
            }
        }
    }

    /// The interim text if there is any, else the last final segment, else the placeholder.
    @ViewBuilder
    private var preview: some View {
        switch model.preview {
        case .interim(let text):
            TranscriptLine(text, isInterim: true, showsCaret: true)
        case .committed(let text):
            TranscriptLine(text)
        case .placeholder(let text):
            TranscriptLine(text, isInterim: true)
        }
    }

    private var previewLines: Int {
        if dynamicTypeSize.isAccessibilitySize {
            return 3
        }
        return isCompact ? 1 : 2
    }
}

/// The expanded transcript `live-transcript`: every final segment, then the interim text.
///
/// It follows the newest line while the reader is at the bottom. Once they scroll up nothing moves, and
/// "Jump to live" brings them back.
private struct LiveTranscriptScroll: View {
    let height: CGFloat

    @Environment(LiveSessionViewModel.self) private var model
    @Environment(\.theme) private var theme

    @State private var position = ScrollPosition(edge: .bottom)
    @State private var isAtBottom = true

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: theme.space.groupGap) {
                ForEach(model.segments) { segment in
                    TranscriptLine(segment.text)
                }
                tail
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .scrollPosition($position)
        .defaultScrollAnchor(.bottom)
        .frame(height: height)
        .onScrollGeometryChange(for: Bool.self) { geometry in
            LiveLayout.reachesBottom(
                offset: geometry.contentOffset.y,
                visibleHeight: geometry.containerSize.height,
                contentHeight: geometry.contentSize.height
            )
        } action: { _, atBottom in
            isAtBottom = atBottom
        }
        .onChange(of: model.segments.count) { _, _ in
            follow()
        }
        .onChange(of: model.partialText) { _, _ in
            follow()
        }
        .accessibilityIdentifier("live-transcript")
        .overlay(alignment: .bottom) {
            if !isAtBottom {
                LCButton("Jump to live", systemImage: "arrow.down", kind: .secondary, action: jumpToLive)
                    .padding(.bottom, theme.space.s1)
            }
        }
    }

    /// The interim text, or the placeholder while nothing was heard yet.
    @ViewBuilder
    private var tail: some View {
        if !model.partialText.isEmpty {
            TranscriptLine(model.partialText, isInterim: true, showsCaret: true)
        } else if model.segments.isEmpty, case .placeholder(let text) = model.preview {
            TranscriptLine(text, isInterim: true)
        }
    }

    private func follow() {
        if isAtBottom {
            position.scrollTo(edge: .bottom)
        }
    }

    private func jumpToLive() {
        withAnimation(theme.motion.base) {
            position.scrollTo(edge: .bottom)
        }
    }
}
