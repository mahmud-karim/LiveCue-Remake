import Foundation
import LiveCueCore

/// Any thrown error to the text of the Assist card.
enum AnswerErrorText {
    /// RelayFailure: RelayMessages.text. OpenRouterError, MetaSpeechError: errorDescription.
    /// TranscriberFailure (Voz transcription inside an Assist): its message.
    /// CancellationError: nil (nothing is shown). Anything else: the L-NETWORK text for Codex, the
    /// `.unavailable` text for OpenRouter.
    static func message(for error: any Error, provider: AssistantProvider) -> String? {
        if error is CancellationError {
            return nil
        }
        if let failure = error as? RelayFailure {
            return RelayMessages.text(for: failure)
        }
        if let failure = error as? OpenRouterError {
            return failure.errorDescription ?? fallback(for: provider)
        }
        if let failure = error as? MetaSpeechError {
            return failure.errorDescription ?? fallback(for: provider)
        }
        if let failure = error as? TranscriberFailure {
            return failure.message
        }
        return fallback(for: provider)
    }

    /// The text of an error that has none of its own. No system text is ever shown.
    private static func fallback(for provider: AssistantProvider) -> String {
        switch provider {
        case .codex:
            return RelayMessages.text(for: RelayLocalCondition.network)
        case .openrouter:
            return OpenRouterError.unavailable.errorDescription ?? ""
        }
    }
}
