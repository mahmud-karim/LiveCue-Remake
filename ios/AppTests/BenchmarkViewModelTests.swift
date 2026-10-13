import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The worksheet's rate, and where "Use latest transcript" takes its text from (IOS-D6).
final class BenchmarkViewModelTests: XCTestCase {
    @MainActor
    private func makeModel(_ owners: TestOwners) -> BenchmarkViewModel {
        BenchmarkViewModel(session: owners.session, history: owners.history)
    }

    private func storedSession(_ texts: [String], startedAt: Date) -> Session {
        let segments = texts.enumerated().map { index, text in
            TranscriptSegment(text: text, startSeconds: Double(index), endSeconds: Double(index) + 1)
        }
        return Session(title: "Stored", startedAt: startedAt, endedAt: startedAt.addingTimeInterval(60), segments: segments)
    }

    // MARK: - The rate

    @MainActor
    func testTheWorksheetStartsWithTheReferencePhrase() async {
        let model = makeModel(TestOwners())

        XCTAssertEqual(model.reference, "The quick brown fox jumps over the lazy dog")
        XCTAssertEqual(model.hypothesis, "")
        // Nine words expected and none heard.
        XCTAssertEqual(model.rateText, "100.0%")
    }

    @MainActor
    func testTheRateFollowsBothTexts() async {
        let model = makeModel(TestOwners())

        model.hypothesis = "the quick brown fox jumps over the lazy dog"
        XCTAssertEqual(model.rateText, "0.0%", "Case does not count.")

        model.hypothesis = "the quick brown fox jumped over a lazy dog"
        XCTAssertEqual(model.rateText, "22.2%", "Two of nine words differ.")

        model.reference = "Hello, world!"
        model.hypothesis = "hello world"
        XCTAssertEqual(model.rateText, "0.0%", "Punctuation does not count.")

        model.hypothesis = "hello there big wide world"
        XCTAssertEqual(model.rateText, "150.0%", "The rate can pass one hundred.")
    }

    @MainActor
    func testWithoutAReferenceThereIsNoRate() async {
        let model = makeModel(TestOwners())
        model.hypothesis = "something was heard"

        model.reference = ""
        XCTAssertEqual(model.rateText, "–")

        model.reference = "  \n "
        XCTAssertEqual(model.rateText, "–")
    }

    // MARK: - Use latest transcript (IOS-D6)

    @MainActor
    func testWithoutAnyConversationTheButtonIsOff() async {
        let model = makeModel(TestOwners())

        XCTAssertFalse(model.canUseLatest)
        XCTAssertEqual(model.latestCaption, "No conversations yet.")

        model.hypothesis = "typed by hand"
        model.useLatestTranscript()
        XCTAssertEqual(model.hypothesis, "typed by hand", "Nothing to fill it with.")
    }

    @MainActor
    func testTheNewestStoredConversationFillsTheBox() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        let now = owners.clock.now()
        try await owners.sessions.save(
            storedSession(["An older", "conversation."], startedAt: now.addingTimeInterval(-7_200)),
            savedAt: now
        )
        try await owners.sessions.save(
            storedSession(["The newest", "stored", "conversation."], startedAt: now.addingTimeInterval(-600)),
            savedAt: now
        )
        await owners.history.reload()

        XCTAssertTrue(model.canUseLatest)
        XCTAssertEqual(model.latestCaption, "Fills the box with your most recent conversation, active or saved.")

        model.useLatestTranscript()
        XCTAssertEqual(model.hypothesis, "The newest stored conversation.")
    }

    @MainActor
    func testTheActiveConversationComesBeforeTheStoredOnes() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        let now = owners.clock.now()
        try await owners.sessions.save(
            storedSession(["A stored conversation."], startedAt: now.addingTimeInterval(-600)),
            savedAt: now
        )
        await owners.history.reload()

        _ = try await owners.startListening()
        try await owners.say("The quick brown fox")
        try await owners.say("jumps over the lazy dog", at: 2)

        XCTAssertTrue(model.canUseLatest)
        model.useLatestTranscript()
        XCTAssertEqual(model.hypothesis, "The quick brown fox jumps over the lazy dog")
        XCTAssertEqual(model.rateText, "0.0%")
    }

    @MainActor
    func testAnActiveConversationWithoutSpeechEmptiesTheBox() async throws {
        let owners = TestOwners()
        let model = makeModel(owners)
        _ = try await owners.startListening()
        model.hypothesis = "typed by hand"

        XCTAssertTrue(model.canUseLatest)
        model.useLatestTranscript()

        XCTAssertEqual(model.hypothesis, "")
    }
}
