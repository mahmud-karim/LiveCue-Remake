import SwiftUI

/// A row that pushes `route`. Title and value form the link's accessibility label and value. The subtitle
/// is laid over the link without hit testing and stays its own accessibility element, so an identifier
/// set inside it is reachable by UI tests. `identifier` goes on the link.
///
/// Design system 4.8: an optional icon tile, the title, an optional value, a chevron. With a subtitle it
/// is a value row: the value shares the first line in `type.caption` and the subtitle takes the full
/// width below. At accessibility text sizes the value moves under the title.
struct LCNavigationRow<Subtitle: View>: View {
    private let route: Route
    private let title: String
    private let value: String?
    private let valueTone: StatusTone?
    private let systemImage: String?
    private let identifier: String?
    private let subtitle: Subtitle

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(
        route: Route,
        title: String,
        value: String? = nil,
        valueTone: StatusTone? = nil,
        systemImage: String? = nil,
        identifier: String? = nil,
        @ViewBuilder subtitle: () -> Subtitle
    ) {
        self.route = route
        self.title = title
        self.value = value
        self.valueTone = valueTone
        self.systemImage = systemImage
        self.identifier = identifier
        self.subtitle = subtitle()
    }

    var body: some View {
        // The row is laid out twice with the same geometry. The link's label shows everything but the
        // subtitle, which only holds its place there; the overlay shows the subtitle alone.
        NavigationLink(value: route) {
            layout(showsSubtitle: false)
        }
        .buttonStyle(LCRowButtonStyle())
        .accessibilityLabel(title)
        .accessibilityValue(value ?? "")
        .accessibilityIdentifier(identifier ?? "")
        .overlay {
            if hasSubtitle {
                layout(showsSubtitle: true)
                    .allowsHitTesting(false)
            }
        }
        .lcStackRole(.row, separatorInset: separatorInset)
    }

    private var hasSubtitle: Bool {
        Subtitle.self != EmptyView.self
    }

    /// The hairline above the row starts at the leading edge of the title.
    private var separatorInset: CGFloat {
        guard systemImage != nil else {
            return 0
        }
        return dynamicTypeSize.lcScaledTile(theme.size.iconTile) + theme.space.s3
    }

    private func layout(showsSubtitle: Bool) -> some View {
        HStack(alignment: .center, spacing: theme.space.s3) {
            if let systemImage {
                LCIconTile(systemImage: systemImage)
                    .opacity(showsSubtitle ? 0 : 1)
            }
            VStack(alignment: .leading, spacing: theme.space.half) {
                firstLine
                    .opacity(showsSubtitle ? 0 : 1)
                    .accessibilityHidden(showsSubtitle)
                if hasSubtitle {
                    subtitle
                        .font(TextRole.caption.font)
                        .foregroundStyle(theme.ink.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .opacity(showsSubtitle ? 1 : 0)
                        .accessibilityHidden(!showsSubtitle)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            LCGlyph("chevron.right", size: .small)
                .foregroundStyle(theme.ink.secondary)
                .opacity(showsSubtitle ? 0 : 1)
        }
        .padding(.vertical, theme.component.rowInset)
        .frame(minHeight: hasSubtitle ? theme.size.twoLineRow : theme.size.row)
    }

    @ViewBuilder
    private var firstLine: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.half) {
                titleText
                valueText
            }
        } else {
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s2) {
                titleText
                    .layoutPriority(1)
                Spacer(minLength: 0)
                valueText
                    .lineLimit(1)
            }
        }
    }

    private var titleText: some View {
        Text(verbatim: title)
            .lcText(.body)
            .multilineTextAlignment(.leading)
    }

    /// The value truncates before the title does.
    @ViewBuilder
    private var valueText: some View {
        if let value {
            Text(verbatim: value)
                .lcText(hasSubtitle ? .caption : .body, color: valueColor)
        }
    }

    private var valueColor: Color {
        valueTone.map { theme.status.color(for: $0) } ?? theme.ink.secondary
    }
}

extension LCNavigationRow where Subtitle == EmptyView {
    init(
        route: Route,
        title: String,
        value: String? = nil,
        valueTone: StatusTone? = nil,
        systemImage: String? = nil,
        identifier: String? = nil
    ) {
        self.init(
            route: route,
            title: title,
            value: value,
            valueTone: valueTone,
            systemImage: systemImage,
            identifier: identifier,
            subtitle: { EmptyView() }
        )
    }
}
