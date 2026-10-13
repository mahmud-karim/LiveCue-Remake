import SwiftUI

/// A content panel (design system 4.9), with an optional header title and trailing chip.
///
/// It sits on the ground and is never glass: its fill is the app's own, so the contrast of the text on it
/// is known. List the children; rows inside it are separated by hairlines and everything else sits 12
/// apart. `compact` is the form for a one- or two-line strip: a tighter radius and padding.
struct Panel<Content: View>: View {
    private let title: String?
    private let chip: StatusChipModel?
    private let compact: Bool
    private let content: Content

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(_ title: String? = nil, chip: StatusChipModel? = nil, compact: Bool = false, @ViewBuilder content: () -> Content) {
        self.title = title
        self.chip = chip
        self.compact = compact
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: theme.space.groupGap) {
            if let title {
                header(title)
                    .accessibilityElement(children: .combine)
                    .accessibilityAddTraits(.isHeader)
            }
            LCSurfaceStack(content)
        }
        .padding(.horizontal, theme.space.panelInset)
        .padding(.vertical, compact ? theme.component.compactPanelInset : theme.space.panelInset)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background {
            LCPanelSurface(
                shape: RoundedRectangle(
                    cornerRadius: compact ? theme.radius.compactPanel : theme.radius.panel,
                    style: .continuous
                )
            )
        }
    }

    /// The title, then the chip: beside it, or below it at accessibility text sizes (design system 6.1).
    @ViewBuilder
    private func header(_ title: String) -> some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.s2) {
                titleText(title)
                chipView
            }
        } else {
            HStack(alignment: .center, spacing: theme.space.s3) {
                titleText(title)
                Spacer(minLength: 0)
                chipView
            }
            .frame(minHeight: theme.size.chip)
        }
    }

    private func titleText(_ title: String) -> some View {
        Text(verbatim: title)
            .lcText(.headline)
            .fixedSize(horizontal: false, vertical: true)
    }

    @ViewBuilder
    private var chipView: some View {
        if let chip {
            StatusChip(chip)
        }
    }
}
