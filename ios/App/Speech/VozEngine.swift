import Foundation
import Voz

/// The Voz recogniser behind `BatchRecognizing`. The only file that imports `Voz`.
///
/// The package's names are used unqualified: the module and its main type share the name `Voz`. Its
/// progress value is never named either, because the other speech package has a type of the same name.
struct VozEngine: BatchRecognizing {
    private let recognizer: Voz

    /// Downloads the model when it is not cached, then loads it. The package picks the model for its
    /// pinned version; the app names no file.
    static func load(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> VozEngine {
        // The download returns the model's folder. It becomes part of the app's data, so it is kept out
        // of backups before anything else happens.
        let directory = try await Voz.download(progress: { update in
            progress(VozEngine.preparation(fraction: update.fraction, completed: update.completedBytes, total: update.totalBytes))
        })
        BackupExclusion.exclude(URL(fileURLWithPath: directory, isDirectory: true))

        // Loading finds the files in place. The first load after a download takes a while: the system
        // prepares the model for the Neural Engine.
        let recognizer = try await Voz(progress: { update in
            progress(VozEngine.preparation(fraction: update.fraction, completed: update.completedBytes, total: update.totalBytes))
        })
        return VozEngine(recognizer: recognizer)
    }

    /// 16 kHz mono samples. The recogniser is an actor: the work runs off the main actor.
    func transcribe(samples: [Float]) async throws -> BatchRecognition {
        let result = try await recognizer.transcribe(samples: samples)
        return BatchRecognition(
            words: result.words.map { RecognizedWord(text: $0.text, start: $0.start, end: $0.end) },
            duration: result.duration
        )
    }

    /// Bytes done of all the model's files. Downloading below 1; at 1 the files are there and the model
    /// is being prepared.
    private static func preparation(fraction: Double, completed: Int64, total: Int64) -> ModelPreparationProgress {
        ModelPreparationProgress(
            fraction: fraction,
            phase: fraction < 1 ? .downloading : .preparing,
            counter: .bytes(completed: completed, total: total)
        )
    }
}
