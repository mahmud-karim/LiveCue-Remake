import Foundation

/// "Is the PC needed" for a pair of selections.
public enum PCRequirement {
    /// True when the speech provider is PC-local or the answer service is Codex.
    public static func needsPC(speech: SpeechProvider, answers: AssistantProvider) -> Bool {
        speech.isPCLocal || answers == .codex
    }
}
