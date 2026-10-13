import SwiftUI

/// The inline card of a failed Assist, with its retry.
///
/// The message is the categorised text of the failure. The banner draws it as it is: nothing in it is
/// read as formatting or as a link.
struct AssistErrorCard: View {
    private let message: String
    private let canRetry: Bool
    private let onRetry: @MainActor () -> Void

    init(message: String, canRetry: Bool, onRetry: @escaping @MainActor () -> Void) {
        self.message = message
        self.canRetry = canRetry
        self.onRetry = onRetry
    }

    var body: some View {
        Card(prominent: true) {
            Banner(tone: .danger, title: "Assist could not finish", message: message)
                .accessibilityIdentifier("assist-error")
            CaptionText("No automatic retry. Any partial answer below is incomplete.")
            LCButton("Retry same text", systemImage: "arrow.clockwise", kind: .secondary, action: onRetry)
                .disabled(!canRetry)
                .accessibilityIdentifier("retry-failed-assist")
        }
    }
}
