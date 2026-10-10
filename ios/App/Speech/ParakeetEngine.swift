import FluidAudio
import Foundation

/// The FluidAudio streaming recogniser behind `StreamingRecognizing`. The only file that imports
/// `FluidAudio`.
struct ParakeetEngine: StreamingRecognizing {
    static func load(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> ParakeetEngine {
        // STUB(WP-07)
        throw CocoaError(.featureUnsupported)
    }

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

    /// Names the package's recogniser, so the build keeps proving that the package compiles and links.
    static var packageTypeName: String {
        String(reflecting: StreamingEouAsrManager.self)
    }
}
