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

/// The system recogniser, scripted by hand: what it answers when asked, what an installation does, and
/// the results of a recognition.
actor FakeSystemSpeech: SystemSpeechRecognizing {
    nonisolated let knownAvailability: SystemSpeechAvailability

    private var answer: SystemSpeechAvailability
    /// A successful installation leaves the asset installed, unless a test says otherwise.
    private var answerAfterInstall: SystemSpeechAvailability? = .installed
    private var installSteps: [ModelPreparationProgress] = []
    private var installFailure: (any Error)?
    private var installGate: FakeGate?
    private var beginFails = false
    private var processFails = false
    private var finishFails = false
    private var finishGate: FakeGate?
    private var finishResults: [SystemSpeechResult] = []
    private var sink: AsyncThrowingStream<SystemSpeechResult, any Error>.Continuation?

    private(set) var availabilityCount = 0
    private(set) var installCount = 0
    private(set) var beginCount = 0
    /// How many times `process` was called.
    private(set) var processed = 0
    /// How many samples `process` received in total.
    private(set) var processedSamples = 0
    private(set) var finishCount = 0
    private(set) var cancelCount = 0

    /// - Parameters:
    ///   - known: what is known without asking; `.unknown` is the real engine's.
    ///   - answer: what the system says when asked.
    init(known: SystemSpeechAvailability = .unknown, answer: SystemSpeechAvailability = .installed) {
        knownAvailability = known
        self.answer = answer
    }

    func setAnswer(_ answer: SystemSpeechAvailability) {
        self.answer = answer
    }

    /// What `install` does: report `steps`, wait for `gate`, then throw `failure` or change the answer.
    func setInstall(
        steps: [ModelPreparationProgress] = [],
        failure: (any Error)? = nil,
        gate: FakeGate? = nil,
        answerAfterwards: SystemSpeechAvailability? = .installed
    ) {
        installSteps = steps
        installFailure = failure
        installGate = gate
        answerAfterInstall = answerAfterwards
    }

    /// True: `begin` throws `FakeEngineFailure`.
    func setBeginError(_ fails: Bool) {
        beginFails = fails
    }

    /// True: `process` throws `FakeEngineFailure`.
    func setProcessError(_ fails: Bool) {
        processFails = fails
    }

    /// True: `finish` throws `FakeEngineFailure`.
    func setFinishError(_ fails: Bool) {
        finishFails = fails
    }

    /// With a gate, `finish` waits until the gate opens. `cancel` opens it.
    func setFinishGate(_ gate: FakeGate?) {
        finishGate = gate
    }

    /// The results `finish` delivers before the stream ends: what the recogniser still held.
    func setFinishResults(_ results: [SystemSpeechResult]) {
        finishResults = results
    }

    /// Delivers a result of the running recognition, as the recogniser does off the main actor.
    func emit(_ result: SystemSpeechResult) {
        sink?.yield(result)
    }

    /// Ends the results of the running recognition with an error.
    func failResults() {
        sink?.finish(throwing: FakeEngineFailure())
        sink = nil
    }

    // MARK: - SystemSpeechRecognizing

    func availability() async -> SystemSpeechAvailability {
        availabilityCount += 1
        return answer
    }

    func install(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws {
        installCount += 1
        for step in installSteps {
            progress(step)
        }
        if let installGate {
            await installGate.wait()
        }
        if let installFailure {
            throw installFailure
        }
        if let answerAfterInstall {
            answer = answerAfterInstall
        }
    }

    func begin() async throws -> AsyncThrowingStream<SystemSpeechResult, any Error> {
        beginCount += 1
        if beginFails {
            throw FakeEngineFailure()
        }
        sink?.finish()
        let (results, newSink) = AsyncThrowingStream<SystemSpeechResult, any Error>.makeStream()
        sink = newSink
        return results
    }

    func process(samples: [Float]) async throws {
        processed += 1
        processedSamples += samples.count
        if processFails {
            throw FakeEngineFailure()
        }
    }

    func finish() async throws {
        finishCount += 1
        if let finishGate {
            await finishGate.wait()
        }
        if finishFails {
            throw FakeEngineFailure()
        }
        for result in finishResults {
            sink?.yield(result)
        }
        sink?.finish()
        sink = nil
    }

    func cancel() async {
        cancelCount += 1
        sink?.finish()
        sink = nil
        await finishGate?.open()
    }
}
