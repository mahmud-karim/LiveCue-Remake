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
///
/// Apple (D-28) is different: its recogniser is part of iOS and its language asset belongs to the system.
/// There is nothing to load or to release. The manager keeps what the system last said about it, and
/// `prepare` asks the system to install the asset.
@MainActor @Observable final class OnDeviceModelManager {
    /// Never restored at launch (IOS-B1).
    private(set) var loadedVariant: String? = nil
    /// nil once its model is ready (IOS-B13).
    private(set) var preparation: ModelPreparation? = nil
    private(set) var statusLine: String = "Prepare a model"

    /// At most one of the three is set.
    private var voz: (any BatchRecognizing)? = nil
    private var parakeet: (any StreamingRecognizing)? = nil
    /// Fluid Speech Mini behind its rolling recogniser (D-29).
    private var fluid: (any StreamingRecognizing)? = nil

    /// What the system last said about its own recogniser. `.unknown` until it was asked.
    private(set) var systemSpeechAvailability: SystemSpeechAvailability
    private let systemSpeech: any SystemSpeechRecognizing
    @ObservationIgnored private var isAskingSystemSpeech = false

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

    var fluidEngine: (any StreamingRecognizing)? {
        fluid
    }

    /// The system recogniser, once its language asset is installed.
    var systemSpeechEngine: (any SystemSpeechRecognizing)? {
        systemSpeechAvailability == .installed ? systemSpeech : nil
    }

    /// Why Apple speech cannot start yet, when that is not simply a missing download: the system is
    /// still being asked, or it cannot be used on this iPhone.
    var systemSpeechNote: String? {
        switch systemSpeechAvailability {
        case .unknown:
            return SystemSpeechText.checking
        case .unavailable(let reason):
            return reason
        case .needsDownload, .installed:
            return nil
        }
    }

    /// Without a system recogniser the manager behaves as on iOS 18: Apple speech is unavailable.
    init(
        loader: any OnDeviceEngineLoading,
        activity: SessionActivity,
        clock: any AppClock,
        systemSpeech: any SystemSpeechRecognizing = UnavailableSystemSpeech()
    ) {
        self.loader = loader
        self.activity = activity
        self.clock = clock
        self.systemSpeech = systemSpeech
        // Known without asking: unavailable on an older iOS, installed in the test seam.
        systemSpeechAvailability = systemSpeech.knownAvailability
    }

    func isLoaded(for provider: SpeechProvider) -> Bool {
        switch provider {
        case .voz:
            return voz != nil && loadedVariant == TranscriptionModel.voz.variant
        case .parakeet:
            return parakeet != nil && loadedVariant == TranscriptionModel.parakeet.variant
        case .fluid:
            return fluid != nil && loadedVariant == TranscriptionModel.fluid.variant
        case .apple:
            return systemSpeechAvailability == .installed
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

        if provider == .apple {
            try await installSystemSpeech(model)
            return
        }

        // One recogniser in memory at a time: whatever is loaded is released first. That includes this
        // model when the button read "Reload"; its files stay cached, so it loads again from the cache.
        let released = takeLoaded()

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
        var loadedStreaming: (any StreamingRecognizing)?
        do {
            if provider == .voz {
                loadedVoz = try await loader.loadVoz(progress: report)
            } else if provider == .fluid {
                loadedStreaming = try await loader.loadFluid(progress: report)
            } else {
                loadedStreaming = try await loader.loadParakeet(progress: report)
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
            await loadedStreaming?.cleanup()
            return
        }
        runningPreparation = nil
        voz = loadedVoz
        if provider == .fluid {
            fluid = loadedStreaming
        } else {
            parakeet = loadedStreaming
        }
        loadedVariant = model.variant
        statusLine = OnDeviceModelManager.readyStatus
        preparation = nil
    }

    /// Releases the loaded recogniser. Deletes no file. Does nothing during a session: the transcriber
    /// of a running session is using the recogniser.
    func unload() {
        guard !activity.isActive else { return }
        release()
    }

    /// Releases the loaded model. For Apple the status line says what the system last said, and the
    /// system is asked when it has not been yet.
    func providerChanged(to provider: SpeechProvider) {
        release()
        guard provider == .apple else { return }
        statusLine = OnDeviceModelManager.status(for: systemSpeechAvailability)
        if systemSpeechAvailability == .unknown {
            Task { await self.refreshSystemSpeech() }
        }
    }

    /// Asks the system whether its recogniser can be used and whether the language asset is installed.
    /// It shows no prompt and downloads nothing. Does nothing during a session, while a preparation runs
    /// or while an earlier question is open. `updatingStatus` is false when Apple is not the selected
    /// provider, so the status line stays the selected provider's.
    func refreshSystemSpeech(updatingStatus: Bool = true) async {
        guard !activity.isActive, !isPreparing, !isAskingSystemSpeech else { return }
        isAskingSystemSpeech = true
        let answer = await systemSpeech.availability()
        isAskingSystemSpeech = false
        guard !activity.isActive, !isPreparing else { return }
        systemSpeechAvailability = answer
        if updatingStatus {
            statusLine = OnDeviceModelManager.status(for: answer)
        }
    }

    /// Transcribers mirror their status line here.
    func reportStatus(_ line: String) {
        statusLine = line
    }

    /// Fixture wiring only (`-on-device-ready`, `-fluid-ready`).
    func installPreloaded(
        variant: String,
        voz: (any BatchRecognizing)?,
        parakeet: (any StreamingRecognizing)?,
        fluid: (any StreamingRecognizing)? = nil
    ) {
        runningPreparation = nil
        preparation = nil
        self.voz = voz
        self.parakeet = parakeet
        self.fluid = fluid
        loadedVariant = variant
        statusLine = OnDeviceModelManager.readyStatus
    }

    // MARK: - Apple

    /// "Download & use" and "Check again" for Apple: the system installs the language asset when it is
    /// missing, then says whether it is there. Nothing is loaded into the app.
    private func installSystemSpeech(_ model: TranscriptionModel) async throws {
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

        let (reports, reporter) = AsyncStream<ModelPreparationProgress>.makeStream(bufferingPolicy: .bufferingNewest(32))
        let report: @Sendable (ModelPreparationProgress) -> Void = { progress in
            _ = reporter.yield(progress)
        }
        Task { [weak self] in
            for await progress in reports {
                self?.apply(progress, to: id, .apple)
            }
        }

        let failure = await OnDeviceModelManager.installFailure(of: systemSpeech, report: report)
        reporter.finish()
        // Abandoned by a change of provider: nobody waits for this outcome.
        guard runningPreparation == id else { return }
        let answer = await systemSpeech.availability()
        guard runningPreparation == id else { return }

        runningPreparation = nil
        systemSpeechAvailability = answer
        if failure == nil, answer == .installed {
            statusLine = OnDeviceModelManager.readyStatus
            preparation = nil
            return
        }
        preparation?.failed = true
        preparation?.finishedAt = clock.now()
        statusLine = OnDeviceModelManager.failedStatus
        if let failure {
            throw failure
        }
        if case .unavailable(let reason) = answer {
            throw SystemSpeechFailure(reason)
        }
        throw SystemSpeechFailure(SystemSpeechText.notInstalledYet)
    }

    /// The error of an installation, or nil when it went through.
    private static func installFailure(
        of systemSpeech: any SystemSpeechRecognizing,
        report: @escaping @Sendable (ModelPreparationProgress) -> Void
    ) async -> (any Error)? {
        do {
            try await systemSpeech.install(progress: report)
            return nil
        } catch {
            return error
        }
    }

    /// The status line for what the system said about its recogniser.
    private static func status(for availability: SystemSpeechAvailability) -> String {
        switch availability {
        case .unknown:
            return SystemSpeechText.checking
        case .unavailable(let reason):
            return reason
        case .needsDownload:
            return idleStatus
        case .installed:
            return readyStatus
        }
    }

    // MARK: - Pieces

    private func release() {
        let released = takeLoaded()
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

    /// Forgets whatever is loaded and returns the recogniser that has something to clean up. Voz has
    /// nothing of the kind.
    private func takeLoaded() -> (any StreamingRecognizing)? {
        let streaming = parakeet ?? fluid
        voz = nil
        parakeet = nil
        fluid = nil
        loadedVariant = nil
        return streaming
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
        if provider == .apple {
            switch progress.phase {
            case .checking:
                return SystemSpeechText.checking
            case .downloading:
                return "Downloading the English speech asset…"
            case .preparing:
                return "Download complete · finishing…"
            }
        }
        switch progress.phase {
        case .checking:
            return "Checking model files…"
        case .downloading:
            return provider == .fluid ? "Downloading Fluid Speech Mini…" : "Downloading Parakeet…"
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
        if variant == TranscriptionModel.apple.variant {
            return .apple
        }
        if variant == TranscriptionModel.fluid.variant {
            return .fluid
        }
        return nil
    }

    static let idleStatus = "Prepare a model"
    static let readyStatus = "Ready"
    static let failedStatus = "Preparation failed"
}
