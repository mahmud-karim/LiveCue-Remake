import UIKit

protocol ClipboardWriting: Sendable {
    @MainActor func copy(_ text: String)
}

/// The general pasteboard.
struct SystemClipboard: ClipboardWriting {
    init() {}

    @MainActor func copy(_ text: String) {
        UIPasteboard.general.string = text
    }
}
