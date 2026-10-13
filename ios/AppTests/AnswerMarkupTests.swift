import SwiftUI
import XCTest
@testable import LiveCueRemake

/// The formatter of answers (1.1.0): each construct it shows as styling, what stays as written, links
/// and images that never become active, text that is still streaming, and input meant to make it slow.
final class AnswerMarkupTests: XCTestCase {
    private func plain(_ text: String) -> MarkupSpan {
        MarkupSpan(text: text)
    }

    private func bold(_ text: String) -> MarkupSpan {
        MarkupSpan(text: text, isBold: true)
    }

    private func italic(_ text: String) -> MarkupSpan {
        MarkupSpan(text: text, isItalic: true)
    }

    private func code(_ text: String) -> MarkupSpan {
        MarkupSpan(text: text, isCode: true)
    }

    private func paragraph(_ spans: MarkupSpan...) -> MarkupBlock {
        MarkupBlock(kind: .paragraph, spans: spans)
    }

    private func item(_ marker: String, level: Int = 0, _ spans: MarkupSpan...) -> MarkupBlock {
        MarkupBlock(kind: .item(marker: marker, level: level), spans: spans)
    }

    // MARK: - Inline styling

    func testPlainTextIsOneParagraphAsWritten() {
        let text = "Your audio stays on the iPhone, so nothing you say leaves the device."
        XCTAssertEqual(AnswerMarkup.blocks(text), [paragraph(plain(text))])
        XCTAssertEqual(AnswerMarkup.plainText(text), text)
        XCTAssertEqual(AnswerMarkup.blocks(""), [])
        XCTAssertEqual(AnswerMarkup.blocks("  \n \n"), [])
        XCTAssertEqual(AnswerMarkup.plainText(""), "")
    }

    func testBoldItalicAndCode() {
        XCTAssertEqual(
            AnswerMarkup.inline("**Bold** and *italic* and `code`."),
            [bold("Bold"), plain(" and "), italic("italic"), plain(" and "), code("code"), plain(".")]
        )
        // Underscores do the same between words.
        XCTAssertEqual(
            AnswerMarkup.inline("__Bold__ and _italic_."),
            [bold("Bold"), plain(" and "), italic("italic"), plain(".")]
        )
        // Three markers are bold and italic at once.
        XCTAssertEqual(
            AnswerMarkup.inline("***Both***"),
            [MarkupSpan(text: "Both", isBold: true, isItalic: true)]
        )
        XCTAssertEqual(AnswerMarkup.plainText("**Bold** and *italic* and `code`."), "Bold and italic and code.")
    }

    func testItalicInsideBoldAndBoldInsideItalic() {
        XCTAssertEqual(
            AnswerMarkup.inline("**bold *nested* bold**"),
            [bold("bold "), MarkupSpan(text: "nested", isBold: true, isItalic: true), bold(" bold")]
        )
        XCTAssertEqual(
            AnswerMarkup.inline("*italic **nested** italic*"),
            [italic("italic "), MarkupSpan(text: "nested", isBold: true, isItalic: true), italic(" italic")]
        )
        // Three closing markers end both.
        XCTAssertEqual(
            AnswerMarkup.inline("**bold *nested***"),
            [bold("bold "), MarkupSpan(text: "nested", isBold: true, isItalic: true)]
        )
    }

    func testCodeKeepsWhatIsInsideItAsWritten() {
        XCTAssertEqual(
            AnswerMarkup.inline("Run `a * b ** c_d` now"),
            [plain("Run "), code("a * b ** c_d"), plain(" now")]
        )
        // A backtick that is not closed, and two of them, stay as written.
        XCTAssertEqual(AnswerMarkup.inline("a ` b"), [plain("a ` b")])
        XCTAssertEqual(AnswerMarkup.inline("a `` b"), [plain("a `` b")])
    }

    func testWhatIsNotFormattingStaysAsWritten() {
        for text in [
            "2 * 3 * 4 = 24",
            "snake_case_name and file_name.txt",
            "a ** b",
            "5 * 4",
            "****",
            "* * *",
            "---",
            "Price: $5 _ $6",
            "Use # for comments",
            "1.5 litres",
            "> quoted",
            "| a | b |",
            "Tom & Jerry <tag>"
        ] {
            XCTAssertEqual(AnswerMarkup.blocks(text), [paragraph(plain(text))], text)
            XCTAssertEqual(AnswerMarkup.plainText(text), text, text)
        }
        // A backslash before a symbol shows the symbol.
        XCTAssertEqual(AnswerMarkup.inline(#"\*not italic\* and \_this\_"#), [plain("*not italic* and _this_")])
        XCTAssertEqual(AnswerMarkup.inline(#"C:\path"#), [plain(#"C:\path"#)])
    }

    // MARK: - Streaming

    func testAMarkerThatIsNotClosedYetStaysAsWritten() {
        // The end of a partial answer, one character at a time.
        XCTAssertEqual(AnswerMarkup.inline("The answer is **forty"), [plain("The answer is **forty")])
        XCTAssertEqual(AnswerMarkup.inline("The answer is **forty-two*"), [plain("The answer is **forty-two*")])
        XCTAssertEqual(AnswerMarkup.inline("The answer is **forty-two**"), [plain("The answer is "), bold("forty-two")])
        XCTAssertEqual(AnswerMarkup.inline("an *open"), [plain("an *open")])
        XCTAssertEqual(AnswerMarkup.inline("an `open"), [plain("an `open")])
        XCTAssertEqual(AnswerMarkup.inline("a [link](https://pc.example.test/x"), [plain("a [link](https://pc.example.test/x")])
        XCTAssertEqual(AnswerMarkup.inline("a [link"), [plain("a [link")])
        // What was complete before the open marker keeps its styling.
        XCTAssertEqual(
            AnswerMarkup.inline("**Done.** Next is **not"),
            [bold("Done."), plain(" Next is **not")]
        )
    }

    func testEveryPrefixOfAnAnswerCanBeRead() {
        let answer = "## Plan\n\n1. **Move** the *dashboard*.\n2. Keep `March 10`.\n\n- See [notes](https://pc.example.test/n)\n\n```\nlet a = 1\n```\nDone."
        var prefix = ""
        for character in answer {
            prefix.append(character)
            let blocks = AnswerMarkup.blocks(prefix)
            // Nothing is lost on the way: the letters of the answer are all there.
            let letters = blocks.map(\.plainText).joined().filter(\.isLetter)
            XCTAssertTrue(letters.hasPrefix("Plan") || "Plan".hasPrefix(letters), prefix)
        }
        XCTAssertEqual(AnswerMarkup.blocks(answer).count, 6)
    }

    // MARK: - Blocks

    func testHeadingsAreTheirTextAsABoldLine() {
        XCTAssertEqual(
            AnswerMarkup.blocks("# Title\n## Second level ##\n###### Deep"),
            [
                MarkupBlock(kind: .heading, spans: [plain("Title")]),
                MarkupBlock(kind: .heading, spans: [plain("Second level")]),
                MarkupBlock(kind: .heading, spans: [plain("Deep")])
            ]
        )
        // Seven marks, and a mark without a space, are not headings.
        XCTAssertEqual(AnswerMarkup.blocks("####### Seven"), [paragraph(plain("####### Seven"))])
        XCTAssertEqual(AnswerMarkup.blocks("#hashtag"), [paragraph(plain("#hashtag"))])

        // Drawn bold: the whole line carries the intent, and no link.
        let heading = MarkupRendering.attributed(MarkupBlock(kind: .heading, spans: [plain("Title")]))
        XCTAssertEqual(String(heading.characters), "Title")
        for run in heading.runs {
            XCTAssertEqual(run.inlinePresentationIntent, InlinePresentationIntent.stronglyEmphasized)
        }
    }

    func testBulletAndNumberedLists() {
        XCTAssertEqual(
            AnswerMarkup.blocks("- one\n* two\n+ three\n• four"),
            [item("•", plain("one")), item("•", plain("two")), item("•", plain("three")), item("•", plain("four"))]
        )
        XCTAssertEqual(
            AnswerMarkup.blocks("1. First\n2) Second\n10. Tenth"),
            [item("1.", plain("First")), item("2.", plain("Second")), item("10.", plain("Tenth"))]
        )
        // Indented items are a level deeper, up to three levels.
        XCTAssertEqual(
            AnswerMarkup.blocks("- top\n  - inner\n    - deeper\n          - deepest"),
            [
                item("•", plain("top")),
                item("•", level: 1, plain("inner")),
                item("•", level: 2, plain("deeper")),
                item("•", level: 3, plain("deepest"))
            ]
        )
        // An indented line under an item goes on with it; a line that is not indented is a paragraph.
        XCTAssertEqual(
            AnswerMarkup.blocks("- first line\n  second line\nAfter the list"),
            [item("•", plain("first line second line")), paragraph(plain("After the list"))]
        )
    }

    func testBoldInsideABullet() {
        XCTAssertEqual(
            AnswerMarkup.blocks("- **Risk:** onboarding needs *one* more week\n1. Use `git log`"),
            [
                item("•", bold("Risk:"), plain(" onboarding needs "), italic("one"), plain(" more week")),
                item("1.", plain("Use "), code("git log"))
            ]
        )
    }

    func testParagraphsAndLineBreaks() {
        XCTAssertEqual(
            AnswerMarkup.blocks("Line one\nLine two\n\n\nNext paragraph\r\nwith a Windows line break"),
            [paragraph(plain("Line one\nLine two")), paragraph(plain("Next paragraph\nwith a Windows line break"))]
        )
        // Formatting runs across the lines of one paragraph.
        XCTAssertEqual(
            AnswerMarkup.blocks("**bold over\ntwo lines**"),
            [paragraph(bold("bold over\ntwo lines"))]
        )
    }

    func testCodeFences() {
        XCTAssertEqual(
            AnswerMarkup.blocks("Before\n```swift\nlet a = **1**\n  indented\n```\nAfter"),
            [
                paragraph(plain("Before")),
                MarkupBlock(kind: .code, spans: [code("let a = **1**\n  indented")]),
                paragraph(plain("After"))
            ]
        )
        // A fence that is not closed yet: what follows it is code.
        XCTAssertEqual(
            AnswerMarkup.blocks("```\nstill arriving"),
            [MarkupBlock(kind: .code, spans: [code("still arriving")])]
        )
        XCTAssertEqual(AnswerMarkup.blocks("```"), [])
    }

    // MARK: - Links and images

    func testALinkIsItsTextThenItsAddressAndNeverActive() {
        XCTAssertEqual(
            AnswerMarkup.inline("See [the notes](https://pc.example.test/n) first."),
            [plain("See the notes (https://pc.example.test/n) first.")]
        )
        // A title is dropped; a label that is the address is shown once; angle brackets are removed.
        XCTAssertEqual(
            AnswerMarkup.inline(#"[notes](https://pc.example.test/n "A title")"#),
            [plain("notes (https://pc.example.test/n)")]
        )
        XCTAssertEqual(
            AnswerMarkup.inline("[https://pc.example.test/n](https://pc.example.test/n)"),
            [plain("https://pc.example.test/n")]
        )
        XCTAssertEqual(AnswerMarkup.inline("<https://pc.example.test/n>"), [plain("https://pc.example.test/n")])
        // A bare address stays as written.
        XCTAssertEqual(AnswerMarkup.inline("Go to https://pc.example.test/y now"), [plain("Go to https://pc.example.test/y now")])
        // Inside bold the link is still only text.
        XCTAssertEqual(
            AnswerMarkup.inline("**[label](https://pc.example.test/x)**"),
            [bold("label (https://pc.example.test/x)")]
        )
        // Brackets that are no link stay as written.
        XCTAssertEqual(AnswerMarkup.inline("[1] and [x] (see below)"), [plain("[1] and [x] (see below)")])
    }

    func testAnImageIsItsDescriptionThenItsAddress() {
        XCTAssertEqual(
            AnswerMarkup.inline("![a chart](https://pc.example.test/c.png)"),
            [plain("a chart (https://pc.example.test/c.png)")]
        )
        XCTAssertEqual(AnswerMarkup.inline("![](https://pc.example.test/c.png)"), [plain("https://pc.example.test/c.png")])
        XCTAssertEqual(AnswerMarkup.inline("Wow! [not an image]"), [plain("Wow! [not an image]")])
    }

    func testNoStyledTextEverCarriesALink() {
        let hostile = """
        # [Title](https://pc.example.test/a)
        - **[item](https://pc.example.test/b)** and ![image](https://pc.example.test/c.png)
        1. <https://pc.example.test/d> and https://pc.example.test/e
        `[code](https://pc.example.test/f)`
        """
        let blocks = AnswerMarkup.blocks(hostile)
        XCTAssertEqual(blocks.count, 4)
        for block in blocks {
            let styled = MarkupRendering.attributed(block)
            for run in styled.runs {
                XCTAssertNil(run.link, block.plainText)
                XCTAssertNil(run.imageURL, block.plainText)
            }
            // The address is there to be read.
            XCTAssertTrue(String(styled.characters).contains("https://pc.example.test/"), block.plainText)
        }
    }

    // MARK: - Styled text

    func testSpansBecomePresentationIntents() {
        let block = paragraph(plain("a "), bold("b"), italic("c"), code("d"), MarkupSpan(text: "e", isBold: true, isItalic: true))
        let styled = MarkupRendering.attributed(block)
        XCTAssertEqual(String(styled.characters), "a bcde")

        var intents: [String: InlinePresentationIntent] = [:]
        for run in styled.runs {
            let none = InlinePresentationIntent()
            intents[String(styled[run.range].characters)] = run.inlinePresentationIntent ?? none
        }
        let bothStyles = InlinePresentationIntent.stronglyEmphasized.union(InlinePresentationIntent.emphasized)
        XCTAssertEqual(intents.count, 5)
        XCTAssertEqual(intents["a "], InlinePresentationIntent())
        XCTAssertEqual(intents["b"], InlinePresentationIntent.stronglyEmphasized)
        XCTAssertEqual(intents["c"], InlinePresentationIntent.emphasized)
        XCTAssertEqual(intents["d"], InlinePresentationIntent.code)
        XCTAssertEqual(intents["e"], bothStyles)
    }

    // MARK: - Copying

    func testCopyingAnAnswerGivesPlainTextWithoutTheSymbols() {
        let answer = "## Plan\n\n**Yes.** Move the *dashboard*:\n\n- `analytics`\n  - [notes](https://pc.example.test/n)\n- banner\n\n1. Confirm\n2. Ship\n\nDone."
        XCTAssertEqual(
            AnswerMarkup.plainText(answer),
            "Plan\n\nYes. Move the dashboard:\n\n• analytics\n  • notes (https://pc.example.test/n)\n• banner\n1. Confirm\n2. Ship\n\nDone."
        )
    }

    // MARK: - Input meant to make it slow

    func testThousandsOfMarkersAreReadOnceAndStayAsWritten() {
        let stars = String(repeating: "*", count: 20_000)
        XCTAssertEqual(AnswerMarkup.plainText(stars), stars)

        let cases: [String] = [
            String(repeating: "**a ", count: 5_000),
            String(repeating: "*a _b ", count: 5_000),
            String(repeating: "[", count: 20_000),
            String(repeating: "[a](", count: 2_000),
            String(repeating: "`", count: 20_001),
            String(repeating: "` ", count: 10_000),
            String(repeating: "<", count: 20_000),
            String(repeating: "_", count: 20_000),
            String(repeating: "\\", count: 20_000),
            String(repeating: "- ", count: 10_000),
            String(repeating: "#", count: 20_000),
            String(repeating: "***a*** ", count: 5_000),
            String(repeating: "\n- **x**", count: 3_000)
        ]
        let began = Date()
        for text in cases {
            let blocks = AnswerMarkup.blocks(text)
            XCTAssertFalse(blocks.isEmpty, String(text.prefix(12)))
            // Whatever it made of it, no letter of the input is lost.
            XCTAssertEqual(
                blocks.map(\.plainText).joined().filter(\.isLetter).count,
                text.filter(\.isLetter).count,
                String(text.prefix(12))
            )
        }
        XCTAssertLessThan(Date().timeIntervalSince(began), 30, "The formatter reads its input once.")

        // Markers that are opened and never closed stay as written.
        let open = String(repeating: "**a ", count: 5_000)
        XCTAssertEqual(AnswerMarkup.plainText(open), open.trimmingCharacters(in: .whitespaces))
    }
}
