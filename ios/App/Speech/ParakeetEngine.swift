import AVFoundation
import CoreML
import FluidAudio
import Foundation

/// The FluidAudio streaming recogniser behind `StreamingRecognizing`. One of the two files that import
/// `FluidAudio`; the other is `FluidSpeechEngine.swift`.
///
/// The recogniser is an actor, so every call is awaited and its work runs off the main actor. Its
/// progress value is never named here, because the other speech package has a type of the same name.
struct ParakeetEngine: StreamingRecognizing {
    private let manager: StreamingEouAsrManager

    /// Downloads the models when they are not cached, then loads them: CPU and Neural Engine, the
    /// 320 ms chunk model, 640 ms of silence before an end of utterance. The package keeps its models in
    /// its default place, `Application Support/FluidAudio`.
    static func load(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> ParakeetEngine {
        let configuration = MLModelConfiguration()
        configuration.computeUnits = .cpuAndNeuralEngine
        let manager = StreamingEouAsrManager(configuration: configuration, chunkSize: .ms320, eouDebounceMs: 640)

        try await manager.loadModels(to: nil, configuration: nil, progressHandler: { update in
            switch update.phase {
            case .listing:
                progress(ModelPreparationProgress(fraction: update.fractionCompleted, phase: .checking, counter: .none))
            case .downloading(let completedFiles, let totalFiles):
                progress(
                    ModelPreparationProgress(
                        fraction: update.fractionCompleted,
                        phase: .downloading,
                        counter: .files(completed: completedFiles, total: totalFiles)
                    )
                )
            case .compiling:
                progress(ModelPreparationProgress(fraction: update.fractionCompleted, phase: .preparing, counter: .none))
            }
        })

        // The models are part of the app's data now. They can be downloaded again, so they stay out of
        // backups.
        if let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first {
            BackupExclusion.exclude(support.appendingPathComponent("FluidAudio", isDirectory: true))
        }
        return ParakeetEngine(manager: manager)
    }

    func reset() async {
        await manager.reset()
    }

    func setPartialHandler(_ handler: @escaping @Sendable (String) -> Void) async {
        await manager.setPartialCallback(handler)
    }

    /// 16 kHz mono samples, wrapped in the buffer the recogniser takes. Its own result is empty: the
    /// text arrives through the partial handler. Every buffer is recognised as it comes.
    @discardableResult
    func process(samples: [Float]) async throws -> Bool {
        guard !samples.isEmpty else { return true }
        let buffer = try ParakeetEngine.makeBuffer(samples)
        _ = try await manager.process(audioBuffer: buffer)
        return true
    }

    func finish() async throws -> String {
        try await manager.finish()
    }

    func cleanup() async {
        await manager.cleanup()
    }

    /// A Float32, 16 kHz, mono buffer holding the samples. It is the format the recogniser works in, so
    /// nothing is resampled on the way.
    private static func makeBuffer(_ samples: [Float]) throws -> AVAudioPCMBuffer {
        guard let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16_000, channels: 1, interleaved: false),
              let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(samples.count)),
              let channels = buffer.floatChannelData else {
            throw ParakeetBufferFailure()
        }
        buffer.frameLength = AVAudioFrameCount(samples.count)
        samples.withUnsafeBufferPointer { source in
            if let base = source.baseAddress {
                channels[0].update(from: base, count: source.count)
            }
        }
        return buffer
    }
}

/// The audio buffer for the recogniser could not be made.
private struct ParakeetBufferFailure: Error {}
