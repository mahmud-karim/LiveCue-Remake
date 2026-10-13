import AVFoundation
import CoreMedia
import Foundation
import Speech

// The recogniser built into iOS 26 behind `SystemSpeechRecognizing`. The only file that imports `Speech`.
//
// The framework has a class with the same name as this app's transcriber protocol, so that class is
// always written with its module name here.
//
// Nothing in this file asks for speech-recognition authorisation. Apple's "Asking Permission to Use
// Speech Recognition" says that process applies to the older recogniser, which can send audio to Apple's
// servers; the analyzer's transcriber modules work on the device. The older recogniser is never named
// here, so its authorisation call, which ends a process whose host declares no usage text, cannot run.

/// Makes the system recogniser of this iPhone.
enum AppleSpeech {
    static func makeEngine() -> any SystemSpeechRecognizing {
        if #available(iOS 26.0, *) {
            return AppleSpeechEngine(mark: SpeechProbeMark(defaults: UserDefaults.standard))
        }
        return UnavailableSystemSpeech()
    }
}

/// English live transcription with tentative and final results. The language asset belongs to the
/// system, which downloads, keeps, updates and shares it; the app only asks for it.
@available(iOS 26.0, *)
actor AppleSpeechEngine: SystemSpeechRecognizing {
    nonisolated let knownAvailability = SystemSpeechAvailability.unknown

    /// One recognition, from `begin` to `finish` or `cancel`.
    private struct Session {
        let analyzer: SpeechAnalyzer
        let input: AsyncStream<AnalyzerInput>.Continuation
        let results: AsyncThrowingStream<SystemSpeechResult, any Error>.Continuation
        let relay: Task<Void, Never>
        /// What the capture delivers: Float32, 16 kHz, mono.
        let captured: AVAudioFormat
        /// What the analyzer takes.
        let format: AVAudioFormat
        /// nil when the analyzer takes the captured format as it is.
        let converter: AVAudioConverter?
    }

    private let mark: SpeechProbeMark
    private var markWasRead = false
    /// The last launch asked the system and never got an answer: this launch does not ask by itself.
    private var heldBack = false
    private var session: Session?

    init(mark: SpeechProbeMark) {
        self.mark = mark
    }

    // MARK: - Availability and the language asset

    func availability() async -> SystemSpeechAvailability {
        if !markWasRead {
            markWasRead = true
            heldBack = mark.takeUnanswered()
        }
        if heldBack {
            return .unavailable(reason: SystemSpeechText.stoppedLastTime)
        }
        mark.asking()
        let answer = await AppleSpeechEngine.ask()
        mark.answered()
        return answer
    }

    func install(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws {
        // Asked for by the user: whatever happened last time, this is a new try.
        markWasRead = true
        heldBack = false
        progress(ModelPreparationProgress(fraction: 0, phase: .checking, counter: .none))
        do {
            guard await Speech.SpeechTranscriber.isAvailable else {
                throw SystemSpeechFailure(SystemSpeechText.unsupportedDevice)
            }
            guard let locale = await AppleSpeechEngine.englishLocale() else {
                throw SystemSpeechFailure(SystemSpeechText.noEnglish)
            }
            let transcriber = AppleSpeechEngine.makeTranscriber(locale)
            // No request: there is nothing to download.
            if let request = try await AssetInventory.assetInstallationRequest(supporting: [transcriber]) {
                let observation = request.progress.observe(\.fractionCompleted, options: [.initial, .new]) { observed, _ in
                    progress(ModelPreparationProgress(fraction: observed.fractionCompleted, phase: .downloading, counter: .none))
                }
                defer { observation.invalidate() }
                try await request.downloadAndInstall()
            }
            progress(ModelPreparationProgress(fraction: 1, phase: .preparing, counter: .none))
        } catch {
            throw EngineLoadFailure.readable(error)
        }
    }

    private static func ask() async -> SystemSpeechAvailability {
        guard await Speech.SpeechTranscriber.isAvailable else {
            return .unavailable(reason: SystemSpeechText.unsupportedDevice)
        }
        guard let locale = await englishLocale() else {
            return .unavailable(reason: SystemSpeechText.noEnglish)
        }
        let status = await AssetInventory.status(forModules: [makeTranscriber(locale)])
        switch status {
        case .installed:
            return .installed
        case .supported, .downloading:
            return .needsDownload
        case .unsupported:
            return .unavailable(reason: SystemSpeechText.unsupportedDevice)
        @unknown default:
            return .needsDownload
        }
    }

    /// The device's own English when the system transcribes it, otherwise American English.
    private static func englishLocale() async -> Locale? {
        let device = Locale.current
        if device.language.languageCode?.identifier == "en",
           let match = await Speech.SpeechTranscriber.supportedLocale(equivalentTo: device) {
            return match
        }
        return await Speech.SpeechTranscriber.supportedLocale(equivalentTo: Locale(identifier: "en_US"))
    }

    /// Tentative results while a phrase is spoken, tuned for a short delay: the options of the
    /// framework's preset for live audio.
    private static func makeTranscriber(_ locale: Locale) -> Speech.SpeechTranscriber {
        Speech.SpeechTranscriber(
            locale: locale,
            transcriptionOptions: [],
            reportingOptions: [.volatileResults, .fastResults],
            attributeOptions: []
        )
    }

    // MARK: - One recognition

    func begin() async throws -> AsyncThrowingStream<SystemSpeechResult, any Error> {
        await cancel()

        guard let locale = await AppleSpeechEngine.englishLocale() else {
            throw SystemSpeechFailure(SystemSpeechText.noEnglish)
        }
        guard let captured = AVAudioFormat(
            commonFormat: .pcmFormatFloat32,
            sampleRate: AppleSpeechEngine.sampleRate,
            channels: 1,
            interleaved: false
        ) else {
            throw AppleSpeechBufferFailure()
        }

        let transcriber = AppleSpeechEngine.makeTranscriber(locale)
        // The analyzer converts nothing: it says which format it wants, and the audio is brought to it.
        let preferred = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [transcriber], considering: captured)
        let format = preferred ?? captured
        var converter: AVAudioConverter?
        if format != captured {
            guard let made = AVAudioConverter(from: captured, to: format) else {
                throw AppleSpeechBufferFailure()
            }
            converter = made
        }

        let analyzer = SpeechAnalyzer(modules: [transcriber])
        let (inputs, input) = AsyncStream<AnalyzerInput>.makeStream()
        let (results, sink) = AsyncThrowingStream<SystemSpeechResult, any Error>.makeStream()
        // The module's results end when the analyzer finishes, and throw when it fails.
        let relay = Task {
            do {
                for try await result in transcriber.results {
                    sink.yield(AppleSpeechEngine.plain(result))
                }
                sink.finish()
            } catch {
                sink.finish(throwing: error)
            }
        }

        do {
            // Returns at once; the analyzer then works on the input as it arrives.
            try await analyzer.start(inputSequence: inputs)
        } catch {
            relay.cancel()
            input.finish()
            sink.finish()
            throw error
        }

        session = Session(
            analyzer: analyzer,
            input: input,
            results: sink,
            relay: relay,
            captured: captured,
            format: format,
            converter: converter
        )
        return results
    }

    func process(samples: [Float]) async throws {
        guard let session, !samples.isEmpty else { return }
        if let buffer = try AppleSpeechEngine.makeBuffer(samples, for: session) {
            session.input.yield(AnalyzerInput(buffer: buffer))
        }
    }

    func finish() async throws {
        guard let session else { return }
        session.input.finish()
        // Finalises through the end of the input and finishes the analyzer, which ends the results.
        try await session.analyzer.finalizeAndFinishThroughEndOfInput()
        if let current = self.session, current.analyzer === session.analyzer {
            self.session = nil
        }
    }

    func cancel() async {
        guard let session else { return }
        self.session = nil
        session.input.finish()
        try? await session.analyzer.cancelAndFinishNow()
        session.relay.cancel()
        session.results.finish()
    }

    // MARK: - Pieces

    private static func plain(_ result: Speech.SpeechTranscriber.Result) -> SystemSpeechResult {
        SystemSpeechResult(
            text: String(result.text.characters),
            isFinal: result.isFinal,
            startSeconds: seconds(result.range.start),
            endSeconds: seconds(result.range.end)
        )
    }

    private static func seconds(_ time: CMTime) -> TimeInterval {
        let value = time.seconds
        return value.isFinite ? max(0, value) : 0
    }

    /// The samples in the format the analyzer takes; nil when the converter kept them all for later.
    private static func makeBuffer(_ samples: [Float], for session: Session) throws -> AVAudioPCMBuffer? {
        guard let source = AVAudioPCMBuffer(pcmFormat: session.captured, frameCapacity: AVAudioFrameCount(samples.count)),
              let channels = source.floatChannelData else {
            throw AppleSpeechBufferFailure()
        }
        source.frameLength = AVAudioFrameCount(samples.count)
        samples.withUnsafeBufferPointer { pointer in
            if let base = pointer.baseAddress {
                channels[0].update(from: base, count: pointer.count)
            }
        }
        guard let converter = session.converter else {
            return source
        }

        let ratio = session.format.sampleRate / session.captured.sampleRate
        let capacity = AVAudioFrameCount((Double(samples.count) * ratio).rounded(.up)) + 64
        guard let output = AVAudioPCMBuffer(pcmFormat: session.format, frameCapacity: capacity) else {
            throw AppleSpeechBufferFailure()
        }
        // The converter asks for input through the block. It gets this one buffer, then "no more for
        // now", which leaves it ready for the next one.
        let pending = AppleSpeechPendingBuffer(source)
        var failure: NSError?
        let status = converter.convert(to: output, error: &failure) { _, inputStatus in
            guard let next = pending.take() else {
                inputStatus.pointee = .noDataNow
                return nil
            }
            inputStatus.pointee = .haveData
            return next
        }
        guard status != .error, failure == nil else {
            throw AppleSpeechBufferFailure()
        }
        return output.frameLength > 0 ? output : nil
    }

    private static let sampleRate: Double = 16_000
}

/// The one buffer a conversion hands to the converter.
private final class AppleSpeechPendingBuffer {
    private var buffer: AVAudioPCMBuffer?

    init(_ buffer: AVAudioPCMBuffer) {
        self.buffer = buffer
    }

    func take() -> AVAudioPCMBuffer? {
        let taken = buffer
        buffer = nil
        return taken
    }
}

/// An audio buffer for the system recogniser could not be made.
private struct AppleSpeechBufferFailure: Error {}
