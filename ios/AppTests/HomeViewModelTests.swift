import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Home chip and rows for every combination.
final class HomeViewModelTests: XCTestCase {
    /// Not a key of any service.
    private let syntheticKey = "synthetic-unit-key"

    @MainActor
    private func makeModel(_ owners: TestOwners) -> HomeViewModel {
        HomeViewModel(
            session: owners.session,
            speech: owners.speech,
            assistant: owners.assistant,
            relay: owners.relay,
            models: owners.models,
            readiness: owners.readiness,
            history: owners.history
        )
    }

    private func status(_ state: String, model: String? = "nemotron", busy: Bool = false, elapsed: Int = 0) -> PCModelStatus {
        PCModelStatus(state: state, model: model, busy: busy, elapsedSeconds: elapsed, message: state)
    }

    // MARK: - First launch (AC-IOS-004)

    @MainActor
    func testAFreshInstallShowsWhatIsMissing() async {
        let owners = TestOwners(paired: false, metaKey: nil)
        let model = makeModel(owners)

        XCTAssertEqual(model.statusChip, StatusChipModel("PC offline", tone: .neutral, mark: .ring))
        XCTAssertEqual(model.readinessChip, StatusChipModel("Setup", tone: .warning, mark: .dot))
        XCTAssertTrue(model.showsFirstRunBanner)

        XCTAssertEqual(model.transcriptionValue, "Meta Muse · cloud")
        XCTAssertFalse(model.transcriptionValueNeedsAttention)
        XCTAssertEqual(model.transcriptionDetail, .meta(state: "Add API key", isReady: false))

        XCTAssertEqual(model.pairingTitle, "Pair Windows PC")
        XCTAssertEqual(model.pairingValue, "Pairing required")
        XCTAssertEqual(model.pairingSubtitle, "Required")
        XCTAssertTrue(model.pairingNeedsAttention)

        XCTAssertEqual(model.assistantSubtitle, "gpt-5.6-sol")
        XCTAssertFalse(model.assistantNeedsAttention)

        // Start is not gated on readiness.
        XCTAssertEqual(model.startControl, .start(isBusy: false, isEnabled: true, caption: nil))
        XCTAssertEqual(model.captionLine1, "Live captions with Meta Muse.")
        XCTAssertEqual(model.captionLine2, "Tap Assist for an answer from your PC.")
    }

    // MARK: - Status chip (AC-IOS-005, 006)

    @MainActor
    func testTheChipNamesTheDirectRouteWhenThePCIsNotNeeded() async {
        let expected: [(speech: SpeechProvider, text: String)] = [
            (.meta, "Cloud direct · no PC"),
            (.voz, "OpenRouter direct"),
            (.parakeet, "OpenRouter direct")
        ]
        for row in expected {
            for paired in [true, false] {
                let owners = TestOwners(paired: paired, provider: row.speech, answers: .openrouter)
                let model = makeModel(owners)
                let what = "\(row.speech.rawValue), paired \(paired)"

                XCTAssertEqual(model.statusChip, StatusChipModel(row.text, tone: .success, mark: .dot), what)
                XCTAssertEqual(model.pairingValue, "Not required", what)
                XCTAssertEqual(model.pairingSubtitle, "Optional for current setup", what)
                XCTAssertFalse(model.pairingNeedsAttention, what)
                XCTAssertEqual(model.pairingTitle, paired ? "PC connection" : "Pair Windows PC", what)
                XCTAssertEqual(model.captionLine2, "Tap Assist for an answer from OpenRouter.", what)
            }
        }
    }

    @MainActor
    func testTheChipFollowsThePCWhenItIsNeeded() async {
        let owners = TestOwners()
        let model = makeModel(owners)

        // Paired, and no check has answered yet.
        XCTAssertEqual(model.statusChip, StatusChipModel("PC reconnecting", tone: .warning, mark: .spinner))
        XCTAssertEqual(model.pairingTitle, "PC connection")
        XCTAssertEqual(model.pairingValue, "Paired")
        XCTAssertEqual(model.pairingSubtitle, "Saved · reconnecting")
        XCTAssertFalse(model.pairingNeedsAttention)

        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.statusChip, StatusChipModel("PC online", tone: .success, mark: .dot))
        XCTAssertEqual(model.pairingSubtitle, "Saved · connected")

        await owners.gateway.setHealth(.failure(RelayFailure.local(.unreachable)))
        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.statusChip, StatusChipModel("PC reconnecting", tone: .warning, mark: .spinner))
        XCTAssertEqual(model.pairingSubtitle, "Saved · reconnecting")

        // A PC speech provider needs the PC with either answer service.
        let unpaired = TestOwners(paired: false, provider: .nemotron, answers: .openrouter)
        XCTAssertEqual(makeModel(unpaired).statusChip, StatusChipModel("PC offline", tone: .neutral, mark: .ring))
    }

    // MARK: - Readiness (AC-IOS-007, 008)

    @MainActor
    func testReadinessNeedsSpeechAndAnswers() async {
        let owners = TestOwners()
        let model = makeModel(owners)
        XCTAssertEqual(model.transcriptionDetail, .meta(state: "Key saved · direct", isReady: true))
        XCTAssertEqual(model.readinessChip.text, "Setup", "Codex answers need the relay online.")

        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.readinessChip, StatusChipModel("Ready", tone: .success, mark: .check))
        XCTAssertFalse(model.showsFirstRunBanner)

        owners.speech.removeMetaKey()
        XCTAssertEqual(model.readinessChip, StatusChipModel("Setup", tone: .warning, mark: .dot))
        XCTAssertEqual(model.transcriptionDetail, .meta(state: "Add API key", isReady: false))

        XCTAssertTrue(owners.speech.saveMetaKey(syntheticKey))
        XCTAssertEqual(model.readinessChip.text, "Ready")
    }

    @MainActor
    func testTheFirstRunBannerNeedsAnEmptyHistory() async throws {
        let owners = TestOwners(paired: false, metaKey: nil)
        let model = makeModel(owners)
        XCTAssertTrue(model.showsFirstRunBanner)

        let earlier = Session(startedAt: Date(timeIntervalSince1970: 1_700_000_000), endedAt: Date(timeIntervalSince1970: 1_700_000_060))
        try await owners.sessions.save(earlier, savedAt: Date(timeIntervalSince1970: 1_700_000_060))
        await owners.history.reload()

        XCTAssertFalse(model.showsFirstRunBanner, "Someone who has had a conversation is not on a first run.")
        XCTAssertEqual(model.readinessChip.text, "Setup")
    }

    // MARK: - On-device providers (AC-IOS-009, 010)

    @MainActor
    func testAnOnDeviceProviderWithoutItsModelOffersTheDownload() async {
        for provider in [SpeechProvider.voz, .parakeet] {
            let owners = TestOwners(provider: provider, answers: .openrouter)
            let model = makeModel(owners)

            XCTAssertEqual(model.startControl, .download, provider.rawValue)
            XCTAssertEqual(model.transcriptionValue, "Not configured", provider.rawValue)
            XCTAssertTrue(model.transcriptionValueNeedsAttention, provider.rawValue)
            XCTAssertEqual(model.transcriptionDetail, .onDevice(text: "Download a model to start"), provider.rawValue)
            XCTAssertEqual(model.captionLine1, "Transcription on your iPhone.", provider.rawValue)
        }
    }

    @MainActor
    func testALoadedOnDeviceModelIsNamedAndCanStart() async {
        let owners = TestOwners(provider: .parakeet, answers: .openrouter)
        let model = makeModel(owners)
        owners.models.installPreloaded(variant: "parakeet", voz: nil, parakeet: FakeStreamingEngine())

        XCTAssertEqual(model.startControl, .start(isBusy: false, isEnabled: true, caption: nil))
        XCTAssertEqual(model.transcriptionValue, "Live Parakeet")
        XCTAssertFalse(model.transcriptionValueNeedsAttention)
        XCTAssertEqual(model.transcriptionDetail, .onDevice(text: "English live captions · 320 ms mode"))

        let voz = TestOwners(provider: .voz, answers: .openrouter)
        voz.models.installPreloaded(variant: "voz", voz: FakeBatchEngine(), parakeet: nil)
        XCTAssertEqual(makeModel(voz).transcriptionValue, "Voz on Assist")
        XCTAssertEqual(makeModel(voz).transcriptionDetail, .onDevice(text: "Transcribes when you tap Assist"))
    }

    @MainActor
    func testAppleSaysWhatItNeedsAndStartsOnceItsAssetIsInstalled() async {
        // iOS 18 to 25: the reason takes the place of the download hint.
        let older = makeModel(TestOwners(provider: .apple, answers: .openrouter))
        XCTAssertEqual(older.startControl, .download)
        XCTAssertEqual(older.transcriptionValue, "Not configured")
        XCTAssertTrue(older.transcriptionValueNeedsAttention)
        XCTAssertEqual(older.transcriptionDetail, .onDevice(text: "Requires iOS 26 or later"))
        XCTAssertEqual(older.captionLine1, "Transcription on your iPhone.")
        XCTAssertEqual(older.statusChip.text, "OpenRouter direct")

        // iOS 26, asset missing: the download is offered as for the other on-device providers.
        let engine = FakeSystemSpeech(answer: .needsDownload)
        let owners = TestOwners(provider: .apple, answers: .openrouter, systemSpeech: engine)
        let model = makeModel(owners)
        XCTAssertEqual(model.transcriptionDetail, .onDevice(text: "Checking Apple speech…"))
        await owners.models.refreshSystemSpeech()
        XCTAssertEqual(model.startControl, .download)
        XCTAssertEqual(model.transcriptionDetail, .onDevice(text: "Download a model to start"))

        // Installed: ready, with no manual step.
        await engine.setAnswer(.installed)
        await owners.models.refreshSystemSpeech()
        XCTAssertEqual(model.startControl, .start(isBusy: false, isEnabled: true, caption: nil))
        XCTAssertEqual(model.transcriptionValue, "Apple live captions")
        XCTAssertFalse(model.transcriptionValueNeedsAttention)
        XCTAssertEqual(model.transcriptionDetail, .onDevice(text: "English live captions · built into iOS"))
        XCTAssertTrue(owners.readiness.speechReady)
    }

    @MainActor
    func testAStreamingProviderNeverOffersTheDownload() async {
        for provider in [SpeechProvider.meta, .nemotron, .qwen3] {
            for answers in AssistantProvider.allCases {
                let model = makeModel(TestOwners(provider: provider, answers: answers))
                XCTAssertNotEqual(model.startControl, .download, "\(provider.rawValue) with \(answers.rawValue)")
                XCTAssertEqual(model.transcriptionValue, provider.displayName)
                XCTAssertFalse(model.transcriptionValueNeedsAttention)
            }
        }
    }

    // MARK: - PC speech providers (AC-IOS-011 to 014)

    @MainActor
    func testStartFollowsThePCSpeechModel() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        XCTAssertEqual(model.captionLine1, "Live captions on your PC's GPU.")

        // Paired, relay not reached yet.
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "PC offline", canStart: false, isChanging: false))
        XCTAssertEqual(
            model.startControl,
            .start(isBusy: false, isEnabled: false, caption: "PC speech model: PC offline. Open Transcription to start it.")
        )

        // Loading (AC-IOS-012).
        await owners.gateway.setStatus(.success(status("starting", elapsed: 3)))
        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "Loading · 3s", canStart: false, isChanging: true))
        XCTAssertEqual(
            model.startControl,
            .start(isBusy: false, isEnabled: false, caption: "PC speech model: Loading · 3s. Open Transcription to start it.")
        )

        // Ready, without any action on the phone.
        await owners.gateway.setStatus(.success(status("ready")))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "Ready", canStart: true, isChanging: false))
        XCTAssertEqual(model.startControl, .start(isBusy: false, isEnabled: true, caption: nil))
        XCTAssertEqual(model.readinessChip.text, "Ready")

        // In use by another stream (AC-IOS-011).
        await owners.gateway.setStatus(.success(status("ready", busy: true)))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "In use", canStart: false, isChanging: false))
        XCTAssertEqual(disabledCaption(of: model), "PC speech model: In use. Open Transcription to start it.")

        // The other model is loaded.
        await owners.gateway.setStatus(.success(status("ready", model: "qwen3")))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "Different model loaded", canStart: false, isChanging: false))

        // Stopped (AC-IOS-013).
        await owners.gateway.setStatus(.success(status("stopped", model: nil)))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "Stopped · open Settings", canStart: false, isChanging: false))
        XCTAssertNotNil(disabledCaption(of: model))

        // Stopping.
        await owners.gateway.setStatus(.success(status("stopping", elapsed: 1)))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "Stopping · 1s", canStart: false, isChanging: true))

        // A failed start (AC-IOS-014).
        await owners.gateway.setStatus(.success(status("error")))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "Error · open Settings", canStart: false, isChanging: false))
        XCTAssertNotNil(disabledCaption(of: model))
        XCTAssertEqual(model.readinessChip.text, "Setup")
    }

    @MainActor
    func testAPCSpeechProviderWithoutAPairingAsksToPairFirst() async {
        let model = makeModel(TestOwners(paired: false, provider: .qwen3))

        XCTAssertEqual(model.transcriptionValue, "Qwen3 1.7B · PC")
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "Pair PC first", canStart: false, isChanging: false))
        XCTAssertEqual(disabledCaption(of: model), "PC speech model: Pair PC first. Open Transcription to start it.")
        XCTAssertEqual(model.pairingValue, "Pairing required")
    }

    /// The caption of a Start control that is disabled; nil for any other control.
    @MainActor
    private func disabledCaption(of model: HomeViewModel) -> String? {
        guard case .start(_, let isEnabled, let caption) = model.startControl, !isEnabled else {
            return nil
        }
        return caption
    }

    // MARK: - Assistant row

    @MainActor
    func testTheAssistantRowNamesTheModelOrAsksForOne() async {
        let codex = makeModel(TestOwners())
        XCTAssertEqual(codex.assistantSubtitle, "gpt-5.6-sol")
        XCTAssertFalse(codex.assistantNeedsAttention)

        let owners = TestOwners(answers: .openrouter)
        let openRouter = makeModel(owners)
        XCTAssertEqual(openRouter.assistantSubtitle, "Choose an OpenRouter model")
        XCTAssertTrue(openRouter.assistantNeedsAttention)

        owners.assistant.adoptCatalogue(FixtureValues.openRouterCatalogue)
        owners.assistant.selectOpenRouterModel(FixtureValues.openRouterCatalogue[0])
        XCTAssertEqual(openRouter.assistantSubtitle, "test/direct")
        XCTAssertFalse(openRouter.assistantNeedsAttention)
    }

    // MARK: - Start

    @MainActor
    func testStartIsBusyWhileTheChecksRun() async throws {
        let owners = await TestOwners.ready(.nemotron)
        let model = makeModel(owners)
        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.startControl, .start(isBusy: false, isEnabled: true, caption: nil))

        // The start check asks the PC for its model again: hold that answer.
        let callsBefore = await owners.gateway.calls.count
        await owners.gateway.setHold(true, for: "localModelStatus")
        let starting = Task { await model.start() }
        await owners.gateway.waitForCalls(count: callsBefore + 1)

        XCTAssertEqual(owners.session.phase, .preflight)
        XCTAssertEqual(model.startControl, .start(isBusy: true, isEnabled: true, caption: nil))

        await owners.gateway.setHold(false, for: "localModelStatus")
        await owners.gateway.release("localModelStatus")
        await starting.value

        XCTAssertEqual(owners.session.phase, .listening, "Start on Home starts the session.")
        XCTAssertEqual(model.startControl, .start(isBusy: false, isEnabled: true, caption: nil))
        XCTAssertNil(owners.app.alertMessage)
    }

    @MainActor
    func testAFailedCheckLeavesHomeWithTheAlert() async {
        let owners = TestOwners(metaKey: nil)
        let model = makeModel(owners)

        await model.start()

        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertEqual(owners.app.alertMessage, "Add your Meta API key in Transcription. It stays in this iPhone's Keychain.")
        XCTAssertEqual(model.startControl, .start(isBusy: false, isEnabled: true, caption: nil))
    }

    // MARK: - PC model polling while Home is visible

    @MainActor
    func testHomeObservesThePCModelWhileItIsVisible() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        owners.relay.start()
        await owners.waitUntil("the first status arrived") { owners.relay.pcModelStatus?.state == "stopped" }

        await owners.gateway.setStatus(.success(status("ready")))
        model.appeared()
        await owners.waitUntil("Home polled the PC model") { owners.relay.pcModelStatus?.state == "ready" }
        XCTAssertEqual(model.transcriptionDetail, .pcModel(label: "Ready", canStart: true, isChanging: false))

        model.disappeared()
        await owners.gateway.setStatus(.success(status("stopped", model: nil)))
        await owners.clock.advance(by: RelayConstants.pcModelPollSeconds)
        try? await Task.sleep(nanoseconds: 50_000_000)
        XCTAssertEqual(owners.relay.pcModelStatus?.state, "ready", "Nothing polls the PC model once Home is gone.")
    }
}
