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
///
/// Nothing is restored at launch (IOS-B1): a model is loaded when the user asks for it, and released when
/// the provider changes. The downloaded files stay cached; this class deletes none.
@MainActor @Observable final class OnDeviceModelManager {
    /// Never restored at launch (IOS-B1).
    private(set) var loadedVariant: String? = nil
    /// nil once its model is ready (IOS-B13).
    private(set) var preparation: ModelPreparation? = nil
    private(set) var statusLine: String = "Prepare a model"

    /// At most one of the two is set.
    private var voz: (any BatchRecognizing)? = nil
    private var parakeet: (any StreamingRecognizing)? = nil

    private let loader: any OnDeviceEngineLoading
    private let activity: SessionActivity
    private let clock: any AppClock
    /// Names the preparation in flight. Progress and results that belong to another one are dropped.
    @ObservationIgnored private var runningPreparation: UUID? = nil

    /// A preparation runs: its block has no end time yet.
    var isPreparing: Bool {
        guard let preparation else { return false }
        return preparation.finishedAt == nil
    }

    var vozEngine: (any BatchRecognizing)? {
        voz
    }

    var parakeetEngine: (any StreamingRecognizing)? {
        parakeet
    }

    init(loader: any OnDeviceEngineLoading, activity: SessionActivity, clock: any AppClock) {
        self.loader = loader
        self.activity = activity
        self.clock = clock
    }

    func isLoaded(for provider: SpeechProvider) -> Bool {
        switch provider {
        case .voz:
            return voz != nil && loadedVariant == TranscriptionModel.voz.variant
        case .parakeet:
            return parakeet != nil && loadedVariant == TranscriptionModel.parakeet.variant
        case .meta, .nemotron, .qwen3:
            return false
        }
    }

    func loadedModel(for provider: SpeechProvider) -> TranscriptionModel? {
        isLoaded(for: provider) ? TranscriptionModel.onDevice(for: provider) : nil
    }

    /// "Download & use" and "Reload". Does nothing during a session or while a preparation runs.
    /// Rethrows the loader's error after setting the status "Preparation failed".
    func prepare(_ model: TranscriptionModel) async throws {
        guard !activity.isActive, !isPreparing,
              let provider = OnDeviceModelManager.provider(for: model.variant) else {
            return
        }

        // One recogniser in memory at a time: whatever is loaded is released first. That includes this
        // model when the button read "Reload"; its files stay cached, so it loads again from the cache.
        let released = parakeet
        voz = nil
        parakeet = nil
        loadedVariant = nil

        let id = UUID()
        runningPreparation = id
        preparation = ModelPreparation(
            variant: model.variant,
            fraction: 0,
            startedAt: clock.now(),
            finishedAt: nil,
            counterText: nil,
            failed: false
        )
        statusLine = "Downloading and preparing \(model.displayName)…"

        await released?.cleanup()

        // The loader reports off the main actor. Its reports pass through a stream, so they are applied
        // here in the order they were made.
        let (reports, reporter) = AsyncStream<ModelPreparationProgress>.makeStream(bufferingPolicy: .bufferingNewest(32))
        let report: @Sendable (ModelPreparationProgress) -> Void = { progress in
            _ = reporter.yield(progress)
        }
        Task { [weak self] in
            for await progress in reports {
                self?.apply(progress, to: id, provider)
            }
        }

        var loadedVoz: (any BatchRecognizing)?
        var loadedParakeet: (any StreamingRecognizing)?
        do {
            if provider == .voz {
                loadedVoz = try await loader.loadVoz(progress: report)
            } else {
                loadedParakeet = try await loader.loadParakeet(progress: report)
            }
        } catch {
            reporter.finish()
            // Abandoned by an unload: nobody waits for this outcome.
            guard runningPreparation == id else { return }
            runningPreparation = nil
            preparation?.failed = true
            preparation?.finishedAt = clock.now()
            statusLine = OnDeviceModelManager.failedStatus
            throw error
        }
        reporter.finish()

        guard runningPreparation == id else {
            // Unloaded while it was loading: the recogniser is released again.
            await loadedParakeet?.cleanup()
            return
        }
        runningPreparation = nil
        voz = loadedVoz
        parakeet = loadedParakeet
        loadedVariant = model.variant
        statusLine = OnDeviceModelManager.readyStatus
        preparation = nil
    }

    /// Releases both recognisers. Deletes no file. Does nothing during a session: the transcriber of a
    /// running session is using the recogniser.
    func unload() {
        guard !activity.isActive else { return }
        release()
    }

    /// Releases the loaded model.
    func providerChanged(to provider: SpeechProvider) {
        release()
    }

    /// Transcribers mirror their status line here.
    func reportStatus(_ line: String) {
        statusLine = line
    }

    /// Fixture wiring only (`-on-device-ready`).
    func installPreloaded(variant: String, voz: (any BatchRecognizing)?, parakeet: (any StreamingRecognizing)?) {
        runningPreparation = nil
        preparation = nil
        self.voz = voz
        self.parakeet = parakeet
        loadedVariant = variant
        statusLine = OnDeviceModelManager.readyStatus
    }

    // MARK: - Pieces

    private func release() {
        let released = parakeet
        voz = nil
        parakeet = nil
        loadedVariant = nil
        if runningPreparation != nil {
            // A preparation in flight is abandoned; its result is released when it arrives.
            runningPreparation = nil
            preparation = nil
        }
        statusLine = OnDeviceModelManager.idleStatus
        if let released {
            Task { await released.cleanup() }
        }
    }

    /// One report of the loader, for the preparation it belongs to.
    private func apply(_ progress: ModelPreparationProgress, to id: UUID, _ provider: SpeechProvider) {
        guard runningPreparation == id, var block = preparation else { return }
        block.fraction = progress.fraction
        switch progress.counter {
        case .bytes(let completed, let total):
            block.counterText = "\(OnDeviceModelManager.fileSize(completed)) / \(OnDeviceModelManager.fileSize(total))"
        case .files(let completed, let total):
            block.counterText = "\(completed) / \(total) files"
        case .none:
            // No count yet while the files are being checked; later the last count stays.
            if progress.phase == .checking {
                block.counterText = nil
            }
        }
        preparation = block
        statusLine = OnDeviceModelManager.status(for: progress, provider)
    }

    private static func status(for progress: ModelPreparationProgress, _ provider: SpeechProvider) -> String {
        if provider == .voz {
            // Voz reports bytes only: it is downloading until the fraction reaches 1.
            return progress.fraction < 1 ? "Downloading Voz…" : "Download complete · preparing Neural Engine…"
        }
        switch progress.phase {
        case .checking:
            return "Checking model files…"
        case .downloading:
            return "Downloading Parakeet…"
        case .preparing:
            return "Download complete · preparing model…"
        }
    }

    private static func fileSize(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: max(0, bytes), countStyle: .file)
    }

    /// The on-device provider a catalogue entry belongs to; nil for anything else.
    private static func provider(for variant: String) -> SpeechProvider? {
        if variant == TranscriptionModel.voz.variant {
            return .voz
        }
        if variant == TranscriptionModel.parakeet.variant {
            return .parakeet
        }
        return nil
    }

    static let idleStatus = "Prepare a model"
    static let readyStatus = "Ready"
    static let failedStatus = "Preparation failed"
}
