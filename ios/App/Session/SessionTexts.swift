import Foundation
import LiveCueCore

/// The messages the session controller composes.
enum SessionTexts {
    /// Start check 4 (section 4.1.2).
    static let microphoneDenied = "Microphone permission is required for live transcription. If you use LiveContainer, allow it for LiveContainer in Settings."

    /// A finalisation that did not end cleanly: the notice of a Pause, the alert of a Stop.
    static let finalizeFailed = "Could not finalize the transcript. Some final words may be missing."

    /// Step 5 of the Assist pipeline: the transcript and the partial are both empty.
    static let noSpeech = "No speech was recognized yet."

    /// Start check 2. `homeLabel` is `RelayMonitor.homeLabel(for:)`.
    static func pcModelNotReady(homeLabel: String) -> String {
        "PC speech model: \(homeLabel). Open Transcription to start it or check its status."
    }

    /// Step 7 of the Assist pipeline with Codex answers.
    static func selectionNotInCatalogue(_ selection: AssistantConfiguration) -> String {
        "\(selection.model) / \(selection.reasoningEffort) is not in this PC's current catalog. Open Assistant models & timing and choose an available combination. Your selection has not changed."
    }

    /// "Transcription could not start. " + the transcriber's reason. A transcriber throws only
    /// `TranscriberFailure`; anything else has no reason to add.
    static func startFailure(_ error: any Error) -> String {
        guard let failure = error as? TranscriberFailure else {
            return "Transcription could not start."
        }
        return RelayMessages.startFailure(failure.message)
    }

    /// "$0.00000 USD"
    static func cost(_ usage: TranscriptionUsage) -> String {
        usage.formattedCost + " USD"
    }
}
