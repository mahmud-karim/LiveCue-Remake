import LiveCueCore
import SwiftUI
import UIKit
import XCTest
@testable import LiveCueRemake

/// ST-26: a text that came from the relay never becomes a link on the phone.
///
/// The PC status message of the PC speech model screen, the Saved PC message, and a line that shows the
/// relay's own text for a code this build does not know are each hosted with a text that holds a
/// Markdown link and a bare address. Two things are checked. The line is taller with that text than with
/// what Markdown would leave of it, so the brackets and the address are drawn. And no accessibility
/// element of the hosted line is a link; where the simulator builds the accessibility tree of a hosted
/// view, one element carries the text as it was written.
final class RelayTextPlainTextTests: XCTestCase {
    /// A Markdown link and a bare address, on a reserved test domain.
    private let hostile = "[label](https://pc.example.test/x) https://pc.example.test/y"
    /// What would be left of `hostile` if it were read as Markdown.
    private let asMarkdownWouldShowIt = "label https://pc.example.test/y"
    private let width: CGFloat = 320

    /// The text several times over, so that it needs more lines than the short one at any font.
    private func repeated(_ text: String) -> String {
        Array(repeating: text, count: 4).joined(separator: " ")
    }

    // MARK: - The lines

    @MainActor
    func testThePCModelMessageIsPlainText() async {
        assertDrawnAsWritten("the PC model message") { text in
            PCModelMessageLine(text: text)
        }
    }

    @MainActor
    func testTheSavedPCMessageIsPlainText() async {
        assertDrawnAsWritten("the Saved PC message") { text in
            SavedPCMessageLine(text: text)
        }
    }

    /// A start command that the relay refuses with a code this build does not know: the screen shows the
    /// relay's own text, and shows it as it was written.
    @MainActor
    func testTheRelayTextOfAnUnknownCodeIsPlainTextOnThePCModelScreen() async {
        let owners = TestOwners(provider: .nemotron)
        let model = PCSpeechModelViewModel(speech: owners.speech, relay: owners.relay)
        let refusal = RelayFailure(kind: .wire("A_CODE_FROM_A_LATER_RELAY"), origin: .http, httpStatus: 409, relayText: hostile)
        await owners.gateway.setStart(.failure(refusal))

        await model.start()

        XCTAssertEqual(model.message, hostile, "The relay's own text is what the screen shows.")
        assertNoLink("the relay text of an unknown code", expecting: hostile) {
            PCModelMessageLine(text: model.message)
        }
    }

    /// A health check that the relay refuses with a code this build does not know: the Saved PC panel
    /// shows the relay's own text, and shows it as it was written.
    @MainActor
    func testTheRelayTextOfAnUnknownCodeIsPlainTextOnTheSavedPCPanel() async {
        let owners = TestOwners()
        let model = PairingViewModel(pairing: owners.pairing, relay: owners.relay, app: owners.app)
        let refusal = RelayFailure(kind: .wire("A_CODE_FROM_A_LATER_RELAY"), origin: .http, httpStatus: 409, relayText: hostile)
        await owners.gateway.setHealth(.failure(refusal))

        await model.reconnect()

        XCTAssertEqual(model.savedMessage, hostile)
        assertNoLink("the Saved PC panel", expecting: hostile) {
            SavedPCPanel()
                .environment(model)
        }
    }

    // MARK: - Pictures

    /// The overlay of the QR sheet over a dark ground. The simulator has no camera, so this is the only
    /// place the viewfinder is seen before a device shows it.
    @MainActor
    func testTheScannerOverlayIsDrawn() async {
        let report = ScreensBHost.show(
            ZStack {
                Color.black.ignoresSafeArea()
                QRScanOverlay(didRead: false, onCancel: {})
            },
            pictureNamed: "wp11-qr-overlay",
            in: self
        )

        if !report.labels.isEmpty {
            XCTAssertTrue(report.labels.contains("Scan PC QR"), "Labels: \(report.labels)")
            XCTAssertTrue(report.labels.contains("Point at the QR in LiveCue Desktop"), "Labels: \(report.labels)")
        }
    }

    // MARK: - Checks

    /// The line drawn with the hostile text is taller than with what Markdown would leave of it, and it
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
        assertNoLink(what, expecting: hostile, file: file, line: line) { content(self.hostile) }
    }

    /// No accessibility element of the hosted view is a link. Where the accessibility tree of a hosted
    /// view is available, one element carries `text` as it was written.
    @MainActor
    private func assertNoLink<Content: View>(
        _ what: String,
        expecting text: String,
        file: StaticString = #filePath,
        line: UInt = #line,
        _ content: () -> Content
    ) {
        let report = ScreensBHost.show(
            content().frame(width: width),
            size: CGSize(width: width, height: 700),
            pictureNamed: nil,
            in: self
        )
        XCTAssertEqual(report.linkCount, 0, "\(what): an element is a link.", file: file, line: line)
        if report.labels.contains(where: { $0.contains("pc.example.test") }) {
            XCTAssertTrue(
                report.labels.contains { $0.contains(text) },
                "\(what): the text is not read as it was written. Labels: \(report.labels)",
                file: file,
                line: line
            )
        }
    }

    /// The height the view asks for at the test width.
    @MainActor
    private func height<Content: View>(of content: Content) -> CGFloat {
        let host = UIHostingController(rootView: content)
        return host.sizeThatFits(in: CGSize(width: width, height: .greatestFiniteMagnitude)).height
    }
}

// MARK: - Hosting a view of this package in the test host

/// What a hosted view exposed to assistive technology.
struct ScreensBHostReport {
    /// Every non-empty accessibility label, in tree order.
    let labels: [String]
    let identifiers: Set<String>
    /// How many elements carry the link trait.
    let linkCount: Int
}

/// Shows a view of Screens B in a window of the app that hosts the unit tests, reads its accessibility
/// tree, and can keep a picture of it with the test results (CI exports the pictures of a full run).
@MainActor enum ScreensBHost {
    /// The default size is an iPhone in points: the size of a picture.
    static func show<Content: View>(
        _ content: Content,
        size: CGSize = CGSize(width: 393, height: 852),
        pictureNamed name: String?,
        in testCase: XCTestCase
    ) -> ScreensBHostReport {
        let host = UIHostingController(rootView: content)
        let window = makeWindow(size: size)
        window.rootViewController = host
        window.isHidden = false
        host.view.frame = window.bounds
        host.view.layoutIfNeeded()
        // Lists and navigation bars settle over a few turns of the run loop.
        RunLoop.main.run(until: Date().addingTimeInterval(name == nil ? 0.05 : 0.6))
        host.view.layoutIfNeeded()

        var elements: [NSObject] = []
        collect(host.view, into: &elements, depth: 0)
        let labels = elements.compactMap { $0.accessibilityLabel }.filter { !$0.isEmpty }
        let identifiers = elements.compactMap { ($0 as? any UIAccessibilityIdentification)?.accessibilityIdentifier }
        let links = elements.filter { $0.accessibilityTraits.contains(.link) }

        if let name {
            let renderer = UIGraphicsImageRenderer(bounds: window.bounds)
            let image = renderer.image { _ in
                window.drawHierarchy(in: window.bounds, afterScreenUpdates: true)
            }
            let attachment = XCTAttachment(image: image)
            attachment.name = name
            attachment.lifetime = .keepAlways
            testCase.add(attachment)
        }

        window.isHidden = true
        window.rootViewController = nil
        return ScreensBHostReport(
            labels: labels,
            identifiers: Set(identifiers.filter { !$0.isEmpty }),
            linkCount: links.count
        )
    }

    /// A window in the scene of the app that hosts the tests, or a window of its own when there is none.
    private static func makeWindow(size: CGSize) -> UIWindow {
        let frame = CGRect(origin: .zero, size: size)
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        guard let scene = scenes.first else {
            return UIWindow(frame: frame)
        }
        let window = UIWindow(windowScene: scene)
        window.frame = frame
        return window
    }

    /// Every accessibility element under `object`, through views and through element containers.
    private static func collect(_ object: NSObject, into found: inout [NSObject], depth: Int) {
        guard depth < 40, found.count < 4_000 else {
            return
        }
        found.append(object)
        if let children = object.accessibilityElements {
            for case let child as NSObject in children {
                collect(child, into: &found, depth: depth + 1)
            }
        }
        if let view = object as? UIView {
            for subview in view.subviews {
                collect(subview, into: &found, depth: depth + 1)
            }
        }
    }
}
