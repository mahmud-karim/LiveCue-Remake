import Foundation

/// Consumes one transcriber's events in order on the main actor.
@MainActor final class TranscriberEventPump {
    init(transcriber: any SpeechTranscriber, handler: @escaping @MainActor (TranscriberEvent) -> Void) {
        // STUB(WP-08)
    }

    /// Returns when every event emitted so far was handled.
    func waitUntilCaughtUp() async {
        // STUB(WP-08)
    }

    func stop() {
        // STUB(WP-08)
    }
}
