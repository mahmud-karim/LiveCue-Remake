import Foundation

/// The engine loader of the test seam: reports 0.5, waits 2 s, returns a fixture engine. Nothing is
/// downloaded and no model is loaded.
struct FixtureEngineLoader: OnDeviceEngineLoading {
    private let clock: any AppClock

    init(clock: any AppClock) {
        self.clock = clock
    }

    func loadVoz(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any BatchRecognizing {
        // Half of the 467 MB the catalogue names, so the block shows a byte count.
        progress(
            ModelPreparationProgress(
                fraction: 0.5,
                phase: .downloading,
                counter: .bytes(completed: 233_500_000, total: 467_000_000)
            )
        )
        try await clock.sleep(seconds: FixtureValues.preparationHoldSeconds)
        return FixtureBatchEngine()
    }

    func loadParakeet(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing {
        progress(
            ModelPreparationProgress(
                fraction: 0.5,
                phase: .downloading,
                counter: .files(completed: 3, total: 6)
            )
        )
        try await clock.sleep(seconds: FixtureValues.preparationHoldSeconds)
        return FixtureStreamingEngine()
    }

    func loadFluid(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing {
        // Half of the model's 252,710,832 bytes, so the block shows a byte count.
        progress(
            ModelPreparationProgress(
                fraction: 0.5,
                phase: .downloading,
                counter: .bytes(completed: 126_355_416, total: 252_710_832)
            )
        )
        try await clock.sleep(seconds: FixtureValues.preparationHoldSeconds)
        return FixtureStreamingEngine()
    }
}

/// A batch recogniser that hears nothing: no words, and the duration of the samples at 16 kHz. The test
/// seam never gives it audio, because its transcriber opens no microphone.
struct FixtureBatchEngine: BatchRecognizing {
    init() {}

    func transcribe(samples: [Float]) async throws -> BatchRecognition {
        BatchRecognition(words: [], duration: Double(samples.count) / 16_000)
    }
}

/// The system recogniser of the test seam: its language asset is installed, on every iOS, so a UI test
/// can choose Apple speech and start at once. It hears nothing; the fixture transcriber never calls it.
struct FixtureSystemSpeech: SystemSpeechRecognizing {
    init() {}

    var knownAvailability: SystemSpeechAvailability {
        .installed
    }

    func availability() async -> SystemSpeechAvailability {
        .installed
    }

    func install(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws {}

    func begin() async throws -> AsyncThrowingStream<SystemSpeechResult, any Error> {
        let (results, sink) = AsyncThrowingStream<SystemSpeechResult, any Error>.makeStream()
        sink.finish()
        return results
    }

    func process(samples: [Float]) async throws {}

    func finish() async throws {}

    func cancel() async {}
}

/// A streaming recogniser that hears nothing. It keeps no state, so every call returns at once.
struct FixtureStreamingEngine: StreamingRecognizing {
    init() {}

    func reset() async {}

    func setPartialHandler(_ handler: @escaping @Sendable (String) -> Void) async {}

    @discardableResult
    func process(samples: [Float]) async throws -> Bool {
        true
    }

    func finish() async throws -> String {
        ""
    }

    func cleanup() async {}
}
