import LiveCueCore
import SwiftUI
import UIKit
import XCTest
@testable import LiveCueRemake

/// ST-26: a text a model wrote never becomes a link on the phone.
///
/// Each card is hosted in a `UIHostingController` with a text that holds a Markdown link and a bare
/// address. Two things are checked. The card is taller with that text than with the text Markdown would
/// leave of it, so the brackets and the address are drawn. And no accessibility element of the hosted
/// card is a link; where the simulator builds the accessibility tree for a hosted view, one element
/// carries the text as it was written.
final class AnswerPlainTextTests: XCTestCase {
    /// A Markdown link and a bare address, on a reserved test domain.
    private let hostile = "[label](https://pc.example.test/x) https://pc.example.test/y"
    /// What would be left of `hostile` if it were read as Markdown.
    private let asMarkdownWouldShowIt = "label https://pc.example.test/y"
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
    func testTheAnswerOfACardIsPlainText() async {
        for isLatest in [true, false] {
            let card: (String) -> AnswerCard = { text in
                AnswerCard(turn: self.turn(answer: text), isLatest: isLatest, canRetry: true, onRetry: {}, onCopy: {})
            }
            assertDrawnAsWritten("the answer, latest \(isLatest)") { text in card(text) }
        }
    }

    @MainActor
    func testTheDetectedQuestionOfACardIsPlainText() async {
        assertNoLink("the detected question") {
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
    func testTheDetailsOfACardArePlainText() async {
        assertDrawnAsWritten("the details") { text in AnswerDetailsText(details: text) }

        // The card with details that are still closed shows no link either.
        assertNoLink("a card with details") {
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
    func testStreamedTextIsPlainText() async {
        assertDrawnAsWritten("text that is streaming in") { text in
            InProgressCard(state: InProgressState(label: "Replying…", text: text, showsSpinner: true, isIncomplete: false))
        }
        assertDrawnAsWritten("text a failed Assist left behind") { text in
            InProgressCard(state: InProgressState(label: "Incomplete answer", text: text, showsSpinner: false, isIncomplete: true))
        }
    }

    @MainActor
    func testTheErrorMessageIsPlainText() async {
        assertDrawnAsWritten("the error message") { text in
            AssistErrorCard(message: text, canRetry: true, onRetry: {})
        }
    }

    // MARK: - Checks

    /// The view drawn with the hostile text is taller than with what Markdown would leave of it, and it
    /// has no link.
    @MainActor
    private func assertDrawnAsWritten<Content: View>(
        _ what: String,
        file: StaticString = #filePath,
        line: UInt = #line,
        _ content: @escaping (String) -> Content
    ) {
        let written = height(of: content(repeated(hostile)))
        let stripped = height(of: content(repeated(asMarkdownWouldShowIt)))
        XCTAssertGreaterThan(
            written,
            stripped,
            "\(what): the brackets and the address of the link are not drawn, so the text was read as Markdown.",
            file: file,
            line: line
        )
        assertNoLink(what, file: file, line: line) { content(self.hostile) }
    }

    /// No accessibility element of the hosted view is a link. Where the accessibility tree of a hosted
    /// view is available, one element carries the hostile text as it was written.
    @MainActor
    private func assertNoLink<Content: View>(
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
        let links = elements.filter { $0.accessibilityTraits.contains(.link) }
        XCTAssertTrue(links.isEmpty, "\(what): an element is a link.", file: file, line: line)

        let labels = elements.compactMap { $0.accessibilityLabel }.filter { !$0.isEmpty }
        if labels.contains(where: { $0.contains("pc.example.test") }) {
            XCTAssertTrue(
                labels.contains { $0.contains(hostile) },
                "\(what): the text is not read as it was written. Labels: \(labels)",
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
