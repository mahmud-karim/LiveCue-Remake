import SwiftUI

/// Text a model wrote, with the small part of Markdown that `AnswerMarkup` reads shown as styling: bold,
/// italic, monospaced code, headings as bold lines, and list items after their marker in a hanging
/// indent (1.1.0).
///
/// Nothing in it can be tapped: the styled text is put together by hand from plain strings, and no link
/// is ever made. Every block is one `Text` in the role's Dynamic Type font, so the text size setting
/// applies, and each block can be selected. `tail` wraps the text of the last block, which is where a
/// streaming answer puts its caret.
struct MarkupText<Tail: View>: View {
    private let blocks: [MarkupBlock]
    private let role: TextRole
    private let color: Color?
    private let identifier: String?
    private let isSelectable: Bool
    private let tail: (Text) -> Tail

    @Environment(\.theme) private var theme

    init(
        _ text: String,
        role: TextRole,
        color: Color? = nil,
        identifier: String? = nil,
        isSelectable: Bool = true,
        @ViewBuilder tail: @escaping (Text) -> Tail
    ) {
        self.blocks = AnswerMarkup.blocks(text)
        self.role = role
        self.color = color
        self.identifier = identifier
        self.isSelectable = isSelectable
        self.tail = tail
    }

    var body: some View {
        VStack(alignment: .leading, spacing: theme.space.s2) {
            ForEach(Array(blocks.enumerated()), id: \.offset) { item in
                MarkupBlockRow(
                    block: item.element,
                    role: role,
                    color: color,
                    identifier: identifier,
                    isSelectable: isSelectable,
                    isLast: item.offset == blocks.count - 1,
                    tail: tail
                )
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

extension MarkupText where Tail == Text {
    /// Without anything around the last block.
    init(_ text: String, role: TextRole, color: Color? = nil, identifier: String? = nil, isSelectable: Bool = true) {
        self.init(text, role: role, color: color, identifier: identifier, isSelectable: isSelectable) { (last: Text) in
            last
        }
    }
}

/// The styled text of a block, put together from plain strings.
enum MarkupRendering {
    /// Bold, italic and code are presentation intents, which `Text` draws in the font it is given. No
    /// other attribute is set: in particular no link.
    static func attributed(_ block: MarkupBlock) -> AttributedString {
        var result = AttributedString()
        for span in block.spans {
            var piece = AttributedString(span.text)
            var intent: InlinePresentationIntent = []
            if span.isBold || block.kind == .heading {
                intent.insert(.stronglyEmphasized)
            }
            if span.isItalic {
                intent.insert(.emphasized)
            }
            if span.isCode {
                intent.insert(.code)
            }
            if !intent.isEmpty {
                piece.inlinePresentationIntent = intent
            }
            result.append(piece)
        }
        return result
    }
}

/// One block: its text, after its marker when it is a list item.
private struct MarkupBlockRow<Tail: View>: View {
    let block: MarkupBlock
    let role: TextRole
    let color: Color?
    let identifier: String?
    let isSelectable: Bool
    let isLast: Bool
    let tail: (Text) -> Tail

    @Environment(\.theme) private var theme

    var body: some View {
        if case .item(let marker, let level) = block.kind {
            HStack(alignment: .firstTextBaseline, spacing: theme.space.s1) {
                // The marker keeps a column of its own, so the lines of the item hang beside it.
                Text(verbatim: marker)
                    .lcText(role, color: color ?? theme.ink.secondary, tabular: true)
                    .frame(minWidth: theme.space.s4, alignment: .leading)
                line
            }
            .padding(.leading, CGFloat(level) * theme.space.s4)
        } else {
            line
        }
    }

    @ViewBuilder
    private var line: some View {
        if isLast {
            tail(text)
                .modifier(MarkupLineStyle(role: role, color: color, identifier: identifier, isSelectable: isSelectable))
        } else {
            text
                .modifier(MarkupLineStyle(role: role, color: color, identifier: identifier, isSelectable: isSelectable))
        }
    }

    private var text: Text {
        Text(MarkupRendering.attributed(block))
    }
}

/// The font and colour of the role, the full width, and selection and the identifier when asked for.
private struct MarkupLineStyle: ViewModifier {
    let role: TextRole
    let color: Color?
    let identifier: String?
    let isSelectable: Bool

    func body(content: Content) -> some View {
        if isSelectable {
            named(sized(content).textSelection(.enabled))
        } else {
            named(sized(content))
        }
    }

    private func sized(_ content: Content) -> some View {
        content
            .lcText(role, color: color)
            .frame(maxWidth: .infinity, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
    }

    @ViewBuilder
    private func named<Styled: View>(_ view: Styled) -> some View {
        if let identifier {
            view.accessibilityIdentifier(identifier)
        } else {
            view
        }
    }
}
