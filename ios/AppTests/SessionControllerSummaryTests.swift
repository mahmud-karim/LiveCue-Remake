import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The end-of-session summary and its cancellation.
final class SessionControllerSummaryTests: XCTestCase {
    private let firstWords = "We agreed to ship the update on Friday."

    /// Starts a session, commits `texts` as segments and stops it. Returns the session's id.
    @MainActor
    private func runSession(_ owners: TestOwners, saying texts: [String]) async throws -> UUID {
        _ = try await owners.startListening()
        let id = try XCTUnwrap(owners.session.session?.id)
        var start = 0.0
        for text in texts {
            try await owners.say(text, at: start)
            start += 2
        }
        await owners.session.stop()
        XCTAssertEqual(owners.session.phase, .idle)
        return id
    }

    /// Lets tasks that are ready run, so that a request that was going to be made has been made.
    @MainActor
    private func letTasksRun() async {
        for _ in 0..<50 {
            await Task.yield()
        }
    }

    // MARK: - When a summary is asked for

    @MainActor
    func testASummaryIsRequestedAfterTheEndAndReplacesTitleAndNotes() async throws {
        // AC-IOS-078
        let owners = TestOwners()
        await owners.codex.setHoldSummary(true)
        await owners.codex.setSummary(.success(
            SummaryResponse(
                title: "Friday release",
                summary: "The update ships on Friday.",
                keyPoints: ["Ship on Friday", "Test on Thursday"],
                actionItems: ["Write the release notes"]
            )
        ))

        let id = try await runSession(owners, saying: [firstWords, "Testing happens on Thursday."])
        await owners.codex.waitForSummaryRequests(count: 1)

        // The UI has returned before the summary arrives.
        XCTAssertEqual(owners.session.phase, .idle)
        XCTAssertNil(owners.session.session)
        XCTAssertEqual(owners.history.sessions.first?.title, firstWords)
        XCTAssertEqual(owners.session.summaryRuns.count, 1)
        let requests = await owners.codex.summaryRequests
        let request = try XCTUnwrap(requests.first)
        XCTAssertEqual(request.sessionId, id)
        XCTAssertEqual(request.assistant, AssistantConfiguration())
        XCTAssertEqual(request.transcript, firstWords + "\nTesting happens on Thursday.")
        XCTAssertNil(request.transcriptTruncated)

        await owners.codex.releaseSummary()
        await owners.waitUntil("the summary was applied") { owners.history.sessions.first?.title == "Friday release" }

        let notes = SessionNotes(
            summary: "The update ships on Friday.",
            keyPoints: ["Ship on Friday", "Test on Thursday"],
            actionItems: ["Write the release notes"]
        )
        XCTAssertEqual(owners.history.sessions.first?.notes, notes)
        XCTAssertEqual(owners.history.sessions.count, 1)
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.title, "Friday release")
        XCTAssertEqual(stored?.notes, notes)
        XCTAssertEqual(stored?.segments.count, 2, "Nothing else of the session changed.")
        XCTAssertNotNil(stored?.endedAt)
        XCTAssertTrue(owners.session.summaryRuns.isEmpty)
        XCTAssertNil(owners.app.alertMessage)
        let allRequests = await owners.codex.summaryRequests
        XCTAssertEqual(allRequests.count, 1)
    }

    @MainActor
    func testNoSummaryForOpenRouterAnswers() async throws {
        // AC-IOS-079, P2-220
        let owners = TestOwners(answers: .openrouter)

        let id = try await runSession(owners, saying: [firstWords])
        await letTasksRun()

        XCTAssertTrue(owners.session.summaryRuns.isEmpty)
        let codexRequests = await owners.codex.summaryRequests
        XCTAssertTrue(codexRequests.isEmpty)
        let openRouterRequests = await owners.openRouter.summaryRequests
        XCTAssertTrue(openRouterRequests.isEmpty)
        let gatewayCalls = await owners.gateway.calls
        XCTAssertEqual(gatewayCalls, [], "The PC is not contacted.")
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.title, firstWords)
        XCTAssertEqual(stored?.notes, SessionNotes())
    }

    @MainActor
    func testTheServiceSelectedAtStopDecides() async throws {
        // Codex when the session started, OpenRouter when it stopped: no summary.
        let owners = TestOwners()
        _ = try await owners.startListening()
        try await owners.say(firstWords)
        owners.assistant.switchProvider(.openrouter)
        await owners.session.stop()
        await letTasksRun()
        XCTAssertTrue(owners.session.summaryRuns.isEmpty)
        var requests = await owners.codex.summaryRequests
        XCTAssertTrue(requests.isEmpty)

        // OpenRouter when the session started, Codex when it stopped: a summary.
        let second = TestOwners(answers: .openrouter)
        _ = try await second.startListening()
        try await second.say(firstWords)
        second.assistant.switchProvider(.codex)
        await second.session.stop()
        await second.codex.waitForSummaryRequests(count: 1)
        requests = await second.codex.summaryRequests
        XCTAssertEqual(requests.first?.assistant.provider, .codex)
    }

    @MainActor
    func testNoSummaryWithoutAPairing() async throws {
        // P2-208
        let owners = TestOwners(paired: false)

        let id = try await runSession(owners, saying: [firstWords])
        await letTasksRun()

        XCTAssertTrue(owners.session.summaryRuns.isEmpty)
        let requests = await owners.codex.summaryRequests
        XCTAssertTrue(requests.isEmpty)
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.title, firstWords)
    }

    @MainActor
    func testNoSummaryForASessionWithoutSegments() async throws {
        // IOS-B12
        let owners = TestOwners()

        let id = try await runSession(owners, saying: [])
        await letTasksRun()

        XCTAssertTrue(owners.session.summaryRuns.isEmpty)
        let requests = await owners.codex.summaryRequests
        XCTAssertTrue(requests.isEmpty)
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.title, "Conversation")
    }

    // MARK: - Failure

    @MainActor
    func testASummaryFailureIsSilentAndFinal() async throws {
        let owners = TestOwners()
        await owners.codex.setSummary(.failure(RelayFailure.wire(.assistantFailed)))

        let id = try await runSession(owners, saying: [firstWords])
        await owners.codex.waitForSummaryRequests(count: 1)
        await owners.waitUntil("the summary ended") { owners.session.summaryRuns.isEmpty }
        await letTasksRun()

        XCTAssertNil(owners.app.alertMessage, "Nothing is shown.")
        XCTAssertEqual(owners.history.sessions.first?.title, firstWords)
        XCTAssertEqual(owners.history.sessions.first?.notes, SessionNotes())
        let stored = await owners.stored(id)
        XCTAssertEqual(stored?.title, firstWords)
        XCTAssertEqual(stored?.notes, SessionNotes())
        let requests = await owners.codex.summaryRequests
        XCTAssertEqual(requests.count, 1, "Nothing is retried.")
    }

    // MARK: - Cancellation and stale results

    @MainActor
    func testCancelSummaryCancelsTheServicesRequest() async throws {
        let owners = TestOwners()
        await owners.codex.setHoldSummary(true)
        let id = try await runSession(owners, saying: [firstWords])
        await owners.codex.waitForSummaryRequests(count: 1)
        let requests = await owners.codex.summaryRequests
        let requestID = try XCTUnwrap(requests.first?.requestId)

        // Deleting a session cancels its summary first (P2-212).
        await owners.history.delete(id: id)

        let cancelled = await owners.codex.cancelledIDs
        XCTAssertEqual(cancelled, [requestID])
        XCTAssertTrue(owners.session.summaryRuns.isEmpty)
        XCTAssertTrue(owners.history.sessions.isEmpty)
        let isStored = await owners.sessions.contains(id: id)
        XCTAssertFalse(isStored)

        // Nothing brings the session back, and a second cancel has nothing to cancel.
        await owners.codex.releaseSummary()
        await letTasksRun()
        await owners.session.cancelSummary(forSession: id)
        let cancelledAgain = await owners.codex.cancelledIDs
        XCTAssertEqual(cancelledAgain, [requestID])
        let isStoredLater = await owners.sessions.contains(id: id)
        XCTAssertFalse(isStoredLater)
        XCTAssertNil(owners.app.alertMessage)
    }

    @MainActor
    func testCancelSummaryForASessionWithoutOneDoesNothing() async {
        let owners = TestOwners()

        await owners.session.cancelSummary(forSession: UUID())

        let cancelled = await owners.codex.cancelledIDs
        XCTAssertTrue(cancelled.isEmpty)
    }

    @MainActor
    func testASummaryForADeletedSessionIsDropped() async throws {
        let owners = TestOwners()
        await owners.codex.setHoldSummary(true)
        let id = try await runSession(owners, saying: [firstWords])
        await owners.codex.waitForSummaryRequests(count: 1)

        // The session leaves the store while its summary is still out.
        try await owners.sessions.delete(id: id)
        await owners.codex.releaseSummary()
        await owners.waitUntil("the summary ended") { owners.session.summaryRuns.isEmpty }
        await letTasksRun()

        let isStored = await owners.sessions.contains(id: id)
        XCTAssertFalse(isStored, "The result did not put the session back.")
        let cancelled = await owners.codex.cancelledIDs
        XCTAssertTrue(cancelled.isEmpty)
    }
}
