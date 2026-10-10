import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Files upsert, sort and skip what does not decode; interrupted sessions close at their last save.
///
/// Every test gives the store a temporary folder of its own.
final class FileSessionStoreTests: XCTestCase {
    private let directory = FileManager.default.temporaryDirectory
        .appendingPathComponent("FileSessionStoreTests-\(UUID().uuidString)", isDirectory: true)
        .appendingPathComponent("Sessions", isDirectory: true)

    /// 2026-09-21T14:13:20Z.
    private let start = Date(timeIntervalSince1970: 1_790_000_000)

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory.deletingLastPathComponent())
        super.tearDown()
    }

    // MARK: - Helpers

    private func fileURL(for session: Session) -> URL {
        directory.appendingPathComponent(session.id.uuidString + ".json", isDirectory: false)
    }

    private func fileNames() throws -> [String] {
        try FileManager.default.contentsOfDirectory(atPath: directory.path).sorted()
    }

    /// A session that started `offset` seconds after `start`, with one segment.
    private func session(startedAfter offset: TimeInterval, endedAfter length: TimeInterval? = nil, title: String = Session.defaultTitle) -> Session {
        let startedAt = start.addingTimeInterval(offset)
        return Session(
            title: title,
            startedAt: startedAt,
            endedAt: length.map { startedAt.addingTimeInterval($0) },
            segments: [TranscriptSegment(text: FixtureValues.segmentText, startSeconds: 0, endSeconds: 3)]
        )
    }

    /// A session with every part filled in.
    private func fullSession() -> Session {
        let performance = AssistPerformance(
            configuration: AssistantConfiguration(),
            speechModel: "meta",
            reusedText: false,
            transcriptionMs: 0,
            contextMs: 1.5,
            roundTripMs: 640,
            totalMs: 642.5,
            lastLiveChunkMs: 80,
            transcriptCharacters: 50,
            execution: FixtureValues.execution
        )
        let segment = TranscriptSegment(text: FixtureValues.segmentText, startSeconds: 0, endSeconds: 3)
        let turn = AssistantTurn(
            createdAt: start.addingTimeInterval(5),
            request: "Answer in one sentence.",
            detectedQuestion: FixtureValues.segmentText,
            answer: FixtureValues.answer,
            details: FixtureValues.details,
            performance: performance
        )
        var session = Session(
            title: "Local transcription",
            startedAt: start,
            endedAt: start.addingTimeInterval(60),
            segments: [segment, TranscriptSegment(text: "A second segment · with a middle dot", startSeconds: 4, endSeconds: 6.5)],
            assistantTurns: [turn],
            memory: SessionMemory(summary: "Earlier context", facts: ["A fact"], openQuestions: ["An open question"], throughSegmentId: segment.id),
            notes: SessionNotes(summary: "A short summary.", keyPoints: ["A key point"], actionItems: ["An action item"])
        )
        var usage = TranscriptionUsage(provider: "meta")
        let stream = UUID()
        usage.begin(stream)
        usage.update(stream, sentMs: 3_000, processedMs: 2_900, hasTranscript: true)
        usage.finish(stream, completed: true)
        session.transcriptionUsage = usage
        return session
    }

    // MARK: - Opening

    @MainActor
    func testOpenCreatesTheFolderAndStartsEmpty() async throws {
        XCTAssertFalse(FileManager.default.fileExists(atPath: directory.path))

        let store = try FileSessionStore.open(directory: directory)

        var isDirectory: ObjCBool = false
        XCTAssertTrue(FileManager.default.fileExists(atPath: directory.path, isDirectory: &isDirectory))
        XCTAssertTrue(isDirectory.boolValue)
        let all = await store.loadAll()
        XCTAssertTrue(all.isEmpty)
        let closed = await store.closeInterruptedSessions()
        XCTAssertEqual(closed, 0)

        // Opening a folder that is already there keeps what is in it.
        try await store.save(session(startedAfter: 0, endedAfter: 10), savedAt: start.addingTimeInterval(10))
        let again = try FileSessionStore.open(directory: directory)
        let kept = await again.loadAll()
        XCTAssertEqual(kept.count, 1)
    }

    @MainActor
    func testTheFolderIsProtectedAndFilesAreWrittenAtomicallyWithProtection() async throws {
        let store = try FileSessionStore.open(directory: directory)
        let saved = session(startedAfter: 0, endedAfter: 10)
        try await store.save(saved, savedAt: start.addingTimeInterval(10))

        XCTAssertEqual(FileSessionStore.folderName, "Sessions")
        XCTAssertEqual(FileSessionStore.folderProtection, .completeUntilFirstUserAuthentication)
        XCTAssertTrue(FileSessionStore.fileWritingOptions.contains(.atomic))
        XCTAssertTrue(FileSessionStore.fileWritingOptions.contains(.completeFileProtectionUntilFirstUserAuthentication))

        // A simulator does not keep protection classes and reports none; a device reports the class asked for.
        for path in [directory.path, fileURL(for: saved).path] {
            let attributes = try FileManager.default.attributesOfItem(atPath: path)
            if let protection = attributes[.protectionKey] as? FileProtectionType {
                XCTAssertEqual(protection, .completeUntilFirstUserAuthentication, path)
            }
        }
        // An atomic write leaves the one finished file behind and nothing else.
        XCTAssertEqual(try fileNames(), [saved.id.uuidString + ".json"])
    }

    // MARK: - Saving and loading

    @MainActor
    func testSaveInsertsAndReplacesTheSessionWithTheSameID() async throws {
        let store = try FileSessionStore.open(directory: directory)
        let older = session(startedAfter: 0, endedAfter: 30)
        let newer = session(startedAfter: 600, endedAfter: 30)

        try await store.save(older, savedAt: start.addingTimeInterval(30))
        try await store.save(newer, savedAt: start.addingTimeInterval(630))
        let all = await store.loadAll()
        XCTAssertEqual(all, [newer, older])

        var renamed = older
        renamed.title = "Renamed"
        renamed.segments.append(TranscriptSegment(text: "One more segment", startSeconds: 4, endSeconds: 5))
        try await store.save(renamed, savedAt: start.addingTimeInterval(31))

        let afterUpdate = await store.loadAll()
        XCTAssertEqual(afterUpdate, [newer, renamed], "The session was replaced, not added again.")
        XCTAssertEqual(try fileNames(), [older.id.uuidString + ".json", newer.id.uuidString + ".json"].sorted())
    }

    @MainActor
    func testSessionsLoadNewestStartFirst() async throws {
        let store = try FileSessionStore.open(directory: directory)
        let first = session(startedAfter: 0, endedAfter: 5)
        let second = session(startedAfter: 3_600, endedAfter: 5)
        let third = session(startedAfter: 86_400, endedAfter: 5)

        // Saved in an order that is neither the start order nor its reverse; the save time does not count.
        try await store.save(second, savedAt: start.addingTimeInterval(90_000))
        try await store.save(third, savedAt: start.addingTimeInterval(86_405))
        try await store.save(first, savedAt: start.addingTimeInterval(95_000))

        let all = await store.loadAll()
        XCTAssertEqual(all.map(\.id), [third.id, second.id, first.id])
    }

    @MainActor
    func testAWholeSessionComesBackAsItWasSaved() async throws {
        let saved = fullSession()
        let store = try FileSessionStore.open(directory: directory)
        try await store.save(saved, savedAt: start.addingTimeInterval(61))

        // A store opened later, as after a relaunch, reads the same session.
        let later = try FileSessionStore.open(directory: directory)
        let all = await later.loadAll()
        XCTAssertEqual(all, [saved])
    }

    @MainActor
    func testAFileHoldsOneSessionAndItsLastSavedTimeInLiveCueJSON() async throws {
        let store = try FileSessionStore.open(directory: directory)
        let open = session(startedAfter: 0)
        // 2026-09-21T14:14:55.250Z.
        try await store.save(open, savedAt: Date(timeIntervalSince1970: 1_790_000_095.25))

        XCTAssertEqual(try fileNames(), [open.id.uuidString + ".json"], "The file is named after the session's id.")
        let json = try XCTUnwrap(JSONValue.parse(try Data(contentsOf: fileURL(for: open))))
        XCTAssertEqual(json.objectValue?.keys.sorted(), ["savedAt", "session"])
        XCTAssertEqual(json["savedAt"]?.stringValue, "2026-09-21T14:14:55.250Z", "Dates are stored with milliseconds.")
        XCTAssertEqual(json["session"]?["id"]?.stringValue, open.id.uuidString)
        XCTAssertEqual(json["session"]?["startedAt"]?.stringValue, "2026-09-21T14:13:20.000Z")
        XCTAssertEqual(json["session"]?["title"]?.stringValue, "New conversation")
        XCTAssertNil(json["session"]?["endedAt"], "An absent end time is left out.")
        XCTAssertEqual(json["session"]?["segments"]?.arrayValue?.count, 1)
    }

    @MainActor
    func testAFileThatDoesNotDecodeIsSkippedAndNotDeleted() async throws {
        let store = try FileSessionStore.open(directory: directory)
        let good = session(startedAfter: 0, endedAfter: 5)
        try await store.save(good, savedAt: start.addingTimeInterval(5))

        let broken = directory.appendingPathComponent(UUID().uuidString + ".json", isDirectory: false)
        let cutShort = directory.appendingPathComponent(UUID().uuidString + ".json", isDirectory: false)
        let notes = directory.appendingPathComponent("notes.txt", isDirectory: false)
        try Data("not json".utf8).write(to: broken)
        try Data(contentsOf: fileURL(for: good)).prefix(40).write(to: cutShort)
        try Data("something else".utf8).write(to: notes)

        let all = await store.loadAll()
        XCTAssertEqual(all, [good])
        let closed = await store.closeInterruptedSessions()
        XCTAssertEqual(closed, 0)

        for url in [broken, cutShort, notes] {
            XCTAssertTrue(FileManager.default.fileExists(atPath: url.path), "\(url.lastPathComponent) was left alone.")
        }
        XCTAssertEqual(try Data(contentsOf: broken), Data("not json".utf8))
    }

    @MainActor
    func testAFileThatIsNotNamedAfterItsSessionIsSkipped() async throws {
        let store = try FileSessionStore.open(directory: directory)
        let good = session(startedAfter: 0, endedAfter: 5)
        try await store.save(good, savedAt: start.addingTimeInterval(5))

        // The same session under two other names: it must not show up three times.
        let copy = directory.appendingPathComponent("copy.json", isDirectory: false)
        let other = directory.appendingPathComponent(UUID().uuidString + ".json", isDirectory: false)
        try FileManager.default.copyItem(at: fileURL(for: good), to: copy)
        try FileManager.default.copyItem(at: fileURL(for: good), to: other)

        let all = await store.loadAll()
        XCTAssertEqual(all, [good])
        XCTAssertTrue(FileManager.default.fileExists(atPath: copy.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: other.path))
    }

    // MARK: - Deleting

    @MainActor
    func testDeleteRemovesTheFileAndAnAbsentSessionIsNotAnError() async throws {
        let store = try FileSessionStore.open(directory: directory)
        let kept = session(startedAfter: 0, endedAfter: 5)
        let removed = session(startedAfter: 60, endedAfter: 5)
        try await store.save(kept, savedAt: start.addingTimeInterval(5))
        try await store.save(removed, savedAt: start.addingTimeInterval(65))

        let hasRemoved = await store.contains(id: removed.id)
        XCTAssertTrue(hasRemoved)

        try await store.delete(id: removed.id)
        try await store.delete(id: removed.id)
        try await store.delete(id: UUID())

        let stillHasRemoved = await store.contains(id: removed.id)
        XCTAssertFalse(stillHasRemoved)
        let hasKept = await store.contains(id: kept.id)
        XCTAssertTrue(hasKept)
        let hasUnknown = await store.contains(id: UUID())
        XCTAssertFalse(hasUnknown)
        let all = await store.loadAll()
        XCTAssertEqual(all, [kept])
        XCTAssertEqual(try fileNames(), [kept.id.uuidString + ".json"])
    }

    // MARK: - Launch recovery

    @MainActor
    func testInterruptedSessionsAreClosedAtTheTimeOfTheirLastSave() async throws {
        let store = try FileSessionStore.open(directory: directory)

        var interrupted = session(startedAfter: 0)
        var usage = TranscriptionUsage(provider: "meta")
        usage.begin(UUID())
        interrupted.transcriptionUsage = usage
        let finished = session(startedAfter: 600, endedAfter: 30, title: "Finished")

        // Saved twice, as a running session is; the app was killed after the second save.
        let firstSave = start.addingTimeInterval(42)
        let lastSave = start.addingTimeInterval(95)
        try await store.save(interrupted, savedAt: firstSave)
        try await store.save(interrupted, savedAt: lastSave)
        try await store.save(finished, savedAt: start.addingTimeInterval(630))

        // The next launch.
        let relaunched = try FileSessionStore.open(directory: directory)
        let closed = await relaunched.closeInterruptedSessions()
        XCTAssertEqual(closed, 1)

        let all = await relaunched.loadAll()
        XCTAssertEqual(all.map(\.id), [finished.id, interrupted.id])
        let recovered = try XCTUnwrap(all.last)
        XCTAssertEqual(recovered.endedAt, lastSave, "The end time is the file's last-saved time.")
        XCTAssertEqual(recovered, interrupted.closedAfterInterruption(at: lastSave))
        XCTAssertEqual(recovered.transcriptionUsage?.streams.filter { $0.state == SpeechStreamUsage.streaming }.count, 0)
        XCTAssertEqual(all.first, finished, "A session that had ended is unchanged.")

        // Nothing is left to close, in this store or in one opened later.
        let closedAgain = await relaunched.closeInterruptedSessions()
        XCTAssertEqual(closedAgain, 0)
        let third = try FileSessionStore.open(directory: directory)
        let closedLater = await third.closeInterruptedSessions()
        XCTAssertEqual(closedLater, 0)
        let afterwards = await third.loadAll()
        XCTAssertEqual(afterwards, all)
    }

    @MainActor
    func testClosingKeepsTheLastSavedTimeOfTheFile() async throws {
        let store = try FileSessionStore.open(directory: directory)
        let interrupted = session(startedAfter: 0)
        let lastSave = start.addingTimeInterval(95)
        try await store.save(interrupted, savedAt: lastSave)
        let before = try XCTUnwrap(JSONValue.parse(try Data(contentsOf: fileURL(for: interrupted))))

        await store.closeInterruptedSessions()

        let after = try XCTUnwrap(JSONValue.parse(try Data(contentsOf: fileURL(for: interrupted))))
        XCTAssertNotNil(before["savedAt"])
        XCTAssertEqual(after["savedAt"], before["savedAt"])
    }
}
