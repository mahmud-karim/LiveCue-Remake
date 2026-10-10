import Foundation
import LiveCueCore

/// Provider `parakeet`: live captions on the phone.
@MainActor final class ParakeetTranscriber: SpeechTranscriber {
    let provider: SpeechProvider = .parakeet
    private let channel: TranscriberEventChannel

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(engine: (any StreamingRecognizing)?, audio: any AudioCapturing, clock: any AppClock, models: OnDeviceModelManager) {
        // STUB(WP-07)
        channel = TranscriberEventChannel()
    }

    func start(_ start: TranscriberStart) async throws {
        // STUB(WP-07)
        throw TranscriberFailure(.notPrepared, message: "Prepare Parakeet first.")
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
