import Foundation

/// A stretch of text with one style.
struct MarkupSpan: Equatable, Sendable {
    var text: String
    var isBold = false
    var isItalic = false
    /// Monospaced.
    var isCode = false
}

/// One block of an answer: a paragraph, a heading, a list item or a block of code.
struct MarkupBlock: Equatable, Sendable {
    enum Kind: Equatable, Sendable {
        case paragraph
        /// Shown as a bold line.
        case heading
        /// `marker` is "•" for a bullet and "1." for a numbered item; `level` is 0 to 3.
        case item(marker: String, level: Int)
        /// Lines between two fences, kept as they are.
        case code
    }

    var kind: Kind
    var spans: [MarkupSpan]

    /// The text of the block without any styling.
    var plainText: String {
        spans.map(\.text).joined()
    }
}

/// Reads the small part of Markdown that models answer in, and nothing else (1.1.0).
///
/// Bold, italic, inline code, headings, bullet and numbered lists, code fences, paragraphs and line
/// breaks become styling. A link or an image never becomes active: it is shown as its text followed by
/// its address in brackets, as plain text. Whatever is not recognised stays as written, and so does a
/// marker that is not closed, which is what the end of an answer that is still streaming looks like.
///
/// The text is untrusted: it came from a model that read what other people said. So this is written by
/// hand, makes no link attribute of any kind, and reads its input once: every search is bounded, and
/// the nesting it follows is capped, so no input can make it slow.
enum AnswerMarkup {
    // MARK: - Blocks

    static func blocks(_ text: String) -> [MarkupBlock] {
        let normalised = text
            .replacingOccurrences(of: "\r\n", with: "\n")
            .replacingOccurrences(of: "\r", with: "\n")
        let lines = normalised.split(separator: "\n", omittingEmptySubsequences: false).map { String($0) }

        var blocks: [MarkupBlock] = []
        var paragraph: [String] = []
        var item: OpenItem?
        var fence: [String]?

        func closeParagraph() {
            if !paragraph.isEmpty {
                blocks.append(MarkupBlock(kind: .paragraph, spans: inline(paragraph.joined(separator: "\n"))))
                paragraph = []
            }
        }
        func closeItem() {
            if let open = item {
                let spans = inline(open.lines.joined(separator: " "))
                blocks.append(MarkupBlock(kind: .item(marker: open.marker, level: open.level), spans: spans))
                item = nil
            }
        }
        func closeFence() {
            if let open = fence {
                let code = open.joined(separator: "\n")
                if !code.isEmpty {
                    blocks.append(MarkupBlock(kind: .code, spans: [MarkupSpan(text: code, isCode: true)]))
                }
                fence = nil
            }
        }

        for line in lines {
            let trimmed = line.trimmingCharacters(in: .whitespaces)

            if fence != nil {
                if trimmed.hasPrefix(fenceMark) {
                    closeFence()
                } else {
                    fence?.append(line)
                }
                continue
            }
            if trimmed.hasPrefix(fenceMark) {
                closeParagraph()
                closeItem()
                fence = []
                continue
            }
            if trimmed.isEmpty {
                closeParagraph()
                closeItem()
                continue
            }
            if let heading = headingText(trimmed) {
                closeParagraph()
                closeItem()
                blocks.append(MarkupBlock(kind: .heading, spans: inline(heading)))
                continue
            }
            if let found = listItem(line) {
                closeParagraph()
                closeItem()
                item = found
                continue
            }
            if item != nil, line.hasPrefix("  ") || line.hasPrefix("\t") {
                // An indented line under a list item goes on with that item.
                item?.lines.append(trimmed)
                continue
            }
            closeItem()
            paragraph.append(trimmed)
        }
        closeParagraph()
        closeItem()
        // A fence that was never closed: the answer is still arriving, or the model forgot. Its lines are code.
        closeFence()
        return blocks
    }

    /// The answer as plain text, without the symbols: what "Copy answer" puts on the clipboard.
    static func plainText(_ text: String) -> String {
        var result = ""
        var previousWasItem = false
        for block in blocks(text) {
            var line = block.plainText
            var isItem = false
            if case .item(let marker, let level) = block.kind {
                isItem = true
                line = String(repeating: "  ", count: level) + marker + " " + line
            }
            if !result.isEmpty {
                // List items follow each other line by line; everything else is set apart.
                result += (isItem && previousWasItem) ? "\n" : "\n\n"
            }
            result += line
            previousWasItem = isItem
        }
        return result
    }

    private struct OpenItem {
        var marker: String
        var level: Int
        var lines: [String]
    }

    private static let fenceMark = "```"
    private static let bulletMarks: Set<Character> = ["-", "*", "+", "•"]
    private static let maximumLevel = 3

    /// "## Title ##" gives "Title"; nil for a line that is not a heading.
    private static func headingText(_ trimmed: String) -> String? {
        var hashes = 0
        var index = trimmed.startIndex
        while index < trimmed.endIndex, trimmed[index] == "#" {
            hashes += 1
            index = trimmed.index(after: index)
        }
        guard hashes >= 1, hashes <= 6, index < trimmed.endIndex, trimmed[index] == " " else {
            return nil
        }
        var title = String(trimmed[index...]).trimmingCharacters(in: .whitespaces)
        while title.hasSuffix("#") {
            title.removeLast()
        }
        title = title.trimmingCharacters(in: .whitespaces)
        return title.isEmpty ? nil : title
    }

    /// "- text", "* text", "+ text", "• text", "1. text" and "1) text", with their indentation.
    private static func listItem(_ line: String) -> OpenItem? {
        let characters = Array(line)
        var indent = 0
        var index = 0
        while index < characters.count, characters[index] == " " || characters[index] == "\t" {
            indent += characters[index] == "\t" ? 4 : 1
            index += 1
        }
        guard index < characters.count else { return nil }
        let level = min(indent / 2, maximumLevel)

        if bulletMarks.contains(characters[index]) {
            let mark = characters[index]
            guard index + 1 < characters.count, characters[index + 1] == " " || characters[index + 1] == "\t" else {
                return nil
            }
            let text = String(characters[(index + 2)...]).trimmingCharacters(in: .whitespaces)
            guard !text.isEmpty else { return nil }
            // "- - -" and "* * *" are rules, not lists: left as written.
            if text.allSatisfy({ $0 == mark || $0 == " " }) {
                return nil
            }
            return OpenItem(marker: "•", level: level, lines: [text])
        }

        var digits = ""
        while index < characters.count, characters[index].isASCII, characters[index].isNumber, digits.count < 4 {
            digits.append(characters[index])
            index += 1
        }
        guard !digits.isEmpty, digits.count <= 3, index + 1 < characters.count,
              characters[index] == "." || characters[index] == ")",
              characters[index + 1] == " " || characters[index + 1] == "\t" else {
            return nil
        }
        let text = String(characters[(index + 2)...]).trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty else { return nil }
        return OpenItem(marker: digits + ".", level: level, lines: [text])
    }

    // MARK: - Inline

    private enum Token {
        case text(String)
        case code(String)
        case delimiter(character: Character, length: Int, canOpen: Bool, canClose: Bool)
    }

    /// The longest label and address of a link that is looked for: a search never runs further.
    private static let maximumLabel = 300
    private static let maximumAddress = 2_000
    /// How many open markers are remembered. Older ones stay as written.
    private static let maximumOpenMarkers = 64

    /// The spans of one block of text.
    static func inline(_ text: String) -> [MarkupSpan] {
        let tokens = tokenise(Array(text))

        // Pair the markers: a closing marker ends the nearest open one of the same kind. A marker that
        // finds no partner stays as written.
        var isActive = [Bool](repeating: false, count: tokens.count)
        var isOpener = [Bool](repeating: false, count: tokens.count)
        var open: [Int] = []
        for (position, token) in tokens.enumerated() {
            guard case .delimiter(let character, let length, let canOpen, let canClose) = token else { continue }
            var matched = false
            if canClose {
                if let found = open.lastIndex(where: { isDelimiter(tokens[$0], character, length) }) {
                    isActive[open[found]] = true
                    isOpener[open[found]] = true
                    isActive[position] = true
                    open.removeSubrange(found...)
                    matched = true
                } else if length == 3,
                          let inner = open.lastIndex(where: { isDelimiter(tokens[$0], character, 1) || isDelimiter(tokens[$0], character, 2) }) {
                    // Three closing markers end a bold and an italic stretch that were opened apart.
                    let other = isDelimiter(tokens[open[inner]], character, 1) ? 2 : 1
                    if let outer = open[..<inner].lastIndex(where: { isDelimiter(tokens[$0], character, other) }) {
                        for index in [open[inner], open[outer]] {
                            isActive[index] = true
                            isOpener[index] = true
                        }
                        isActive[position] = true
                        open.removeSubrange(outer...)
                        matched = true
                    }
                }
            }
            if !matched, canOpen {
                open.append(position)
                if open.count > maximumOpenMarkers {
                    open.removeFirst()
                }
            }
        }

        var spans: [MarkupSpan] = []
        var bold = 0
        var italic = 0
        func add(_ text: String, isCode: Bool = false) {
            guard !text.isEmpty else { return }
            let span = MarkupSpan(text: text, isBold: bold > 0, isItalic: italic > 0, isCode: isCode)
            if let last = spans.last, last.isBold == span.isBold, last.isItalic == span.isItalic, last.isCode == span.isCode {
                spans[spans.count - 1].text += text
            } else {
                spans.append(span)
            }
        }
        for (position, token) in tokens.enumerated() {
            switch token {
            case .text(let text):
                add(text)
            case .code(let code):
                add(code, isCode: true)
            case .delimiter(let character, let length, _, _):
                guard isActive[position] else {
                    add(String(repeating: String(character), count: length))
                    continue
                }
                let change = isOpener[position] ? 1 : -1
                if length == 1 || length == 3 {
                    italic += change
                }
                if length >= 2 {
                    bold += change
                }
            }
        }
        return spans
    }

    private static func isDelimiter(_ token: Token, _ character: Character, _ length: Int) -> Bool {
        if case .delimiter(let itsCharacter, let itsLength, _, _) = token {
            return itsCharacter == character && itsLength == length
        }
        return false
    }

    private static func tokenise(_ characters: [Character]) -> [Token] {
        var tokens: [Token] = []
        var buffer = ""
        var index = 0
        let count = characters.count

        func flush() {
            if !buffer.isEmpty {
                tokens.append(.text(buffer))
                buffer = ""
            }
        }

        while index < count {
            let character = characters[index]
            switch character {
            case "\\":
                // A backslash before a symbol shows the symbol.
                if index + 1 < count, characters[index + 1].isPunctuation || characters[index + 1].isSymbol {
                    buffer.append(characters[index + 1])
                    index += 2
                } else {
                    buffer.append(character)
                    index += 1
                }

            case "`":
                var run = 1
                while index + run < count, characters[index + run] == "`" {
                    run += 1
                }
                if run == 1, let close = closing("`", in: characters, from: index + 1, limit: maximumAddress), close > index + 1 {
                    flush()
                    tokens.append(.code(String(characters[(index + 1)..<close])))
                    index = close + 1
                } else {
                    // Not closed on this line, or more than one: as written.
                    buffer.append(String(repeating: "`", count: run))
                    index += run
                }

            case "!", "[":
                let start = character == "!" ? index + 1 : index
                if start < count, characters[start] == "[", let found = link(in: characters, at: start) {
                    // Never active: the text, then the address in brackets, as plain text.
                    buffer.append(found.shown)
                    index = found.end
                } else {
                    buffer.append(character)
                    index += 1
                }

            case "<":
                // Only what begins like an address is looked at further.
                if beginsAddress(characters, at: index + 1),
                   let close = closing(">", in: characters, from: index + 1, limit: maximumAddress),
                   isAddress(characters[(index + 1)..<close]) {
                    buffer.append(String(characters[(index + 1)..<close]))
                    index = close + 1
                } else {
                    buffer.append(character)
                    index += 1
                }

            case "*", "_":
                var run = 1
                while index + run < count, characters[index + run] == character {
                    run += 1
                }
                let previous: Character? = index > 0 ? characters[index - 1] : nil
                let next: Character? = index + run < count ? characters[index + run] : nil
                var canOpen = next.map { !$0.isWhitespace } ?? false
                var canClose = previous.map { !$0.isWhitespace } ?? false
                if character == "_" {
                    // An underscore inside a word is part of the word: snake_case stays as written.
                    canOpen = canOpen && !(previous.map(isWordCharacter) ?? false)
                    canClose = canClose && !(next.map(isWordCharacter) ?? false)
                }
                if run > 3 || (!canOpen && !canClose) {
                    buffer.append(String(repeating: String(character), count: run))
                } else {
                    flush()
                    tokens.append(.delimiter(character: character, length: run, canOpen: canOpen, canClose: canClose))
                }
                index += run

            default:
                buffer.append(character)
                index += 1
            }
        }
        flush()
        return tokens
    }

    private static func isWordCharacter(_ character: Character) -> Bool {
        character.isLetter || character.isNumber
    }

    /// The index of the next `target` on the same line, looking at most `limit` characters ahead.
    private static func closing(_ target: Character, in characters: [Character], from start: Int, limit: Int) -> Int? {
        var index = start
        let end = min(characters.count, start + limit)
        while index < end {
            let character = characters[index]
            if character == target {
                return index
            }
            if character == "\n" {
                return nil
            }
            index += 1
        }
        return nil
    }

    /// "[label](address)" starting at the bracket: what is shown for it, and where it ends.
    private static func link(in characters: [Character], at bracket: Int) -> (shown: String, end: Int)? {
        guard let labelEnd = closing("]", in: characters, from: bracket + 1, limit: maximumLabel),
              labelEnd + 1 < characters.count, characters[labelEnd + 1] == "(",
              let addressEnd = closing(")", in: characters, from: labelEnd + 2, limit: maximumAddress) else {
            return nil
        }
        let label = String(characters[(bracket + 1)..<labelEnd]).trimmingCharacters(in: .whitespaces)
        // An address may be followed by a title in quotes: only the address is shown.
        var address = String(characters[(labelEnd + 2)..<addressEnd]).trimmingCharacters(in: .whitespaces)
        if let space = address.firstIndex(of: " ") {
            address = String(address[..<space])
        }
        if address.hasPrefix("<"), address.hasSuffix(">"), address.count >= 2 {
            address = String(address.dropFirst().dropLast())
        }
        guard !address.isEmpty else { return nil }
        let shown = (label.isEmpty || label == address) ? address : label + " (" + address + ")"
        return (shown, addressEnd + 1)
    }

    private static let addressSchemes: [[Character]] = [Array("https://"), Array("http://"), Array("mailto:")]

    /// The characters from `start` begin with "https://", "http://" or "mailto:".
    private static func beginsAddress(_ characters: [Character], at start: Int) -> Bool {
        for scheme in addressSchemes where start + scheme.count <= characters.count {
            if Array(characters[start..<(start + scheme.count)]) == scheme {
                return true
            }
        }
        return false
    }

    /// "<https://…>": an address between angle brackets, with no space in it.
    private static func isAddress(_ characters: ArraySlice<Character>) -> Bool {
        !characters.isEmpty && !characters.contains(where: { $0.isWhitespace })
    }
}
