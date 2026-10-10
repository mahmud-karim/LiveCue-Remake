import Foundation

/// Loads either on-device engine and marks its model folder excluded from backup.
struct LiveOnDeviceEngineLoader: OnDeviceEngineLoading {
    init() {}

    func loadVoz(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any BatchRecognizing {
        // STUB(WP-07)
        try await VozEngine.load(progress: progress)
    }

    func loadParakeet(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> any StreamingRecognizing {
        // STUB(WP-07)
        try await ParakeetEngine.load(progress: progress)
    }
}
