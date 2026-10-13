import SwiftUI

/// A header that shows or hides its content (design system 4.24). `identifier` and the two labels go on
/// the header button.
///
/// The header is a full-width row with the title on the left and a chevron on the right: down when
/// closed, up when open. The title does not change. `collapsedLabel` and `expandedLabel` are what
/// assistive technology reads in each state; without them it reads the title and "Collapsed" or
/// "Expanded". The content appears directly below, inside the same card or panel, and nothing above it
/// moves. It is never the only place a required action lives.
struct DisclosureRow<Content: View>: View {
    private let title: String
    private let isExpanded: Binding<Bool>
    private let identifier: String?
    private let collapsedLabel: String?
    private let expandedLabel: String?
    private let content: Content

    @Environment(\.theme) private var theme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(
        _ title: String,
        isExpanded: Binding<Bool>,
        identifier: String? = nil,
        collapsedLabel: String? = nil,
        expandedLabel: String? = nil,
        @ViewBuilder content: () -> Content
    ) {
        self.title = title
        self.isExpanded = isExpanded
        self.identifier = identifier
        self.collapsedLabel = collapsedLabel
        self.expandedLabel = expandedLabel
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Button(action: toggle) {
                HStack(alignment: .center, spacing: theme.space.s3) {
                    Text(verbatim: title)
                        .lcText(.label)
                        .multilineTextAlignment(.leading)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    LCGlyph(isExpanded.wrappedValue ? "chevron.up" : "chevron.down", size: .small)
                        .foregroundStyle(theme.ink.secondary)
                }
                .frame(minHeight: theme.size.minimumTarget)
            }
            .buttonStyle(LCRowButtonStyle())
            .accessibilityLabel(currentLabel)
            .accessibilityValue(stateDescription)
            .accessibilityIdentifier(identifier ?? "")
            if isExpanded.wrappedValue {
                LCSurfaceStack(content)
                    .padding(.bottom, theme.space.s1)
                    .transition(.opacity)
            }
        }
        .lcStackRole(.disclosure)
    }

    /// The content fades in while the container grows downward; with Reduce Motion it is a short fade.
    private func toggle() {
        withAnimation(reduceMotion ? theme.motion.fast : theme.motion.base) {
            isExpanded.wrappedValue.toggle()
        }
    }

    private var currentLabel: String {
        let label = isExpanded.wrappedValue ? expandedLabel : collapsedLabel
        return label ?? title
    }

    /// The state in words, unless the label already says what a press will do.
    private var stateDescription: String {
        let label = isExpanded.wrappedValue ? expandedLabel : collapsedLabel
        guard label == nil else {
            return ""
        }
        return isExpanded.wrappedValue ? "Expanded" : "Collapsed"
    }
}
