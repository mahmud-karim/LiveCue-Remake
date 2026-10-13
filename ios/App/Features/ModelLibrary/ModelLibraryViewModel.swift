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

/// Model Library (screens 11): download and load the on-device model of the current provider.
///
/// Loading, progress and the status line are the model manager's. This class picks what belongs to the
/// model on screen and words it.
@MainActor @Observable final class ModelLibraryViewModel {
    private let speech: SpeechSettings
    private let models: OnDeviceModelManager
    private let app: AppModel
    private let clock: any AppClock
    /// Counts the seconds seen while the screen is visible, so the elapsed time is read again.
    private var refreshCount = 0
    @ObservationIgnored private var refreshTask: Task<Void, Never>?

    init(speech: SpeechSettings, models: OnDeviceModelManager, app: AppModel, clock: any AppClock) {
        self.speech = speech
        self.models = models
        self.app = app
        self.clock = clock
    }

    /// nil for a streaming provider.
    var model: TranscriptionModel? {
        TranscriptionModel.onDevice(for: speech.provider)
    }

    /// "~467 MB · Transcribes when you tap Assist"
    var descriptionText: String {
        guard let model else { return "" }
        return "~\(model.approximateMegabytes) MB · \(model.quality)"
    }

    var isLoaded: Bool {
        models.isLoaded(for: speech.provider)
    }

    /// "Download & use" / "Reload"
    var mainButtonTitle: String {
        isLoaded ? "Reload" : "Download & use"
    }

    var isPreparing: Bool {
        models.isPreparing
    }

    /// Only for this model, while preparing or after a failure.
    var block: PreparationBlock? {
        guard let model, let preparation = models.preparation, preparation.variant == model.variant else {
            return nil
        }
        // Read so that the one-second refresh makes a view ask again while the time is running.
        _ = refreshCount

        let end = preparation.finishedAt ?? clock.now()
        let seconds = max(0, Int(end.timeIntervalSince(preparation.startedAt).rounded(.down)))
        let fraction = min(max(preparation.fraction, 0), 1)
        let percent = Int((fraction * 100).rounded())
        return PreparationBlock(
            fraction: fraction,
            percentText: "\(percent)% setup",
            elapsedText: ModelLibraryViewModel.elapsedText(seconds),
            statusLine: models.statusLine,
            counterText: preparation.counterText,
            failed: preparation.failed
        )
    }

    var statusLine: String {
        models.statusLine
    }

    var packageLabel: String {
        speech.provider.packageLabel ?? ""
    }

    var showsVozUsageSentence: Bool {
        speech.provider == .voz
    }

    /// On failure: `app.presentAlert(error.localizedDescription)`.
    func prepare() async {
        guard let model else { return }
        do {
            try await models.prepare(model)
        } catch is CancellationError {
            // Never shown.
        } catch {
            app.presentAlert(error.localizedDescription)
        }
    }

    func unload() {
        models.unload()
    }

    /// Starts the 1 s elapsed refresh.
    func appeared() {
        guard refreshTask == nil else { return }
        refreshTask = Task { [weak self, clock] in
            while self != nil {
                do {
                    try await clock.sleep(seconds: 1)
                } catch {
                    return
                }
                self?.secondPassed()
            }
        }
    }

    func disappeared() {
        refreshTask?.cancel()
        refreshTask = nil
    }

    // MARK: - Pieces

    /// Only a running preparation has a time that moves.
    private func secondPassed() {
        if models.isPreparing {
            refreshCount += 1
        }
    }

    /// "MM:SS elapsed", with "." and digits that do not follow the device's locale.
    private static func elapsedText(_ seconds: Int) -> String {
        String(format: "%02d:%02d elapsed", locale: Locale(identifier: "en_US_POSIX"), seconds / 60, seconds % 60)
    }
}
