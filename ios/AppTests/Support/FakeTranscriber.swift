import Foundation
import LiveCueCore
@testable import LiveCueRemake

/// A transcriber a test scripts by hand.
@MainActor final class FakeTranscriber: SpeechTranscriber {
    let provider: SpeechProvider
    private let channel: TranscriberEventChannel
    private var heldStarts: [CheckedContinuation<Void, Never>] = []

    /// What `start` does after recording the call. Default: success.
    var startResult: Result<Void, TranscriberFailure> = .success(())
    /// What `finalize` does after emitting `segmentsOnFinalize`. Default: success.
    var finalizeResult: Result<Void, TranscriberFailure> = .success(())
    /// What `transcribePending` does after emitting `segmentsOnTranscribePending`. Default: success.
    var transcribePendingResult: Result<Void, TranscriberFailure> = .success(())
    /// The seconds `transcribePending` returns.
    var pendingSeconds: TimeInterval = 0
    /// Emitted as segments before `finalize` returns, then cleared.
    var segmentsOnFinalize: [String] = []
    /// Emitted as segments before `transcribePending` returns, then cleared.
    var segmentsOnTranscribePending: [String] = []
    /// True: `start` suspends until `releaseStart()`.
    var holdStart = false

    /// "start", "finalize", "transcribePending", "shutdown", in call order.
    private(set) var calls: [String] = []
    private(set) var starts: [TranscriberStart] = []

    init(provider: SpeechProvider) {
        self.provider = provider
        self.channel = TranscriberEventChannel()
    }

    var events: AsyncStream<TranscriberEvent> {
        channel.stream
    }

    var emittedEventCount: Int {
        channel.emittedCount
    }

    /// Emits with the generation of the last start.
    func emit(_ kind: TranscriberEvent.Kind) {
        emit(kind, generation: starts.last?.generation ?? UUID())
    }

    func emit(_ kind: TranscriberEvent.Kind, generation: UUID) {
        channel.emit(kind, generation: generation)
    }

    /// Lets every held `start` go on.
    func releaseStart() {
        let held = heldStarts
        heldStarts = []
        for continuation in held {
            continuation.resume()
        }
    }

    // MARK: - SpeechTranscriber

    func start(_ start: TranscriberStart) async throws {
        calls.append("start")
        starts.append(start)
        if holdStart {
            await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
                heldStarts.append(continuation)
            }
        }
        try startResult.get()
    }

    func finalize() async throws {
        calls.append("finalize")
        emitSegments(segmentsOnFinalize)
        segmentsOnFinalize = []
        try finalizeResult.get()
    }

    func transcribePending() async throws -> TimeInterval {
        calls.append("transcribePending")
        emitSegments(segmentsOnTranscribePending)
        segmentsOnTranscribePending = []
        try transcribePendingResult.get()
        return pendingSeconds
    }

    func shutdown() async {
        calls.append("shutdown")
    }

    private func emitSegments(_ texts: [String]) {
        let offset = starts.last?.streamOffsetSeconds ?? 0
        for text in texts {
            emit(.segment(TranscriptSegment(text: text, startSeconds: offset, endSeconds: offset)))
        }
    }
}

@MainActor final class FakeTranscriberFactory: SpeechTranscriberFactory {
    /// Every transcriber made so far, in order.
    private(set) var made: [FakeTranscriber] = []
    /// Called with each new transcriber before it is handed out.
    var configure: ((FakeTranscriber) -> Void)?

    init() {}

    func makeTranscriber(for provider: SpeechProvider) -> any SpeechTranscriber {
        let transcriber = FakeTranscriber(provider: provider)
        configure?(transcriber)
        made.append(transcriber)
        return transcriber
    }
}

/// A Meta key test a test scripts by hand. Default: success.
actor FakeMetaKeyTester: MetaKeyTesting {
    private var result: Result<Void, MetaSpeechError> = .success(())
    private var held = false
    private var heldTests: [CheckedContinuation<Void, Never>] = []

    /// The key of every test, in order.
    private(set) var testedKeys: [String] = []

    init() {}

    func setResult(_ result: Result<Void, MetaSpeechError>) {
        self.result = result
    }

    /// True: `test(key:)` suspends until `release()`.
    func setHold(_ hold: Bool) {
        held = hold
    }

    func release() {
        let waiting = heldTests
        heldTests = []
        for continuation in waiting {
            continuation.resume()
        }
    }

    func test(key: String) async throws {
        testedKeys.append(key)
        if held {
            await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
                heldTests.append(continuation)
            }
        }
        try result.get()
    }
}
