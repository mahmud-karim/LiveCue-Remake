import Foundation

/// The engine loader of the test seam: reports 0.5, waits 2 s, returns a fixture engine.
struct FixtureEngineLoader: OnDeviceEngineLoading {
    private let clock: any AppClock

    init(clock: any AppClock) {
        self.clock = clock
    }

    func loadVoz(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any BatchRecognizing {
        // STUB(WP-07)
        FixtureBatchEngine()
    }

    func loadParakeet(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing {
        // STUB(WP-07)
        FixtureStreamingEngine()
    }
}

struct FixtureBatchEngine: BatchRecognizing {
    init() {}

    func transcribe(samples: [Float]) async throws -> BatchRecognition {
        // STUB(WP-07)
        BatchRecognition(words: [], duration: 0)
    }
}

struct FixtureStreamingEngine: StreamingRecognizing {
    init() {}

    func reset() async {
        // STUB(WP-07)
    }

    func setPartialHandler(_ handler: @escaping @Sendable (String) -> Void) async {
        // STUB(WP-07)
    }

    func process(samples: [Float]) async throws {
        // STUB(WP-07)
    }

    func finish() async throws -> String {
        // STUB(WP-07)
        ""
    }

    func cleanup() async {
        // STUB(WP-07)
    }
}
