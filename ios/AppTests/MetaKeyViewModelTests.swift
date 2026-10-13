import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The Meta key screen: state, save, test and remove (AC-IOS-107, 109 as changed, 110, 112, 113).
final class MetaKeyViewModelTests: XCTestCase {
    /// Not a key of any service.
    private let syntheticKey = "synthetic-unit-key"
    private let savedText = "Key saved securely on this iPhone. Meta connects directly; no PC needed."
    private let missingText = "Add your Meta API key in Transcription. It stays in this iPhone's Keychain."
    private let malformedText = "This key contains spaces or line breaks. Paste it again exactly as issued."

    @MainActor
    func testWithoutAKeyTheStateSaysSoAndNothingCanBeSaved() async {
        let owners = TestOwners(metaKey: nil)
        let model = MetaKeyViewModel(speech: owners.speech)

        XCTAssertFalse(model.hasKey)
        XCTAssertEqual(model.stateChip, StatusChipModel("No key saved", tone: .neutral, mark: .ring))
        XCTAssertNil(model.message)
        XCTAssertFalse(model.isTesting)
        XCTAssertFalse(model.canSave, "The field is empty.")
    }

    @MainActor
    func testSavingAKeyEmptiesTheFieldAndShowsTheSavedState() async {
        let owners = TestOwners(metaKey: nil)
        let model = MetaKeyViewModel(speech: owners.speech)

        model.keyDraft = syntheticKey
        XCTAssertTrue(model.canSave)
        model.save()

        XCTAssertEqual(model.keyDraft, "", "The field is emptied.")
        XCTAssertTrue(model.hasKey)
        XCTAssertEqual(model.stateChip, StatusChipModel("Key saved", tone: .success, mark: .check))
        XCTAssertEqual(model.message, InlineMessage(savedText))
        XCTAssertEqual(owners.secrets.read(.metaAPIKey), syntheticKey)
        XCTAssertFalse(model.canSave)
    }

    @MainActor
    func testAKeyThatIsNotValidIsRefusedAndStaysInTheField() async {
        let owners = TestOwners(metaKey: nil)
        let model = MetaKeyViewModel(speech: owners.speech)

        model.keyDraft = "   "
        model.save()
        XCTAssertEqual(model.keyDraft, "   ")
        XCTAssertFalse(model.hasKey)
        XCTAssertEqual(model.message, InlineMessage(missingText, isError: true))

        model.keyDraft = "synthetic unit key"
        model.save()
        XCTAssertEqual(model.keyDraft, "synthetic unit key")
        XCTAssertFalse(model.hasKey)
        XCTAssertEqual(model.message, InlineMessage(malformedText, isError: true))
        XCTAssertNil(owners.secrets.read(.metaAPIKey))
    }

    @MainActor
    func testTestingTheKeyReportsThatNoAudioWasSent() async {
        let owners = TestOwners(metaKey: syntheticKey)
        let model = MetaKeyViewModel(speech: owners.speech)

        await model.test()

        XCTAssertEqual(model.message?.isError, false)
        XCTAssertEqual(model.message?.text.contains("No microphone audio"), true)
        XCTAssertFalse(model.isTesting)
        let tested = await owners.keyTester.testedKeys
        XCTAssertEqual(tested, [syntheticKey])
    }

    @MainActor
    func testWhileATestRunsNothingCanBeSaved() async {
        let owners = TestOwners(metaKey: syntheticKey)
        let model = MetaKeyViewModel(speech: owners.speech)
        await owners.keyTester.setHold(true)
        model.keyDraft = "another-synthetic-key"
        XCTAssertTrue(model.canSave)

        let testing = Task { await model.test() }
        await SpeechTest.waitUntil { model.isTesting }
        XCTAssertFalse(model.canSave, "A test is running.")
        await SpeechTest.eventually { await owners.keyTester.testedKeys.count == 1 }

        await owners.keyTester.release()
        await testing.value
        XCTAssertFalse(model.isTesting)
        XCTAssertTrue(model.canSave)
    }

    @MainActor
    func testAFailedTestShowsItsMessage() async {
        let owners = TestOwners(metaKey: syntheticKey)
        let model = MetaKeyViewModel(speech: owners.speech)
        await owners.keyTester.setResult(.failure(.authentication))

        await model.test()

        XCTAssertEqual(
            model.message,
            InlineMessage("Meta did not accept the connection. Check the API key, account access, and billing in Transcription.", isError: true)
        )
        XCTAssertTrue(model.hasKey, "A failed test removes nothing.")
    }

    @MainActor
    func testRemovingTheKeyShowsTheEmptyState() async {
        let owners = TestOwners(metaKey: syntheticKey)
        let model = MetaKeyViewModel(speech: owners.speech)
        XCTAssertTrue(model.hasKey)

        model.remove()

        XCTAssertFalse(model.hasKey)
        XCTAssertEqual(model.stateChip, StatusChipModel("No key saved", tone: .neutral, mark: .ring))
        XCTAssertEqual(model.message, InlineMessage("Key removed."))
        XCTAssertNil(owners.secrets.read(.metaAPIKey))
    }

    @MainActor
    func testAppearingClearsTheDraft() async {
        let owners = TestOwners()
        let model = MetaKeyViewModel(speech: owners.speech)
        model.keyDraft = syntheticKey

        model.appeared()

        XCTAssertEqual(model.keyDraft, "")
    }

    @MainActor
    func testDuringASessionTheKeyCannotBeChanged() async {
        let owners = TestOwners(metaKey: nil)
        let model = MetaKeyViewModel(speech: owners.speech)
        owners.activity.isActive = true

        model.keyDraft = syntheticKey
        model.save()

        XCTAssertEqual(model.keyDraft, syntheticKey, "Nothing was saved, so the field keeps its text.")
        XCTAssertFalse(model.hasKey)
        XCTAssertNil(owners.secrets.read(.metaAPIKey))
    }
}
