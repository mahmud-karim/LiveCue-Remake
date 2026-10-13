import LiveCueCore
import SwiftUI
import UIKit
import XCTest
@testable import LiveCueRemake

/// ST-26: a text a model wrote never becomes a link or an image on the phone.
///
/// Since 1.1.0 the answer, the details and the streamed text show their formatting (`MarkupText`), so
/// "drawn as written" is no longer the rule for them. What is checked instead, for each card hosted in a
/// `UIHostingController` with a text that holds a Markdown link and a bare address: the card is taller
/// with that text than with the text a Markdown renderer would leave of it, so the address of the link
/// is drawn as text; no accessibility element of the hosted card is a link; and where the simulator
/// builds the accessibility tree for a hosted view, one element carries the text with the address in
/// it. The detected question and the error message are still drawn exactly as written.
final class AnswerPlainTextTests: XCTestCase {
    /// A Markdown link and a bare address, on a reserved test domain.
    private let hostile = "[label](https://pc.example.test/x) https://pc.example.test/y"
    /// What would be left of `hostile` if it were read as Markdown.
    private let asMarkdownWouldShowIt = "label https://pc.example.test/y"
    /// What the formatter shows for `hostile`: the label, then the address in brackets, as plain text.
    private let asTheFormatterShowsIt = "label (https://pc.example.test/x) https://pc.example.test/y"
    private let hostileImage = "![a picture](https://pc.example.test/p.png)"
    private let width: CGFloat = 320

    /// The hostile text several times over, so that it needs more lines than the short one at any font.
    private func repeated(_ text: String) -> String {
        Array(repeating: text, count: 4).joined(separator: " ")
    }

    private func turn(answer: String, details: String = "", question: String = "A plain question") -> AssistantTurn {
        AssistantTurn(detectedQuestion: question, answer: answer, details: details)
    }

    // MARK: - The cards

    @MainActor
    func testTheAnswerOfACardHasNoActiveLink() async {
        XCTAssertEqual(AnswerMarkup.plainText(hostile), asTheFormatterShowsIt)
        for isLatest in [true, false] {
            let card: (String) -> AnswerCard = { text in
                AnswerCard(turn: self.turn(answer: text), isLatest: isLatest, canRetry: true, onRetry: {}, onCopy: {})
            }
            assertAddressIsDrawn("the answer, latest \(isLatest)", shown: asTheFormatterShowsIt) { text in card(text) }
        }
    }

    /// An answer with every kind of formatting, and a link and an image inside it, has no link and no
    /// image either.
    @MainActor
    func testAFormattedAnswerHasNoActiveLinkAndNoImage() async {
        let formatted = "## [Title](https://pc.example.test/a)\n\n**Bold [x](https://pc.example.test/b)** and *italic* and `code`.\n\n- one \(hostileImage)\n1. two <https://pc.example.test/c>"
        assertNoLink("a formatted answer", shown: "Title (https://pc.example.test/a)") {
            AnswerCard(turn: self.turn(answer: formatted), isLatest: true, canRetry: true, onRetry: {}, onCopy: {})
        }
        assertNoImage("the details") {
            AnswerDetailsText(details: self.hostileImage)
        }
        assertNoLink("an image in the details", shown: "a picture (https://pc.example.test/p.png)") {
            AnswerDetailsText(details: self.hostileImage)
        }
    }

    @MainActor
    func testTheDetectedQuestionOfACardIsPlainText() async {
        assertNoLink("the detected question", shown: hostile) {
            AnswerCard(
                turn: self.turn(answer: "A plain answer.", question: self.hostile),
                isLatest: true,
                canRetry: true,
                onRetry: {},
                onCopy: {}
            )
        }
    }

    @MainActor
    func testTheDetailsOfACardHaveNoActiveLink() async {
        assertAddressIsDrawn("the details", shown: asTheFormatterShowsIt) { text in AnswerDetailsText(details: text) }

        // The card with details that are still closed shows no link either.
        assertNoLink("a card with details", shown: asTheFormatterShowsIt) {
            AnswerCard(
                turn: self.turn(answer: "A plain answer.", details: self.hostile),
                isLatest: true,
                canRetry: true,
                onRetry: {},
                onCopy: {}
            )
        }
    }

    @MainActor
    func testStreamedTextHasNoActiveLink() async {
        assertAddressIsDrawn("text that is streaming in", shown: asTheFormatterShowsIt) { text in
            InProgressCard(state: InProgressState(label: "Replying…", text: text, showsSpinner: true, isIncomplete: false))
        }
        assertAddressIsDrawn("text a failed Assist left behind", shown: asTheFormatterShowsIt) { text in
            InProgressCard(state: InProgressState(label: "Incomplete answer", text: text, showsSpinner: false, isIncomplete: true))
        }
    }

    @MainActor
    func testTheErrorMessageIsPlainText() async {
        assertAddressIsDrawn("the error message", shown: hostile) { text in
            AssistErrorCard(message: text, canRetry: true, onRetry: {})
        }
    }

    // MARK: - Checks

    /// The view drawn with the hostile text is taller than with what a Markdown renderer would leave of
    /// it, so the address of the link is drawn as text; and it has no link. `shown` is the text an
    /// accessibility element carries for the hostile text.
    @MainActor
    private func assertAddressIsDrawn<Content: View>(
        _ what: String,
        shown: String,
        file: StaticString = #filePath,
        line: UInt = #line,
        _ content: @escaping (String) -> Content
    ) {
        let written = height(of: content(repeated(hostile)))
        let stripped = height(of: content(repeated(asMarkdownWouldShowIt)))
        XCTAssertGreaterThan(
            written,
            stripped,
            "\(what): the address of the link is not drawn, so the link was read as Markdown.",
            file: file,
            line: line
        )
        assertNoLink(what, shown: shown, file: file, line: line) { content(self.hostile) }
    }

    /// No accessibility element of the hosted view is an image.
    @MainActor
    private func assertNoImage<Content: View>(
        _ what: String,
        file: StaticString = #filePath,
        line: UInt = #line,
        _ content: () -> Content
    ) {
        let host = UIHostingController(rootView: content().frame(width: width))
        let window = makeWindow()
        window.rootViewController = host
        window.isHidden = false
        host.view.layoutIfNeeded()
        RunLoop.main.run(until: Date().addingTimeInterval(0.05))

        var elements: [NSObject] = []
        collectAccessibilityElements(of: host.view, into: &elements, depth: 0)
        let images = elements.filter { $0.accessibilityTraits.contains(.image) }
        XCTAssertTrue(images.isEmpty, "\(what): an element is an image.", file: file, line: line)
        window.isHidden = true
        window.rootViewController = nil
    }

    /// No accessibility element of the hosted view is a link. Where the accessibility tree of a hosted
    /// view is available, one element carries `shown`, the text with the address in it.
    @MainActor
    private func assertNoLink<Content: View>(
        _ what: String,
        shown: String,
        file: StaticString = #filePath,
        line: UInt = #line,
        _ content: () -> Content
    ) {
        let host = UIHostingController(rootView: content().frame(width: width))
        let window = makeWindow()
        window.rootViewController = host
        window.isHidden = false
        host.view.layoutIfNeeded()
        RunLoop.main.run(until: Date().addingTimeInterval(0.05))

        var elements: [NSObject] = []
        collectAccessibilityElements(of: host.view, into: &elements, depth: 0)
        let links = elements.filter { $0.accessibilityTraits.contains(.link) }
        XCTAssertTrue(links.isEmpty, "\(what): an element is a link.", file: file, line: line)

        let labels = elements.compactMap { $0.accessibilityLabel }.filter { !$0.isEmpty }
        if labels.contains(where: { $0.contains("pc.example.test") }) {
            XCTAssertTrue(
                labels.contains { $0.contains(shown) },
                "\(what): the text is not read with its address. Labels: \(labels)",
                file: file,
                line: line
            )
        }
        window.isHidden = true
        window.rootViewController = nil
    }

    /// A window in the scene of the app that hosts the tests, or a window of its own when there is none.
    @MainActor
    private func makeWindow() -> UIWindow {
        let frame = CGRect(x: 0, y: 0, width: width, height: 900)
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        guard let scene = scenes.first else {
            return UIWindow(frame: frame)
        }
        let window = UIWindow(windowScene: scene)
        window.frame = frame
        return window
    }

    /// The height the view asks for at the test width.
    @MainActor
    private func height<Content: View>(of content: Content) -> CGFloat {
        let host = UIHostingController(rootView: content)
        return host.sizeThatFits(in: CGSize(width: width, height: .greatestFiniteMagnitude)).height
    }

    /// Every accessibility element under `object`, through views and through element containers.
    @MainActor
    private func collectAccessibilityElements(of object: NSObject, into found: inout [NSObject], depth: Int) {
        guard depth < 40, found.count < 2_000 else {
            return
        }
        found.append(object)
        if let children = object.accessibilityElements {
            for case let child as NSObject in children {
                collectAccessibilityElements(of: child, into: &found, depth: depth + 1)
            }
        }
        if let view = object as? UIView {
            for subview in view.subviews {
                collectAccessibilityElements(of: subview, into: &found, depth: depth + 1)
            }
        }
    }
}
