import Foundation
@testable import LiveCueRemake

/// The error the fake engines throw when a test asks them to fail.
struct FakeEngineFailure: Error, Equatable {}

/// A door a test opens: `wait()` suspends until `open()` was called.
actor FakeGate {
    private var isOpen = false
    private var waiting: [CheckedContinuation<Void, Never>] = []

    init() {}

    func wait() async {
        if isOpen { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiting.append(continuation)
        }
    }

    func open() {
        isOpen = true
        let held = waiting
        waiting = []
        for continuation in held {
            continuation.resume()
        }
    }
}

/// A batch recogniser a test scripts by hand. Default result: no words, and the duration of the samples
/// at 16 kHz.
actor FakeBatchEngine: BatchRecognizing {
    private var result: Result<BatchRecognition, any Error>?
    private var gate: FakeGate?

    /// The sample count of every `transcribe`, in order.
    private(set) var calls: [Int] = []

    init() {}

    func setResult(_ result: Result<BatchRecognition, any Error>) {
        self.result = result
    }

    /// With a gate, `transcribe` records the call and then waits for the gate to open.
    func setGate(_ gate: FakeGate?) {
        self.gate = gate
    }

    func transcribe(samples: [Float]) async throws -> BatchRecognition {
        calls.append(samples.count)
        if let gate {
            await gate.wait()
        }
        if let result {
            return try result.get()
        }
        return BatchRecognition(words: [], duration: Double(samples.count) / 16_000)
    }
}

/// A streaming recogniser a test scripts by hand.
actor FakeStreamingEngine: StreamingRecognizing {
    private var handler: (@Sendable (String) -> Void)?
    private var finalText = ""
    private var processFails = false
    private var finishFails = false

    /// How many times `process` was called.
    private(set) var processed = 0
    /// How many samples `process` received in total.
    private(set) var processedSamples = 0
    private(set) var resetCount = 0
    private(set) var finishCount = 0
    private(set) var cleanupCount = 0

    init() {}

    /// Calls the partial handler, as the recogniser does off the main actor.
    func emitPartial(_ text: String) {
        handler?(text)
    }

    /// What `finish` returns.
    func setFinal(_ text: String) {
        finalText = text
    }

    /// True: `process` throws `FakeEngineFailure`.
    func setProcessError(_ fails: Bool) {
        processFails = fails
    }

    /// True: `finish` throws `FakeEngineFailure`.
    func setFinishError(_ fails: Bool) {
        finishFails = fails
    }

    // MARK: - StreamingRecognizing

    func reset() async {
        resetCount += 1
    }

    func setPartialHandler(_ handler: @escaping @Sendable (String) -> Void) async {
        self.handler = handler
    }

    func process(samples: [Float]) async throws {
        processed += 1
        processedSamples += samples.count
        if processFails {
            throw FakeEngineFailure()
        }
    }

    func finish() async throws -> String {
        finishCount += 1
        if finishFails {
            throw FakeEngineFailure()
        }
        return finalText
    }

    func cleanup() async {
        cleanupCount += 1
    }
}

/// An engine loader that reports `steps`, waits for `gate` when there is one, then throws `failure` or
/// returns the fake engine.
struct FakeEngineLoader: OnDeviceEngineLoading {
    private let voz: FakeBatchEngine
    private let parakeet: FakeStreamingEngine
    private let steps: [ModelPreparationProgress]
    private let failure: (any Error)?
    private let gate: FakeGate?

    init(
        voz: FakeBatchEngine,
        parakeet: FakeStreamingEngine,
        steps: [ModelPreparationProgress] = [],
        failure: (any Error)? = nil,
        gate: FakeGate? = nil
    ) {
        self.voz = voz
        self.parakeet = parakeet
        self.steps = steps
        self.failure = failure
        self.gate = gate
    }

    func loadVoz(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any BatchRecognizing {
        try await run(progress)
        return voz
    }

    func loadParakeet(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing {
        try await run(progress)
        return parakeet
    }

    private func run(_ progress: @Sendable (ModelPreparationProgress) -> Void) async throws {
        for step in steps {
            progress(step)
        }
        if let gate {
            await gate.wait()
        }
        if let failure {
            throw failure
        }
    }
}
