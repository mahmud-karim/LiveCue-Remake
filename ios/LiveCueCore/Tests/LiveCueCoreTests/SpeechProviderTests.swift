import Foundation
import LiveCueCore
import XCTest

/// Provider flags, the stored default and `PCRequirement.needsPC`.
final class SpeechProviderTests: XCTestCase {
    // MARK: - Old tests (spec 02 sections 10.3 and 10.7, read against `isPCLocal`: CORE-C5)

    /// AC-CORE-024.
    func testMetaIsCloudAndThePCModelsUseThePC() {
        XCTAssertFalse(SpeechProvider.meta.isPCLocal)
        XCTAssertTrue(SpeechProvider.meta.isCloud)
        XCTAssertTrue(SpeechProvider.meta.streamsAudio)

        XCTAssertTrue(SpeechProvider.nemotron.isPCLocal)
        XCTAssertTrue(SpeechProvider.qwen3.isPCLocal)

        XCTAssertFalse(SpeechProvider.voz.streamsAudio)
        XCTAssertFalse(SpeechProvider.parakeet.streamsAudio)
        XCTAssertFalse(SpeechProvider.apple.streamsAudio)
        XCTAssertFalse(SpeechProvider.fluid.streamsAudio)
    }

    /// AC-CORE-081.
    func testThePCModelsArePCLocalAtSixteenKilohertz() {
        for provider in [SpeechProvider.nemotron, SpeechProvider.qwen3] {
            XCTAssertTrue(provider.isPCLocal, provider.rawValue)
            XCTAssertEqual(provider.sampleRate, 16_000, provider.rawValue)
            XCTAssertFalse(provider.isCloud, provider.rawValue)
            XCTAssertTrue(provider.streamsAudio, provider.rawValue)
            XCTAssertFalse(provider.isOnDevice, provider.rawValue)
        }
    }

    /// AC-CORE-084.
    func testMetaIsTwentyFourKilohertzAndVozDoesNotUseThePC() {
        XCTAssertEqual(SpeechProvider.meta.sampleRate, 24_000)
        XCTAssertFalse(SpeechProvider.voz.isPCLocal)
    }

    // MARK: - The catalogue (spec 02 section 7.1)

    func testCatalogueOrderAndRawValues() {
        XCTAssertEqual(SpeechProvider.allCases, [.meta, .nemotron, .qwen3, .voz, .parakeet, .apple, .fluid])
        XCTAssertEqual(
            SpeechProvider.allCases.map(\.rawValue),
            ["meta", "nemotron", "qwen3", "voz", "parakeet", "apple", "fluid"]
        )
        XCTAssertEqual(RelayConstants.speechModels, [SpeechProvider.nemotron.rawValue, SpeechProvider.qwen3.rawValue])
    }

    func testEveryRowOfTheProviderTable() {
        struct Row {
            let provider: SpeechProvider
            let name: String
            let isCloud: Bool
            let isPCLocal: Bool
            let isOnDevice: Bool
            let streamsAudio: Bool
            let sampleRate: Int
        }
        let rows = [
            Row(provider: .meta, name: "Meta Muse \u{00B7} cloud", isCloud: true, isPCLocal: false, isOnDevice: false, streamsAudio: true, sampleRate: 24_000),
            Row(provider: .nemotron, name: "Nemotron 3.5 \u{00B7} PC", isCloud: false, isPCLocal: true, isOnDevice: false, streamsAudio: true, sampleRate: 16_000),
            Row(provider: .qwen3, name: "Qwen3 1.7B \u{00B7} PC", isCloud: false, isPCLocal: true, isOnDevice: false, streamsAudio: true, sampleRate: 16_000),
            Row(provider: .voz, name: "Voz \u{00B7} iPhone", isCloud: false, isPCLocal: false, isOnDevice: true, streamsAudio: false, sampleRate: 16_000),
            Row(provider: .parakeet, name: "Parakeet \u{00B7} iPhone", isCloud: false, isPCLocal: false, isOnDevice: true, streamsAudio: false, sampleRate: 16_000),
            Row(provider: .apple, name: "Apple \u{00B7} iPhone", isCloud: false, isPCLocal: false, isOnDevice: true, streamsAudio: false, sampleRate: 16_000),
            Row(provider: .fluid, name: "Fluid Speech Mini \u{00B7} iPhone", isCloud: false, isPCLocal: false, isOnDevice: true, streamsAudio: false, sampleRate: 16_000)
        ]
        XCTAssertEqual(rows.map(\.provider), SpeechProvider.allCases)
        for row in rows {
            let provider = row.provider
            XCTAssertEqual(provider.displayName, row.name)
            XCTAssertEqual(provider.isCloud, row.isCloud, provider.rawValue)
            XCTAssertEqual(provider.isPCLocal, row.isPCLocal, provider.rawValue)
            XCTAssertEqual(provider.isOnDevice, row.isOnDevice, provider.rawValue)
            XCTAssertEqual(provider.streamsAudio, row.streamsAudio, provider.rawValue)
            XCTAssertEqual(provider.sampleRate, row.sampleRate, provider.rawValue)
        }
    }

    /// D-28: the recogniser built into iOS is the sixth provider.
    func testAppleIsOnDeviceAndNeedsNeitherCloudNorPC() {
        let apple = SpeechProvider.apple
        XCTAssertEqual(apple.rawValue, "apple")
        XCTAssertEqual(SpeechProvider(rawValue: "apple"), .apple)
        XCTAssertEqual(SpeechProvider.allCases.firstIndex(of: .apple), 5)
        XCTAssertEqual(apple.displayName, "Apple \u{00B7} iPhone")
        XCTAssertTrue(apple.isOnDevice)
        XCTAssertFalse(apple.isCloud)
        XCTAssertFalse(apple.isPCLocal)
        XCTAssertFalse(apple.streamsAudio)
        XCTAssertEqual(apple.sampleRate, 16_000)
        XCTAssertEqual(SpeechProvider.stored("apple"), .apple)
        XCTAssertEqual(SpeechProvider.stored("Apple"), .meta, "Raw values are compared exactly.")
        XCTAssertFalse(RelayConstants.speechModels.contains(apple.rawValue), "The PC has no model of this name.")
        XCTAssertEqual(TranscriptionUsage(provider: apple.rawValue).hourlyRateUSD, 0)
        XCTAssertFalse(PCRequirement.needsPC(speech: apple, answers: .openrouter))
    }

    /// D-29: Fluid Speech Mini is the seventh provider, last in the catalogue.
    func testFluidIsOnDeviceAndNeedsNeitherCloudNorPC() {
        let fluid = SpeechProvider.fluid
        XCTAssertEqual(fluid.rawValue, "fluid")
        XCTAssertEqual(SpeechProvider(rawValue: "fluid"), .fluid)
        XCTAssertEqual(SpeechProvider.allCases.last, .fluid)
        XCTAssertEqual(SpeechProvider.allCases.count, 7)
        XCTAssertEqual(fluid.displayName, "Fluid Speech Mini \u{00B7} iPhone")
        XCTAssertTrue(fluid.isOnDevice)
        XCTAssertFalse(fluid.isCloud)
        XCTAssertFalse(fluid.isPCLocal)
        XCTAssertFalse(fluid.streamsAudio)
        XCTAssertEqual(fluid.sampleRate, 16_000)
        XCTAssertEqual(SpeechProvider.stored("fluid"), .fluid)
        XCTAssertEqual(SpeechProvider.stored("fluid-mini"), .meta, "The model's variant is not the provider's raw value.")
        XCTAssertFalse(RelayConstants.speechModels.contains(fluid.rawValue), "The PC has no model of this name.")
        XCTAssertEqual(TranscriptionUsage(provider: fluid.rawValue).hourlyRateUSD, 0)
        XCTAssertFalse(PCRequirement.needsPC(speech: fluid, answers: .openrouter))
    }

    func testEveryProviderIsExactlyOneKind() {
        for provider in SpeechProvider.allCases {
            let kinds = [provider.isCloud, provider.isPCLocal, provider.isOnDevice].filter { $0 }
            XCTAssertEqual(kinds.count, 1, provider.rawValue)
            XCTAssertEqual(provider.streamsAudio, provider.isCloud || provider.isPCLocal, provider.rawValue)
            XCTAssertEqual(provider.streamsAudio, !provider.isOnDevice, provider.rawValue)
        }
    }

    func testOnDeviceCatalogueEntries() {
        XCTAssertEqual(TranscriptionModel.onDevice(for: .voz), TranscriptionModel.voz)
        XCTAssertEqual(TranscriptionModel.onDevice(for: .parakeet), TranscriptionModel.parakeet)
        XCTAssertEqual(TranscriptionModel.onDevice(for: .apple), TranscriptionModel.apple)
        XCTAssertEqual(TranscriptionModel.onDevice(for: .fluid), TranscriptionModel.fluid)
        XCTAssertNil(TranscriptionModel.onDevice(for: .meta))
        XCTAssertNil(TranscriptionModel.onDevice(for: .nemotron))
        XCTAssertNil(TranscriptionModel.onDevice(for: .qwen3))

        XCTAssertEqual(
            TranscriptionModel.voz,
            TranscriptionModel(
                variant: "voz",
                displayName: "Voz on Assist",
                approximateMegabytes: 467,
                quality: "Transcribes when you tap Assist",
                recommended: true
            )
        )
        XCTAssertEqual(
            TranscriptionModel.parakeet,
            TranscriptionModel(
                variant: "parakeet",
                displayName: "Live Parakeet",
                approximateMegabytes: 230,
                quality: "English live captions \u{00B7} 320 ms mode",
                recommended: true
            )
        )
        // The system keeps Apple's language asset, so the entry names no size.
        XCTAssertEqual(
            TranscriptionModel.apple,
            TranscriptionModel(
                variant: "apple",
                displayName: "Apple live captions",
                approximateMegabytes: 0,
                quality: "English live captions \u{00B7} built into iOS",
                recommended: false
            )
        )
        // Nobody has tried Fluid Speech Mini on a phone yet, so it is not recommended (D-29).
        XCTAssertEqual(
            TranscriptionModel.fluid,
            TranscriptionModel(
                variant: "fluid-mini",
                displayName: "Live Fluid Speech Mini",
                approximateMegabytes: 253,
                quality: "English live captions \u{00B7} rolling 15 s window",
                recommended: false
            )
        )
        XCTAssertEqual(TranscriptionModel.fluid.id, "fluid-mini")
        XCTAssertEqual(TranscriptionModel.apple.id, SpeechProvider.apple.rawValue)
        XCTAssertEqual(TranscriptionModel.voz.id, SpeechProvider.voz.rawValue)
        XCTAssertEqual(TranscriptionModel.parakeet.id, SpeechProvider.parakeet.rawValue)
    }

    // MARK: - The stored value (spec 02 section 7.3)

    func testStoredDefaultsToMeta() {
        XCTAssertEqual(SpeechProvider.stored(nil), .meta)
        XCTAssertEqual(SpeechProvider.stored(""), .meta)
        XCTAssertEqual(SpeechProvider.stored("whisper"), .meta)
        XCTAssertEqual(SpeechProvider.stored("Meta"), .meta)
        XCTAssertEqual(SpeechProvider.stored("NEMOTRON"), .meta, "Raw values are compared exactly.")
        XCTAssertEqual(SpeechProvider.stored(" voz"), .meta)
        XCTAssertEqual(SpeechProvider.stored("qwen"), .meta)
    }

    func testStoredReadsEveryKnownRawValue() {
        for provider in SpeechProvider.allCases {
            XCTAssertEqual(SpeechProvider.stored(provider.rawValue), provider)
        }
    }

    // MARK: - Is the PC needed

    func testNeedsPCForEveryPair() {
        let expected: [(speech: SpeechProvider, answers: AssistantProvider, needsPC: Bool)] = [
            (.meta, .codex, true),
            (.meta, .openrouter, false),
            (.nemotron, .codex, true),
            (.nemotron, .openrouter, true),
            (.qwen3, .codex, true),
            (.qwen3, .openrouter, true),
            (.voz, .codex, true),
            (.voz, .openrouter, false),
            (.parakeet, .codex, true),
            (.parakeet, .openrouter, false),
            (.apple, .codex, true),
            (.apple, .openrouter, false),
            (.fluid, .codex, true),
            (.fluid, .openrouter, false)
        ]
        XCTAssertEqual(expected.count, SpeechProvider.allCases.count * AssistantProvider.allCases.count)
        for row in expected {
            XCTAssertEqual(
                PCRequirement.needsPC(speech: row.speech, answers: row.answers),
                row.needsPC,
                "\(row.speech.rawValue) with \(row.answers.rawValue)"
            )
        }
        // The table above names every pair once.
        for speech in SpeechProvider.allCases {
            for answers in AssistantProvider.allCases {
                XCTAssertEqual(expected.filter { $0.speech == speech && $0.answers == answers }.count, 1)
            }
        }
    }
}
