import Foundation
import LiveCueCore

/// Any thrown error to the text of the Assist card.
enum AnswerErrorText {
    /// RelayFailure: RelayMessages.text. OpenRouterError, MetaSpeechError: errorDescription.
    /// TranscriberFailure (Voz transcription inside an Assist): its message.
    /// CancellationError: nil (nothing is shown). Anything else: the L-NETWORK text for Codex, the
    /// `.unavailable` text for OpenRouter.
    static func message(for error: any Error, provider: AssistantProvider) -> String? {
        // STUB(WP-08)
        nil
    }
}
