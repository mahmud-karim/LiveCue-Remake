import Foundation
import LiveCueCore

@MainActor final class FixtureTranscriberFactory: SpeechTranscriberFactory {
    private let clock: any AppClock

    init(clock: any AppClock) {
        self.clock = clock
    }

    func makeTranscriber(for provider: SpeechProvider) -> any SpeechTranscriber {
        // STUB(WP-07)
        FixtureTranscriber(provider: provider, clock: clock)
    }
}
