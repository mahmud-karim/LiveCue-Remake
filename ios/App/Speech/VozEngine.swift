import Foundation
import Voz

/// The Voz recogniser behind `BatchRecognizing`. The only file that imports `Voz`.
struct VozEngine: BatchRecognizing {
    static func load(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> VozEngine {
        // STUB(WP-07)
        throw CocoaError(.featureUnsupported)
    }

    func transcribe(samples: [Float]) async throws -> BatchRecognition {
        // STUB(WP-07)
        BatchRecognition(words: [], duration: 0)
    }

    /// Names the package's recogniser, so the build keeps proving that the package compiles and links.
    static var packageTypeName: String {
        String(reflecting: Voz.self)
    }
}
