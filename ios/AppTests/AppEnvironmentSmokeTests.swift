import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// `AppEnvironment` builds in fixture mode and in live mode, and `bootstrap()` is idempotent.
final class AppEnvironmentSmokeTests: XCTestCase {
    // MARK: - Building

    @MainActor
    func testBuildsInFixtureMode() async {
        let launch = LaunchConfiguration(arguments: ["LiveCueRemake", "-ui-testing"], environment: [:])
        let environment = AppEnvironment.make(launch)

        XCTAssertEqual(environment.mode, .fixture(TestMode()))
        XCTAssertEqual(environment.app.selectedTab, .live)
        XCTAssertTrue(environment.app.livePath.isEmpty)
        XCTAssertNil(environment.app.alertMessage)
        XCTAssertFalse(environment.app.isAlertPresented)
        XCTAssertFalse(environment.activity.isActive)
        XCTAssertFalse(environment.activity.isAssisting)
        XCTAssertEqual(environment.session.phase, .idle)
        XCTAssertNil(environment.session.session)
        XCTAssertFalse(environment.session.showsLiveScreen)
        XCTAssertNil(environment.toasts.message)
    }

    @MainActor
    func testBuildsWithEveryModifier() async {
        let arguments = [
            "-on-device-ready", "-fluid-ready", "-meta-no-key", "-meta-key-persistence", "-openrouter-ui-reset",
            "-openrouter-persistence-test", "-pc-offline", "-pc-catalog-slow", "-pc-model-slow-start",
            "-pc-model-start-error", "-assist-fails-once", "-pairing-persistence-test",
            "-save-pairing-fixture", "-drop-test-keychain"
        ]
        for argument in arguments {
            let launch = LaunchConfiguration(arguments: ["LiveCueRemake", "-ui-testing", argument], environment: [:])
            let environment = AppEnvironment.make(launch)
            XCTAssertEqual(environment.mode, launch.mode, argument)
            XCTAssertFalse(environment.session.showsLiveScreen, argument)
        }
    }

    /// `-on-device-ready` starts with Parakeet loaded, `-fluid-ready` (D-29) with Fluid Speech Mini.
    @MainActor
    func testTheReadyModifiersStartWithTheirModelLoaded() async {
        func make(_ argument: String?) -> AppEnvironment {
            let arguments = ["LiveCueRemake", "-ui-testing"] + (argument.map { [$0] } ?? [])
            return AppEnvironment.make(LaunchConfiguration(arguments: arguments, environment: [:]))
        }

        let fluid = make("-fluid-ready")
        XCTAssertEqual(fluid.speech.provider, .fluid)
        XCTAssertTrue(fluid.models.isLoaded(for: .fluid))
        XCTAssertNotNil(fluid.models.fluidEngine)
        XCTAssertNil(fluid.models.parakeetEngine)
        XCTAssertTrue(fluid.readiness.speechReady)

        let parakeet = make("-on-device-ready")
        XCTAssertEqual(parakeet.speech.provider, .parakeet)
        XCTAssertTrue(parakeet.models.isLoaded(for: .parakeet))
        XCTAssertNil(parakeet.models.fluidEngine)

        // A base launch has nothing loaded, and leaves Meta selected for whatever runs next.
        let base = make(nil)
        XCTAssertEqual(base.speech.provider, .meta)
        XCTAssertNil(base.models.loadedVariant)
    }

    /// The launch path of the shipped app: it must build without a test seam, and building it must not
    /// start anything.
    @MainActor
    func testBuildsInLiveMode() async {
        let environment = AppEnvironment.make(LaunchConfiguration(arguments: ["LiveCueRemake"], environment: [:]))

        XCTAssertEqual(environment.mode, .live)
        XCTAssertEqual(environment.app.selectedTab, .live)
        XCTAssertEqual(environment.session.phase, .idle)
        XCTAssertFalse(environment.session.showsLiveScreen)
    }

    // MARK: - Bootstrap

    @MainActor
    func testBootstrapIsIdempotent() async {
        let store = CountingSessionStore()
        var dependencies = AppDependencies.fixture(TestMode())
        dependencies.sessions = store
        let environment = AppEnvironment(mode: .fixture(TestMode()), dependencies: dependencies)

        let before = await store.closeCalls
        XCTAssertEqual(before, 0, "Nothing reads the session store before bootstrap.")

        await environment.bootstrap()
        await environment.bootstrap()
        environment.sceneBecameActive()
        await environment.bootstrap()

        let after = await store.closeCalls
        XCTAssertEqual(after, 1, "Launch recovery runs once.")
        XCTAssertFalse(environment.session.showsLiveScreen)
        XCTAssertNil(environment.app.alertMessage)
    }

    // MARK: - Shell state

    @MainActor
    func testTheGlobalAlertShowsTheNewestMessageAndClearsWhenDismissed() async {
        let app = AppModel()
        XCTAssertFalse(app.isAlertPresented)

        app.presentAlert("First")
        app.presentAlert("Second")
        XCTAssertTrue(app.isAlertPresented)
        XCTAssertEqual(app.alertMessage, "Second")

        app.isAlertPresented = true
        XCTAssertEqual(app.alertMessage, "Second", "Presenting again keeps the message.")

        app.isAlertPresented = false
        XCTAssertNil(app.alertMessage)
        XCTAssertFalse(app.isAlertPresented)
    }

    @MainActor
    func testASessionStartAndEndEmptyEveryNavigationPath() async {
        let app = AppModel()
        app.selectedTab = .settings
        app.livePath = [.transcription]
        app.historyPath = [.sessionDetail(sessionID: UUID())]
        app.settingsPath = [.transcription, .metaKey]
        app.sessionPath = [.assistantLab]

        app.sessionDidStart()
        XCTAssertTrue(app.livePath.isEmpty)
        XCTAssertTrue(app.historyPath.isEmpty)
        XCTAssertTrue(app.settingsPath.isEmpty)
        XCTAssertTrue(app.sessionPath.isEmpty)
        XCTAssertEqual(app.selectedTab, .settings, "Starting does not change the tab.")

        app.sessionPath = [.assistantLab, .reasoning]
        app.settingsPath = [.pairing]
        app.sessionDidEnd()
        XCTAssertTrue(app.sessionPath.isEmpty)
        XCTAssertTrue(app.settingsPath.isEmpty)
        XCTAssertEqual(app.selectedTab, .live)
    }

    // MARK: - Fixtures and fakes the other packages build on

    @MainActor
    func testTheFixturePairingIsValid() async throws {
        let pairing = FixtureValues.pairing
        XCTAssertEqual(pairing.endpoint, "https://pc.example.test:11443/")
        XCTAssertEqual(pairing.token.count, RelayConstants.tokenLength)
        XCTAssertEqual(try PairingPayload.parse(pairing.storedText), pairing)
        XCTAssertEqual(FixturePairingStore().load(), pairing)
    }

    @MainActor
    func testFixtureCataloguesAreThoseOfTheTestSeam() async {
        XCTAssertEqual(
            FixtureValues.codexCatalogue.map(\.id),
            ["gpt-6-astra", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-luna", "gpt-5.3-codex-spark"]
        )
        for option in FixtureValues.codexCatalogue {
            let isFivePointSix = option.id.hasPrefix("gpt-5.6-")
            XCTAssertEqual(option.reasoningEfforts.contains("none"), isFivePointSix, option.id)
            XCTAssertTrue(option.reasoningEfforts.contains("low"), option.id)
        }
        XCTAssertEqual(FixtureValues.openRouterCatalogue.map(\.id), ["test/direct"])
        XCTAssertEqual(FixtureValues.openRouterCatalogue.first?.reasoningEfforts, ["default"])
        XCTAssertEqual(FixtureValues.segmentText, "What is the main advantage of local transcription?")
        XCTAssertTrue(FixtureValues.answer.hasPrefix(FixtureValues.partialAnswer))
    }

    @MainActor
    func testTheInMemoryStoreKeepsSessionsNewestFirst() async throws {
        let store = InMemorySessionStore()
        let older = Session(startedAt: Date(timeIntervalSince1970: 1_790_000_000))
        let newer = Session(startedAt: Date(timeIntervalSince1970: 1_790_000_600))
        let savedAt = Date(timeIntervalSince1970: 1_790_000_900)

        try await store.save(older, savedAt: savedAt)
        try await store.save(newer, savedAt: savedAt)
        let all = await store.loadAll()
        XCTAssertEqual(all.map(\.id), [newer.id, older.id])

        var renamed = older
        renamed.title = "Renamed"
        try await store.save(renamed, savedAt: savedAt)
        let afterUpdate = await store.loadAll()
        XCTAssertEqual(afterUpdate.count, 2)
        XCTAssertEqual(afterUpdate.last?.title, "Renamed")

        let hasNewer = await store.contains(id: newer.id)
        XCTAssertTrue(hasNewer)
        try await store.delete(id: newer.id)
        try await store.delete(id: newer.id)
        let stillHasNewer = await store.contains(id: newer.id)
        XCTAssertFalse(stillHasNewer)
        let afterDelete = await store.loadAll()
        XCTAssertEqual(afterDelete.map(\.id), [older.id])
    }

    @MainActor
    func testTheEventChannelCountsWhatItDelivered() async {
        let channel = TranscriberEventChannel()
        let generation = UUID()
        XCTAssertEqual(channel.emittedCount, 0)

        channel.emit(.status("Listening"), generation: generation)
        channel.emit(.partial("hello"), generation: generation)
        XCTAssertEqual(channel.emittedCount, 2)

        channel.finish()
        channel.emit(.partial("late"), generation: generation)
        XCTAssertEqual(channel.emittedCount, 2, "An event emitted after the end is not counted.")

        var received: [String] = []
        for await event in channel.stream {
            XCTAssertEqual(event.generation, generation)
            switch event.kind {
            case .status(let text), .partial(let text): received.append(text)
            default: XCTFail("Unexpected event.")
            }
        }
        XCTAssertEqual(received, ["Listening", "hello"])
    }

    @MainActor
    func testTestOwnersBuildOnFakes() async {
        let owners = TestOwners()
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertEqual(owners.pairingStore.load(), FixtureValues.pairing)
        XCTAssertEqual(owners.secrets.read(.metaAPIKey), "fixture-meta-key")
        XCTAssertNil(owners.secrets.read(.openRouterAPIKey))
        XCTAssertEqual(owners.preferences.speechProvider, .meta)
        XCTAssertEqual(owners.preferences.assistantConfiguration.provider, .codex)
        XCTAssertNotNil(owners.speech.onProviderChanged)
        XCTAssertNotNil(owners.speech.changeIsBlocked)
        XCTAssertNotNil(owners.assistant.onProviderChanged)
        XCTAssertNotNil(owners.history.willDelete)

        let unpaired = TestOwners(paired: false, provider: .parakeet, answers: .openrouter, metaKey: nil)
        XCTAssertNil(unpaired.pairingStore.load())
        XCTAssertNil(unpaired.secrets.read(.metaAPIKey))
        XCTAssertEqual(unpaired.preferences.speechProvider, .parakeet)
        XCTAssertEqual(unpaired.preferences.assistantConfiguration.provider, .openrouter)
    }

    @MainActor
    func testManualClockWakesSleepersInOrderAndMovesBothClocks() async throws {
        let clock = ManualClock()
        let start = clock.now()
        XCTAssertEqual(start, Date(timeIntervalSince1970: 1_790_000_000))
        XCTAssertEqual(clock.uptime(), 0)

        let woken = WakeLog()
        let late = Task { @MainActor in
            try await clock.sleep(seconds: 2)
            woken.names.append("late")
        }
        let early = Task { @MainActor in
            try await clock.sleep(seconds: 0.5)
            woken.names.append("early")
        }
        await clock.waitForSleepers(count: 2)

        await clock.advance(by: 1)
        try await early.value
        XCTAssertEqual(woken.names, ["early"])
        XCTAssertEqual(clock.uptime(), 1)
        XCTAssertEqual(clock.now().timeIntervalSince(start), 1, accuracy: 0.0005)

        await clock.advance(by: 1)
        try await late.value
        XCTAssertEqual(woken.names, ["early", "late"])
        XCTAssertEqual(clock.sleeperCount, 0)

        let cancelled = Task { try await clock.sleep(seconds: 60) }
        await clock.waitForSleepers()
        cancelled.cancel()
        do {
            try await cancelled.value
            XCTFail("A cancelled sleep throws.")
        } catch {
            XCTAssertTrue(error is CancellationError, "Unexpected error: \(error)")
        }
    }
}

/// The order in which tasks woke.
@MainActor private final class WakeLog {
    var names: [String] = []
}

/// A session store that counts how often launch recovery ran.
private actor CountingSessionStore: SessionStoring {
    private(set) var closeCalls = 0
    private(set) var loadCalls = 0

    func loadAll() async -> [Session] {
        loadCalls += 1
        return []
    }

    func save(_ session: Session, savedAt: Date) async throws {}

    func delete(id: UUID) async throws {}

    func contains(id: UUID) async -> Bool {
        false
    }

    @discardableResult
    func closeInterruptedSessions() async -> Int {
        closeCalls += 1
        return 0
    }
}
