import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The provider list and the selected provider's rows (AC-IOS-095 to 097).
final class TranscriptionViewModelTests: XCTestCase {
    @MainActor
    private func makeModel(_ owners: TestOwners) -> TranscriptionViewModel {
        TranscriptionViewModel(
            speech: owners.speech,
            assistant: owners.assistant,
            relay: owners.relay,
            models: owners.models,
            readiness: owners.readiness
        )
    }

    @MainActor
    func testTheFiveProvidersAreListedInCatalogueOrder() async {
        let model = makeModel(TestOwners())

        XCTAssertEqual(model.providers, [.meta, .nemotron, .qwen3, .voz, .parakeet])
        XCTAssertEqual(
            model.providers.map(\.pickerTitle),
            [
                "Meta Muse · live cloud",
                "Nemotron 3.5 · PC live",
                "Qwen3 1.7B · PC live",
                "Voz · iPhone on Assist",
                "Parakeet · iPhone live"
            ]
        )
        XCTAssertEqual(model.selected, .meta)
        XCTAssertEqual(model.selectedTitle, "Meta Muse · cloud")
    }

    @MainActor
    func testSelectingAProviderShowsWhatItNeeds() async {
        let owners = TestOwners()
        let model = makeModel(owners)

        // Meta: the key row, and nothing else.
        XCTAssertEqual(model.setup.rows, [.metaKey(value: "Saved on iPhone", needsAttention: false)])

        // PC-local: the PC speech model row, and nothing else.
        for provider in [SpeechProvider.nemotron, .qwen3] {
            model.select(provider)
            XCTAssertEqual(model.selected, provider)
            XCTAssertEqual(owners.preferences.speechProvider, provider, "The choice is saved.")
            XCTAssertEqual(model.selectedTitle, provider.displayName)
            XCTAssertEqual(model.setup.rows, [.pcSpeechModel(value: owners.relay.homeLabel(for: provider))])
            XCTAssertTrue(model.explanation.hasPrefix("Audio streams to the model on your PC's GPU."))
        }

        // On-device: Model Library and Speech benchmark, and nothing else.
        for provider in [SpeechProvider.voz, .parakeet] {
            model.select(provider)
            XCTAssertEqual(model.selected, provider)
            XCTAssertEqual(model.selectedTitle, provider.displayName)
            XCTAssertEqual(model.setup.rows, [.modelLibrary(value: "Not loaded"), .benchmark])
            XCTAssertEqual(model.explanation, "Audio is transcribed on this iPhone. Only text is sent to your PC.")
        }

        model.select(.meta)
        XCTAssertEqual(model.setup.rows, [.metaKey(value: "Saved on iPhone", needsAttention: false)])
        XCTAssertTrue(model.explanation.hasPrefix("Audio streams directly from this iPhone to Meta."))
    }

    @MainActor
    func testTheMetaRowSaysWhenTheKeyIsMissing() async {
        let model = makeModel(TestOwners(metaKey: nil))

        XCTAssertEqual(model.setup.rows, [.metaKey(value: "Required", needsAttention: true)])
    }

    @MainActor
    func testALoadedModelReadsReady() async throws {
        let owners = TestOwners(provider: .voz)
        let model = makeModel(owners)
        XCTAssertEqual(model.setup.rows, [.modelLibrary(value: "Not loaded"), .benchmark])

        try await owners.models.prepare(.voz)
        XCTAssertEqual(model.setup.rows, [.modelLibrary(value: "Ready"), .benchmark])
    }

    @MainActor
    func testThePCModelRowCarriesTheHomeLabel() async {
        let owners = TestOwners(provider: .qwen3)
        let model = makeModel(owners)
        XCTAssertEqual(model.setup.rows, [.pcSpeechModel(value: "PC offline")])

        let ready = PCModelStatus(state: "ready", model: "qwen3", message: "Ready")
        await owners.gateway.setStatus(.success(ready))
        await owners.relay.checkHealthNow()
        XCTAssertEqual(model.setup.rows, [.pcSpeechModel(value: "Ready")])
    }

    // MARK: - The lock

    @MainActor
    func testTheProvidersAreLockedWhileThePCModelChanges() async {
        let owners = TestOwners(provider: .nemotron)
        let model = makeModel(owners)
        XCTAssertNil(model.lockCaption)

        let stopping = PCModelStatus(state: "stopping", model: "nemotron", elapsedSeconds: 1, message: "Stopping")
        await owners.gateway.setStatus(.success(stopping))
        await owners.relay.refreshPCModel()
        XCTAssertEqual(model.lockCaption, "Available when the PC model has finished changing.")

        // The owner refuses the change, so the selection stays.
        model.select(.meta)
        XCTAssertEqual(model.selected, .nemotron)

        let stopped = PCModelStatus(state: "stopped", message: "Stopped")
        await owners.gateway.setStatus(.success(stopped))
        await owners.relay.refreshPCModel()
        XCTAssertNil(model.lockCaption)
        model.select(.meta)
        XCTAssertEqual(model.selected, .meta)
    }

    @MainActor
    func testTheProvidersAreLockedWhileAModelIsBeingPrepared() async throws {
        let gate = FakeGate()
        let owners = TestOwners(provider: .parakeet)
        let models = OnDeviceModelManager(
            loader: FakeEngineLoader(voz: owners.vozEngine, parakeet: owners.parakeetEngine, gate: gate),
            activity: owners.activity,
            clock: owners.clock
        )
        let readiness = SystemReadiness(speech: owners.speech, assistant: owners.assistant, relay: owners.relay, models: models)
        let model = TranscriptionViewModel(
            speech: owners.speech,
            assistant: owners.assistant,
            relay: owners.relay,
            models: models,
            readiness: readiness
        )
        XCTAssertNil(model.lockCaption)

        let preparing = Task { try await models.prepare(.parakeet) }
        await SpeechTest.waitUntil { models.isPreparing }
        XCTAssertEqual(model.lockCaption, "Available when the model has finished preparing.")

        await gate.open()
        try await preparing.value
        XCTAssertNil(model.lockCaption)
        XCTAssertEqual(model.setup.rows, [.modelLibrary(value: "Ready"), .benchmark])
    }
}
