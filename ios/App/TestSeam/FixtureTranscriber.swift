import Foundation
import LiveCueCore

/// The transcriber of the test seam (section 4.9): no microphone, no socket, one fixture segment.
///
/// Every start commits the fixture segment at the stream offset. While started, a loop ticks once a
/// second: a streaming provider's usage stream gains one second of audio, sent and processed, and the
/// level meter gets a value. Pause closes the usage stream as completed; Resume opens a new one. An
/// on-device provider has no usage.
@MainActor final class FixtureTranscriber: SpeechTranscriber {
    let provider: SpeechProvider
    private let channel: TranscriberEventChannel
    private let clock: any AppClock

    /// One start of the transcriber.
    @MainActor private final class Run {
        let generation: UUID
        /// The usage stream of this start.
        let streamID = UUID()
        var audioMilliseconds: Double = 0
        var loop: Task<Void, Never>?

        init(generation: UUID) {
            self.generation = generation
        }
    }

    /// The start in progress; nil while paused and after the end.
    private var run: Run?

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    init(provider: SpeechProvider, clock: any AppClock) {
        self.provider = provider
        self.channel = TranscriberEventChannel()
        self.clock = clock
    }

    func start(_ start: TranscriberStart) async throws {
        // One stream at a time. A start that is still there ends without a word.
        self.run?.loop?.cancel()

        let run = Run(generation: start.generation)
        self.run = run
        channel.emit(.status(TranscriberStatusText.listening), generation: run.generation)
        if provider.streamsAudio {
            channel.emit(.usage(.begin(id: run.streamID)), generation: run.generation)
        }
        let segment = TranscriptSegment(
            text: FixtureValues.segmentText,
            startSeconds: start.streamOffsetSeconds,
            endSeconds: start.streamOffsetSeconds + FixtureTranscriber.segmentSeconds
        )
        channel.emit(.segment(segment), generation: run.generation)

        run.loop = Task { await self.tick(run) }
    }

    func finalize() async throws {
        guard let run else { return }
        self.run = nil
        run.loop?.cancel()
        if provider.streamsAudio {
            channel.emit(.usage(.finish(id: run.streamID, completed: true)), generation: run.generation)
        }
        channel.emit(.status(TranscriberStatusText.paused), generation: run.generation)
    }

    func transcribePending() async throws -> TimeInterval {
        0
    }

    func shutdown() async {
        run?.loop?.cancel()
        run = nil
    }

    /// Once a second while `run` is the start in progress.
    private func tick(_ run: Run) async {
        while true {
            do {
                try await clock.sleep(seconds: FixtureTranscriber.tickSeconds)
            } catch {
                return
            }
            guard self.run === run else { return }

            if provider.streamsAudio {
                run.audioMilliseconds += FixtureTranscriber.tickSeconds * 1_000
                let update = UsageUpdate.update(
                    id: run.streamID,
                    sentMs: run.audioMilliseconds,
                    processedMs: run.audioMilliseconds,
                    hasTranscript: true
                )
                channel.emit(.usage(update), generation: run.generation)
            }
            channel.emit(.level(FixtureTranscriber.level), generation: run.generation)
        }
    }

    static let segmentSeconds: Double = 3
    static let tickSeconds: TimeInterval = 1
    static let level: Float = 0.4
}
