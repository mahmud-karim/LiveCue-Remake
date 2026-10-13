import SwiftUI

// How a panel or a card lays out its children (design system 4.8 and 4.9). Rows are stacked without a
// gap and separated by a hairline; everything else sits 12 apart. A child says what it is through a
// container value, and the stack reads that from each of its subviews, so a screen only lists the rows.

/// What a child of a panel or a card is, as far as the layout around it goes.
enum LCStackRole: Equatable, Sendable {
    /// Anything that is not a row: 12 from its neighbours.
    case block
    /// A list row. Adjacent rows touch, with a hairline between them.
    case row
    /// A detail row. Adjacent ones touch, with no separator.
    case detail
    /// A disclosure header: a hairline above it, unless it comes first.
    case disclosure
}

struct LCStackTrait: Equatable, Sendable {
    var role: LCStackRole = .block
    /// How far the hairline above a row starts from the leading edge: the leading edge of its title.
    var separatorInset: CGFloat = 0
}

extension ContainerValues {
    @Entry var lcStackTrait: LCStackTrait = LCStackTrait()
}

extension View {
    /// Tells the enclosing panel or card what this child is. Applied by the row components themselves.
    func lcStackRole(_ role: LCStackRole, separatorInset: CGFloat = 0) -> some View {
        containerValue(\.lcStackTrait, LCStackTrait(role: role, separatorInset: separatorInset))
    }
}

/// The children of a panel, a card or an open disclosure, top to bottom.
struct LCSurfaceStack<Content: View>: View {
    private let content: Content

    init(_ content: Content) {
        self.content = content
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Group(subviews: content) { subviews in
                ForEach(subviews) { subview in
                    LCStackItem(subview: subview, join: LCStackJoin(above: subview, in: subviews))
                }
            }
        }
    }
}

/// What goes between a child and the one above it.
private enum LCStackJoin: Equatable {
    case nothing
    case gap
    case separator(inset: CGFloat, isSpaced: Bool)

    init(above subview: Subview, in subviews: SubviewsCollection) {
        guard
            let index = subviews.firstIndex(where: { $0.id == subview.id }),
            index != subviews.startIndex
        else {
            self = .nothing
            return
        }

        let previous = subviews[subviews.index(before: index)].containerValues.lcStackTrait
        let current = subview.containerValues.lcStackTrait

        switch (previous.role, current.role) {
        case (.row, .row):
            // No separator after the last row, and none that starts left of either title.
            self = .separator(inset: min(previous.separatorInset, current.separatorInset), isSpaced: false)
        case (.row, .disclosure), (.disclosure, .row), (.disclosure, .disclosure):
            self = .separator(inset: 0, isSpaced: false)
        case (_, .disclosure):
            self = .separator(inset: 0, isSpaced: true)
        case (.detail, .detail):
            self = .nothing
        default:
            self = .gap
        }
    }
}

private struct LCStackItem: View {
    let subview: Subview
    let join: LCStackJoin

    @Environment(\.theme) private var theme

    var body: some View {
        switch join {
        case .nothing:
            subview
        case .gap:
            subview.padding(.top, theme.space.groupGap)
        case .separator(let inset, let isSpaced):
            LCHairline()
                .padding(.leading, inset)
                .padding(.top, isSpaced ? theme.space.groupGap : 0)
            subview
        }
    }
}

/// The pressed state of a row that does something (design system 4.8): `state.pressed` in a rounded shape
/// that reaches a little into the panel's padding. Rows that do nothing have no pressed state.
struct LCRowButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        LCRowFace(configuration: configuration)
    }
}

private struct LCRowFace: View {
    let configuration: ButtonStyleConfiguration

    @Environment(\.theme) private var theme
    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        configuration.label
            .contentShape(Rectangle())
            .background {
                RoundedRectangle(cornerRadius: theme.radius.field, style: .continuous)
                    .fill(theme.state.pressed)
                    .padding(.horizontal, -theme.space.s2)
                    .opacity(configuration.isPressed ? 1 : 0)
            }
            .opacity(isEnabled ? 1 : theme.opacity.disabled)
            .animation(theme.motion.press, value: configuration.isPressed)
    }
}
