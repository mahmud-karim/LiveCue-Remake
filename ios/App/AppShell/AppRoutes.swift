import Foundation

enum AppTab: Hashable, Sendable {
    case live, history, settings
}

/// A screen that can be pushed onto a tab's stack or onto the live session's stack.
enum Route: Hashable, Sendable {
    case transcription, pairing, assistantLab, modelLibrary, benchmark, metaKey, pcSpeechModel
    case openRouterModels, reasoning
    case runDetails(turnID: UUID)
    case sessionDetail(sessionID: UUID)
}
