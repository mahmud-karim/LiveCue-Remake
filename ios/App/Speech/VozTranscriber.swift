import Foundation
import LiveCueCore

/// Provider `voz`: records on the phone and transcribes when Assist is tapped.
@MainActor final class VozTranscriber: SpeechTranscriber {
    let provider: SpeechProvider = .voz
    private let channel: TranscriberEventChannel

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(engine: (any BatchRecognizing)?, audio: any AudioCapturing, clock: any AppClock, models: OnDeviceModelManager) {
        // STUB(WP-07)
        channel = TranscriberEventChannel()
    }

    func start(_ start: TranscriberStart) async throws {
        // STUB(WP-07)
        throw TranscriberFailure(.notPrepared, message: "Prepare Voz first.")
    }

    func finalize() async throws {
        // STUB(WP-07)
    }

    func transcribePending() async throws -> TimeInterval {
        // STUB(WP-07)
        0
    }

    func shutdown() async {
        // STUB(WP-07)
    }
}
