import Foundation

enum SavedPCState: Equatable, Sendable {
    case connected, reconnecting, needsAttention
}

/// A message line on the screen that started an action.
struct InlineMessage: Equatable, Sendable {
    let text: String
    let isError: Bool

    init(_ text: String, isError: Bool = false) {
        self.text = text
        self.isError = isError
    }
}
