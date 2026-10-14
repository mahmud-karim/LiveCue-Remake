@preconcurrency import CoreML
import FluidAudio
import Foundation

/// Fluid Speech Mini behind `WindowRecognizing` (D-29). With `ParakeetEngine.swift`, one of the two files
/// that import `FluidAudio`.
///
/// The model has the format of Parakeet TDT 0.6B v3, so the pinned package runs it as its v3. The
/// package's own v3 loader looks for other file names than the model ships, so the four models are
/// loaded here, from the app's own folder, and handed to the package ready-made. No language hint is
/// ever passed: with one the package reads outputs this model's joint network does not have.
///
/// Each call transcribes one window from a fresh decoder state. The recogniser is an actor, so the
/// work runs off the main actor.
struct FluidSpeechEngine: WindowRecognizing {
    private let recognizer: AsrManager
    let minimumSamples: Int

    /// Downloads the model's files when they are not all in place, then loads them: the preprocessor on
    /// the CPU, the encoder, the decoder and the joint network on CPU and Neural Engine, as the
    /// package's own loader does for this format. The first load after a download takes a while: the
    /// system prepares the models for the Neural Engine.
    static func load(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> FluidSpeechEngine {
        let downloader = try ModelDownloader.fluidSpeechMini()
        let folder = try await downloader.prepare(progress: progress)
        // The marker is written after the last file was checked. Without it nothing is loaded.
        guard downloader.isComplete else {
            throw ModelDownloadFailure.incomplete
        }

        let pieces = try vocabulary(at: folder.appendingPathComponent(vocabularyFile, isDirectory: false))
        let preprocessor = try model("Preprocessor", in: folder, units: .cpuOnly)
        let encoder = try model("Encoder", in: folder, units: .cpuAndNeuralEngine)
        let decoder = try model("Decoder", in: folder, units: .cpuAndNeuralEngine)
        let joint = try model("JointDecision", in: folder, units: .cpuAndNeuralEngine)
        let models = AsrModels(
            encoder: encoder,
            preprocessor: preprocessor,
            decoder: decoder,
            joint: joint,
            configuration: MLModelConfigurationUtils.defaultConfiguration(computeUnits: .cpuAndNeuralEngine),
            vocabulary: pieces,
            version: .v3
        )
        let recognizer = AsrManager()
        try await recognizer.loadModels(models)
        return FluidSpeechEngine(
            recognizer: recognizer,
            minimumSamples: ASRConstants.minimumRequiredSamples(forSampleRate: sampleRate)
        )
    }

    /// 16 kHz mono samples, between `minimumSamples` and 15 seconds: the package then runs them as one
    /// window. Word times are seconds from the start of the samples.
    func transcribe(samples: [Float]) async throws -> WindowRecognition {
        var state = TdtDecoderState.make(decoderLayers: await recognizer.decoderLayerCount)
        let result = try await recognizer.transcribe(samples, decoderState: &state)
        let words = buildWordTimings(from: result.tokenTimings ?? []).map { timing in
            RecognizedWord(text: timing.word, start: timing.startTime, end: timing.endTime)
        }
        return WindowRecognition(text: result.text, words: words)
    }

    func cleanup() async {
        await recognizer.cleanup()
    }

    // MARK: - Pieces

    private static let sampleRate = 16_000
    private static let vocabularyFile = "parakeet_vocab.json"

    /// One compiled model of the folder.
    private static func model(_ name: String, in folder: URL, units: MLComputeUnits) throws -> MLModel {
        try MLModel(
            contentsOf: folder.appendingPathComponent(name + ".mlmodelc", isDirectory: true),
            configuration: MLModelConfigurationUtils.defaultConfiguration(computeUnits: units)
        )
    }

    /// The vocabulary as the package reads it for this format: a JSON object from the token's number,
    /// written as text, to its piece.
    private static func vocabulary(at url: URL) throws -> [Int: String] {
        guard let pieces = try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: String] else {
            throw FluidSpeechFailure.vocabulary
        }
        var vocabulary: [Int: String] = [:]
        for (key, piece) in pieces {
            if let token = Int(key) {
                vocabulary[token] = piece
            }
        }
        guard !vocabulary.isEmpty else {
            throw FluidSpeechFailure.vocabulary
        }
        return vocabulary
    }
}

/// The model's files are in place but cannot be used.
private enum FluidSpeechFailure: LocalizedError {
    case vocabulary

    var errorDescription: String? {
        "The vocabulary of Fluid Speech Mini could not be read."
    }
}
