import Foundation
import LiveCueCore

/// What one start of a transcriber is given.
struct TranscriberStart: Equatable, Sendable {
    /// Every event of this start carries it.
    let generation: UUID
    /// Added to the segment times of a streaming provider.
    let streamOffsetSeconds: Double
}

struct TranscriberFailure: Error, Equatable, Sendable {
    enum Reason: Equatable, Sendable {
        /// Relay, Meta or send failure.
        case stream
        case uploadOverflow, microphoneInterrupted, routeChanged, noMicrophone
        /// "Prepare Voz first." / "Prepare Parakeet first."
        case notPrepared
        case pendingAudioLimit, processingOverflow, processingFailed
    }

    let reason: Reason
    /// The text to show; never raw system or library text.
    let message: String

    init(_ reason: Reason, message: String) {
        self.reason = reason
        self.message = message
    }
}

struct TranscriberEvent: Sendable {
    /// Exactly the cases of architecture 4.7.
    enum Kind: Sendable {
        case status(String)
        case partial(String)
        /// Times already include the stream offset.
        case segment(TranscriptSegment)
        case level(Float)
        case usage(UsageUpdate)
        case lastChunk(milliseconds: Double)
        /// After a successful start only.
        case failed(TranscriberFailure)
    }

    let generation: UUID
    let kind: Kind
}

/// Five speech providers behind one interface. One object per session; it lives across that session's
/// pauses and resumes.
@MainActor protocol SpeechTranscriber: AnyObject {
    var provider: SpeechProvider { get }
    /// One stream for the life of the object, unbounded. Events are emitted on the main actor.
    var events: AsyncStream<TranscriberEvent> { get }
    /// How many events have been emitted so far. When `start`, `finalize` or `transcribePending` returns
    /// or throws, every event that call caused has been emitted, so a consumer that has handled this many
    /// events has seen them all.
    var emittedEventCount: Int { get }
    /// Returns once audio is flowing. Streaming providers open the microphone only after the provider
    /// handshake. Throws `TranscriberFailure`; after a throw nothing is running.
    func start(_ start: TranscriberStart) async throws
    /// The provider's Pause and Stop rule (spec 01 section 3.1). Throws `TranscriberFailure` when the end
    /// was not clean; capture and connection are down either way. Streaming providers release the audio
    /// session; on-device providers keep it active.
    func finalize() async throws
    /// Voz: transcribe pending audio now and return the seconds it took. Others return 0 at once.
    /// Calls are serialised with `finalize` by the transcriber.
    func transcribePending() async throws -> TimeInterval
    /// Release everything and deactivate the audio session. Idempotent. Emits nothing.
    func shutdown() async
}
