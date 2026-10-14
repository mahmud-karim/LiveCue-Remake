import Foundation

// On-device engines sit behind protocols, so the transcribers are unit-tested with fakes and a speech
// package is imported only by the files that wrap one of its recognisers.

struct RecognizedWord: Equatable, Sendable {
    let text: String
    let start: TimeInterval
    let end: TimeInterval
}

struct BatchRecognition: Equatable, Sendable {
    let words: [RecognizedWord]
    let duration: TimeInterval
}

/// Voz.
protocol BatchRecognizing: Sendable {
    /// 16 kHz mono.
    func transcribe(samples: [Float]) async throws -> BatchRecognition
}

/// Parakeet, and Fluid Speech Mini through `RollingWindowRecognizer`.
protocol StreamingRecognizing: Sendable {
    func reset() async
    /// The handler is called off the main actor.
    func setPartialHandler(_ handler: @escaping @Sendable (String) -> Void) async
    /// 16 kHz mono; text arrives through the handler. False when the samples were only kept for a
    /// later pass: the time the call took then says nothing about the recogniser.
    @discardableResult
    func process(samples: [Float]) async throws -> Bool
    /// Flushes and returns the final text.
    func finish() async throws -> String
    func cleanup() async
}

struct WindowRecognition: Equatable, Sendable {
    let text: String
    /// Times from the start of the samples. Empty when the recogniser gave none.
    let words: [RecognizedWord]
}

/// Fluid Speech Mini: a recogniser that takes one whole window at a time and keeps nothing between calls.
protocol WindowRecognizing: Sendable {
    /// The shortest window it takes, in samples.
    var minimumSamples: Int { get }
    /// 16 kHz mono, at most `RollingWindowRecognizer.windowSamples`.
    func transcribe(samples: [Float]) async throws -> WindowRecognition
    func cleanup() async
}

struct ModelPreparationProgress: Equatable, Sendable {
    enum Phase: Equatable, Sendable {
        case checking, downloading, preparing
    }

    enum Counter: Equatable, Sendable {
        case none
        case bytes(completed: Int64, total: Int64)
        case files(completed: Int, total: Int)
    }

    /// Clamped to 0...1.
    let fraction: Double
    let phase: Phase
    let counter: Counter

    init(fraction: Double, phase: Phase, counter: Counter) {
        self.fraction = fraction.isNaN ? 0 : min(max(fraction, 0), 1)
        self.phase = phase
        self.counter = counter
    }
}

protocol OnDeviceEngineLoading: Sendable {
    /// Downloads when needed, then loads. `progress` is called off the main actor.
    func loadVoz(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any BatchRecognizing
    /// Downloads when needed, then loads. `progress` is called off the main actor.
    func loadParakeet(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing
    /// Downloads when needed, then loads. `progress` is called off the main actor.
    func loadFluid(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing
}
