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
        XCTAssertEqual(SpeechProvider.allCases, [.meta, .nemotron, .qwen3, .voz, .parakeet])
        XCTAssertEqual(SpeechProvider.allCases.map(\.rawValue), ["meta", "nemotron", "qwen3", "voz", "parakeet"])
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
            Row(provider: .parakeet, name: "Parakeet \u{00B7} iPhone", isCloud: false, isPCLocal: false, isOnDevice: true, streamsAudio: false, sampleRate: 16_000)
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

    func testNeedsPCForAllTenPairs() {
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
            (.parakeet, .openrouter, false)
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
