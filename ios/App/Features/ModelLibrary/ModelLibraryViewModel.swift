import Foundation
import LiveCueCore
import Observation

/// The progress block of the model that is being prepared.
struct PreparationBlock: Equatable {
    let fraction: Double
    /// "N% setup"
    let percentText: String
    /// "MM:SS elapsed"
    let elapsedText: String
    let statusLine: String
    let counterText: String?
    let failed: Bool
}

/// Model Library (screens 11).
@MainActor @Observable final class ModelLibraryViewModel {
    init(speech: SpeechSettings, models: OnDeviceModelManager, app: AppModel, clock: any AppClock) {
        // STUB(WP-11)
    }

    /// nil for a streaming provider.
    var model: TranscriptionModel? { nil } // STUB(WP-11)
    /// "~467 MB · Transcribes when you tap Assist"
    var descriptionText: String { "" } // STUB(WP-11)
    var isLoaded: Bool { false } // STUB(WP-11)
    /// "Download & use" / "Reload"
    var mainButtonTitle: String { "" } // STUB(WP-11)
    var isPreparing: Bool { false } // STUB(WP-11)
    /// Only for this model, while preparing or after a failure.
    var block: PreparationBlock? { nil } // STUB(WP-11)
    var statusLine: String { "" } // STUB(WP-11)
    var packageLabel: String { "" } // STUB(WP-11)
    var showsVozUsageSentence: Bool { false } // STUB(WP-11)

    /// On failure: `app.presentAlert(error.localizedDescription)`.
    func prepare() async {
        // STUB(WP-11)
    }

    func unload() {
        // STUB(WP-11)
    }

    /// Starts the 1 s elapsed refresh.
    func appeared() {
        // STUB(WP-11)
    }

    func disappeared() {
        // STUB(WP-11)
    }
}
