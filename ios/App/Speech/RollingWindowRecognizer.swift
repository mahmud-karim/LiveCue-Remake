import Foundation
import LiveCueCore

/// Live captions from a recogniser that takes whole windows (D-29), behind `StreamingRecognizing`.
///
/// Audio is kept until its words are committed. Once a second of new audio has come in, everything
/// kept is transcribed again, and `RollingTranscript` says what has become final and how much audio can
/// go. The text after each pass goes to the partial handler. A pass is awaited inside `process`, so a
/// slow recogniser holds the capture's queue back and passes never pile up.
actor RollingWindowRecognizer: StreamingRecognizing {
    static let sampleRate = 16_000
    /// The model's input, 15 seconds. No pass is given more.
    static let windowSamples = 240_000
    /// A pass runs once this much audio has come in since the last one: 1.0 s.
    static let passSamples = 16_000

    private let engine: any WindowRecognizing
    private var transcript = RollingTranscript()
    /// The audio whose words are not all committed yet.
    private var pending: [Float] = []
    /// How many samples came in since the last pass.
    private var unheard = 0
    private var isPassing = false
    /// Counts the recording stretches, so a pass that ends after a reset changes nothing.
    private var stretch = 0
    private var handler: (@Sendable (String) -> Void)?

    init(engine: any WindowRecognizing) {
        self.engine = engine
    }

    // MARK: - StreamingRecognizing

    /// A new recording stretch: no audio and no text. The partial handler stays.
    func reset() async {
        stretch += 1
        transcript.reset()
        pending = []
        unheard = 0
    }

    func setPartialHandler(_ handler: @escaping @Sendable (String) -> Void) async {
        self.handler = handler
    }

    /// Keeps the samples. With a second of new audio, and at least the shortest window the recogniser
    /// takes, everything kept is transcribed and the text for the screen goes to the handler.
    @discardableResult
    func process(samples: [Float]) async throws -> Bool {
        pending.append(contentsOf: samples)
        unheard += samples.count
        guard !isPassing,
              unheard >= RollingWindowRecognizer.passSamples,
              pending.count >= engine.minimumSamples else {
            return false
        }
        if let text = try await pass() {
            handler?(text)
        }
        return true
    }

    /// One last pass over audio no pass has heard yet, then the whole text of the stretch. When that
    /// pass fails, the text up to it is still returned: a recording is not lost over its last second.
    func finish() async throws -> String {
        if unheard > 0, !pending.isEmpty, !isPassing {
            _ = try? await pass()
        }
        let text = transcript.finish()
        transcript.reset()
        pending = []
        unheard = 0
        return text
    }

    func cleanup() async {
        await reset()
        handler = nil
        await engine.cleanup()
    }

    // MARK: - Pieces

    /// Transcribes what is kept and applies the rules. Returns the text for the screen; nil when the
    /// stretch ended while the recogniser worked.
    private func pass() async throws -> String? {
        // More than one window has come together: only the newest 15 seconds are heard, and the audio
        // before them goes.
        if pending.count > RollingWindowRecognizer.windowSamples {
            pending.removeFirst(pending.count - RollingWindowRecognizer.windowSamples)
        }
        var window = pending
        let heard = window.count
        // Only the last pass of a stretch can be this short. Silence fills it up.
        if heard < engine.minimumSamples {
            window.append(contentsOf: [Float](repeating: 0, count: engine.minimumSamples - heard))
        }

        let running = stretch
        unheard = 0
        isPassing = true
        defer { isPassing = false }
        let recognition = try await engine.transcribe(samples: window)
        guard running == stretch else { return nil }

        let outcome = transcript.apply(
            duration: Double(heard) / Double(RollingWindowRecognizer.sampleRate),
            text: recognition.text,
            words: recognition.words.map { RollingTranscript.Word(text: $0.text, start: $0.start, end: $0.end) }
        )
        // Audio that came in during the pass sits behind the window and stays.
        let dropped = Int((outcome.dropSeconds * Double(RollingWindowRecognizer.sampleRate)).rounded())
        pending.removeFirst(min(max(0, dropped), heard, pending.count))
        return outcome.text
    }
}
