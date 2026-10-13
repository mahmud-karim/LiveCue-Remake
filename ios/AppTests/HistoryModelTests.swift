import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// Order, lookup and deletion of stored sessions.
final class HistoryModelTests: XCTestCase {
    /// A session store that answers in the order it was given, keeps a log, and can be told to hold one
    /// load or to refuse a delete.
    private actor ScriptedStore: SessionStoring {
        private var sessions: [Session]
        private var deleteFails = false
        private var heldLoad: FakeGate?
        /// Opens when a held load has begun.
        let loadBegan = FakeGate()
        /// "loadAll" and "delete", and what a test notes, in order.
        private(set) var events: [String] = []

        init(_ sessions: [Session]) {
            self.sessions = sessions
        }

        func setDeleteFails(_ fails: Bool) {
            deleteFails = fails
        }

        /// The next load reads the store at once and answers only when `gate` opens.
        func holdNextLoad(until gate: FakeGate) {
            heldLoad = gate
        }

        func replaceAll(with sessions: [Session]) {
            self.sessions = sessions
        }

        func note(_ event: String) {
            events.append(event)
        }

        func loadAll() async -> [Session] {
            events.append("loadAll")
            let read = sessions
            if let gate = heldLoad {
                heldLoad = nil
                await loadBegan.open()
                await gate.wait()
            }
            return read
        }

        func save(_ session: Session, savedAt: Date) async throws {
            sessions.removeAll { $0.id == session.id }
            sessions.append(session)
        }

        func delete(id: UUID) async throws {
            events.append("delete")
            if deleteFails {
                throw CocoaError(.fileWriteUnknown)
            }
            sessions.removeAll { $0.id == id }
        }

        func contains(id: UUID) async -> Bool {
            sessions.contains { $0.id == id }
        }

        @discardableResult
        func closeInterruptedSessions() async -> Int {
            0
        }
    }

    private func session(_ title: String, startedAfter seconds: TimeInterval) -> Session {
        Session(title: title, startedAt: Date(timeIntervalSince1970: 1_790_000_000 + seconds))
    }

    // MARK: - Order (AC-IOS-085)

    @MainActor
    func testSessionsAreListedNewestStartFirst() async {
        let oldest = session("Oldest", startedAfter: 0)
        let middle = session("Middle", startedAfter: 600)
        let newest = session("Newest", startedAfter: 1_200)
        let history = HistoryModel(store: ScriptedStore([middle, oldest, newest]))
        XCTAssertTrue(history.sessions.isEmpty, "Nothing is read before the first reload.")

        await history.reload()

        XCTAssertEqual(history.sessions.map(\.title), ["Newest", "Middle", "Oldest"])
        XCTAssertEqual(history.sessions.map(\.id), [newest.id, middle.id, oldest.id])
    }

    @MainActor
    func testSessionsThatStartedTogetherKeepTheOrderOfTheStore() async {
        let first = session("First", startedAfter: 60)
        let second = session("Second", startedAfter: 60)
        let third = session("Third", startedAfter: 60)
        let later = session("Later", startedAfter: 120)
        let history = HistoryModel(store: ScriptedStore([first, second, later, third]))

        await history.reload()

        XCTAssertEqual(history.sessions.map(\.title), ["Later", "First", "Second", "Third"])
    }

    @MainActor
    func testAReloadShowsWhatTheStoreHoldsNow() async throws {
        let store = InMemorySessionStore()
        let history = HistoryModel(store: store)
        let savedAt = Date(timeIntervalSince1970: 1_790_003_600)
        let older = session("Older", startedAfter: 0)
        var newer = session("Newer", startedAfter: 900)

        try await store.save(older, savedAt: savedAt)
        await history.reload()
        XCTAssertEqual(history.sessions.map(\.id), [older.id])

        // Saving alone changes nothing in memory.
        try await store.save(newer, savedAt: savedAt)
        XCTAssertEqual(history.sessions.map(\.id), [older.id])
        await history.reload()
        XCTAssertEqual(history.sessions.map(\.id), [newer.id, older.id])

        // A summary arrives: the stored session changes, and the reload shows it.
        newer.title = "Local transcription"
        try await store.save(newer, savedAt: savedAt)
        await history.reload()
        XCTAssertEqual(history.sessions.first?.title, "Local transcription")
        XCTAssertEqual(history.sessions.count, 2)
    }

    @MainActor
    func testAReloadThatAskedTheStoreEarlierDoesNotReplaceANewerOne() async {
        let old = session("Old", startedAfter: 0)
        let new = session("New", startedAfter: 60)
        let store = ScriptedStore([old])
        let history = HistoryModel(store: store)
        let gate = FakeGate()
        await store.holdNextLoad(until: gate)

        // The first reload has read the store and is still on its way back.
        let slow = Task { await history.reload() }
        await store.loadBegan.wait()

        await store.replaceAll(with: [new])
        await history.reload()
        XCTAssertEqual(history.sessions.map(\.title), ["New"])

        await gate.open()
        await slow.value
        XCTAssertEqual(history.sessions.map(\.title), ["New"], "The older answer is dropped.")
    }

    // MARK: - Lookup

    @MainActor
    func testASessionIsFoundByItsId() async {
        let kept = session("Kept", startedAfter: 0)
        let other = session("Other", startedAfter: 60)
        let history = HistoryModel(store: ScriptedStore([kept, other]))
        XCTAssertNil(history.session(id: kept.id), "Nothing is known before the first reload.")

        await history.reload()

        XCTAssertEqual(history.session(id: kept.id), kept)
        XCTAssertEqual(history.session(id: other.id)?.title, "Other")
        XCTAssertNil(history.session(id: UUID()))
    }

    // MARK: - Delete

    @MainActor
    func testDeleteCallsWillDeleteFirstThenDeletesAndReloads() async {
        let doomed = session("Doomed", startedAfter: 0)
        let kept = session("Kept", startedAfter: 60)
        let store = ScriptedStore([doomed, kept])
        let history = HistoryModel(store: store)
        await history.reload()
        history.willDelete = { [store] id in
            // The summary request of this session is cancelled while the session is still stored.
            let isStillStored = await store.contains(id: id)
            await store.note("willDelete \(id == doomed.id ? "the doomed session" : "another session"), stored: \(isStillStored)")
        }

        await history.delete(id: doomed.id)

        let events = await store.events
        XCTAssertEqual(events, ["loadAll", "willDelete the doomed session, stored: true", "delete", "loadAll"])
        XCTAssertEqual(history.sessions.map(\.id), [kept.id])
        XCTAssertNil(history.session(id: doomed.id))
        let isStored = await store.contains(id: doomed.id)
        XCTAssertFalse(isStored)
    }

    @MainActor
    func testDeleteWorksWithoutAWillDeleteHook() async {
        let doomed = session("Doomed", startedAfter: 0)
        let store = ScriptedStore([doomed])
        let history = HistoryModel(store: store)
        await history.reload()
        XCTAssertNil(history.willDelete)

        await history.delete(id: doomed.id)
        XCTAssertTrue(history.sessions.isEmpty)

        // Deleting what is not there is harmless.
        await history.delete(id: UUID())
        XCTAssertTrue(history.sessions.isEmpty)
    }

    @MainActor
    func testASessionTheStoreCouldNotDeleteIsStillListed() async {
        let stubborn = session("Stubborn", startedAfter: 0)
        let store = ScriptedStore([stubborn])
        let history = HistoryModel(store: store)
        await history.reload()
        await store.setDeleteFails(true)

        await history.delete(id: stubborn.id)

        XCTAssertEqual(history.sessions.map(\.id), [stubborn.id], "The list shows what the store still holds.")
        let events = await store.events
        XCTAssertEqual(events, ["loadAll", "delete", "loadAll"])
    }
}
