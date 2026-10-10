import AVFoundation

/// Owns the `AVAudioConverter` and the two continuations of one capture, and builds the tap block off the
/// main actor, so the block that runs on the audio thread is not main-actor isolated.
final class AudioTapConverter: @unchecked Sendable {
    // STUB(WP-07)
    init() {}

    nonisolated static func makeTapBlock(for converter: AudioTapConverter) -> AVAudioNodeTapBlock {
        // STUB(WP-07)
        return { _, _ in }
    }
}
