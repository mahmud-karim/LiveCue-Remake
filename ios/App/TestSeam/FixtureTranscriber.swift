import Foundation
import LiveCueCore

/// The transcriber of the test seam (section 4.9): no microphone, no socket, one fixture segment.
@MainActor final class FixtureTranscriber: SpeechTranscriber {
    let provider: SpeechProvider
    private let channel: TranscriberEventChannel

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(provider: SpeechProvider, clock: any AppClock) {
        // STUB(WP-07)
        self.provider = provider
        self.channel = TranscriberEventChannel()
    }

    func start(_ start: TranscriberStart) async throws {
        // STUB(WP-07)
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
