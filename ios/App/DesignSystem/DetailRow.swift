import SwiftUI

/// A key on the left and its value on the right, in tabular digits.
///
/// Design system 4.8, detail row: read-only facts and timings, read as "key, value". Consecutive detail
/// rows in a panel, a card or a disclosure touch, with no separator. At accessibility text sizes the value
/// moves under the key.
struct DetailRow: View {
    private let key: String
    private let value: String
    private let valueTone: StatusTone?

    @Environment(\.theme) private var theme
    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    init(_ key: String, value: String, valueTone: StatusTone? = nil) {
        self.key = key
        self.value = value
        self.valueTone = valueTone
    }

    var body: some View {
        content
            .frame(maxWidth: .infinity, minHeight: theme.component.detailRow, alignment: .leading)
            .accessibilityElement(children: .combine)
            .lcStackRole(.detail)
    }

    @ViewBuilder
    private var content: some View {
        if dynamicTypeSize.isAccessibilitySize {
            VStack(alignment: .leading, spacing: theme.space.half) {
                keyText
                valueText
            }
            .padding(.vertical, theme.space.s1)
        } else {
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s3) {
                keyText
                Spacer(minLength: 0)
                valueText
                    .multilineTextAlignment(.trailing)
            }
        }
    }

    private var keyText: some View {
        Text(verbatim: key)
            .lcText(.caption, color: theme.ink.secondary)
    }

    private var valueText: some View {
        Text(verbatim: value)
            .lcText(.caption, color: valueTone.map { theme.status.color(for: $0) } ?? theme.ink.primary, tabular: true)
    }
}
