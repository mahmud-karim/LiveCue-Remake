import Foundation
import LiveCueCore
import Observation

/// The progress block of one model preparation.
struct ModelPreparation: Equatable, Sendable {
    /// The model this block belongs to.
    let variant: String
    var fraction: Double
    var startedAt: Date
    /// Elapsed time freezes here.
    var finishedAt: Date?
    /// "212 MB / 467 MB" or "3 / 7 files"
    var counterText: String?
    var failed: Bool
}

/// Preparation, progress, reload and unloading of the on-device models: one recogniser in memory at a time.
@MainActor @Observable final class OnDeviceModelManager {
    /// Never restored at launch (IOS-B1).
    private(set) var loadedVariant: String? = nil
    /// nil once its model is ready (IOS-B13).
    private(set) var preparation: ModelPreparation? = nil
    private(set) var statusLine: String = "Prepare a model"

    var isPreparing: Bool {
        // STUB(WP-07)
        false
    }

    var vozEngine: (any BatchRecognizing)? {
        // STUB(WP-07)
        nil
    }

    var parakeetEngine: (any StreamingRecognizing)? {
        // STUB(WP-07)
        nil
    }

    init(loader: any OnDeviceEngineLoading, activity: SessionActivity, clock: any AppClock) {
        // STUB(WP-07)
    }

    func isLoaded(for provider: SpeechProvider) -> Bool {
        // STUB(WP-07)
        false
    }

    func loadedModel(for provider: SpeechProvider) -> TranscriptionModel? {
        // STUB(WP-07)
        nil
    }

    /// "Download & use" and "Reload". Does nothing during a session or while a preparation runs.
    /// Rethrows the loader's error after setting the status "Preparation failed".
    func prepare(_ model: TranscriptionModel) async throws {
        // STUB(WP-07)
    }

    func unload() {
        // STUB(WP-07)
    }

    /// Releases the loaded model.
    func providerChanged(to provider: SpeechProvider) {
        // STUB(WP-07)
    }

    /// Transcribers mirror their status line here.
    func reportStatus(_ line: String) {
        // STUB(WP-07)
    }

    /// Fixture wiring only (`-on-device-ready`).
    func installPreloaded(variant: String, voz: (any BatchRecognizing)?, parakeet: (any StreamingRecognizing)?) {
        // STUB(WP-07)
    }
}
