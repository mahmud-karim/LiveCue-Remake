import SwiftUI

/// One choice of a list: a single button with the selected trait and a trailing check.
///
/// Design system 4.8, choice row: the selected row shows a check in the accent and its title at semibold;
/// the others show nothing. The check always keeps its place, so nothing shifts when the selection moves.
struct ChoiceRow: View {
    private let title: String
    private let subtitle: String?
    private let isSelected: Bool
    private let action: @MainActor () -> Void

    @Environment(\.theme) private var theme

    init(title: String, subtitle: String? = nil, isSelected: Bool, action: @escaping @MainActor () -> Void) {
        self.title = title
        self.subtitle = subtitle
        self.isSelected = isSelected
        self.action = action
    }

    var body: some View {
        Button(action: action) {
            HStack(alignment: .center, spacing: theme.space.s3) {
                VStack(alignment: .leading, spacing: theme.space.half) {
                    Text(verbatim: title)
                        .font(TextRole.body.font.weight(isSelected ? .semibold : .regular))
                        .foregroundStyle(theme.ink.primary)
                        .multilineTextAlignment(.leading)
                    if let subtitle {
                        CaptionText(subtitle)
                            .multilineTextAlignment(.leading)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                LCGlyph("checkmark", size: .regular)
                    .foregroundStyle(theme.accent.fill)
                    .opacity(isSelected ? 1 : 0)
            }
            .padding(.vertical, theme.component.rowInset)
            .frame(minHeight: subtitle == nil ? theme.size.row : theme.size.twoLineRow)
        }
        .buttonStyle(LCRowButtonStyle())
        .accessibilityAddTraits(isSelected ? .isSelected : [])
        .lcStackRole(.row)
    }
}
