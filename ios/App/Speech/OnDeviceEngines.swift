import Foundation

// On-device engines sit behind two protocols, so the transcribers are unit-tested with fakes and each
// speech package is imported by exactly one file.

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

/// Parakeet.
protocol StreamingRecognizing: Sendable {
    func reset() async
    /// The handler is called off the main actor.
    func setPartialHandler(_ handler: @escaping @Sendable (String) -> Void) async
    /// 16 kHz mono; text arrives through the handler.
    func process(samples: [Float]) async throws
    /// Flushes and returns the final text.
    func finish() async throws -> String
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
}
