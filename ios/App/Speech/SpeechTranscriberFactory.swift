import Foundation
import LiveCueCore

@MainActor protocol SpeechTranscriberFactory: AnyObject {
    /// A new transcriber for one session. It lives across that session's pauses and resumes.
    func makeTranscriber(for provider: SpeechProvider) -> any SpeechTranscriber
}
