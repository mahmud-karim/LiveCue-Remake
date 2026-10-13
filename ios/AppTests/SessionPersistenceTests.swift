import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// A session store that records every save, and can keep a save waiting or refuse it.
private actor RecordingSessionStore: SessionStoring {
    private struct StartWaiter {
        let count: Int
        let continuation: CheckedContinuation<Void, Never>
    }

    private var holds = false
    private var refuses = false
    private var held: [CheckedContinuation<Void, Never>] = []
    private var startWaiters: [StartWaiter] = []
    private var startedCount = 0

    /// The title of every save that reached the store, in the order the saves started.
    private(set) var startedTitles: [String] = []
    /// Every save that finished, in order.
    private(set) var saved: [Session] = []
    private(set) var savedTimes: [Date] = []

    /// True: a save waits, after it was recorded as started, until `release()`.
    func setHold(_ hold: Bool) {
        holds = hold
    }

    /// True: a save throws.
    func setRefuses(_ refuse: Bool) {
        refuses = refuse
    }

    /// Lets every waiting save finish.
    func release() {
        let waiting = held
        held = []
        for continuation in waiting {
            continuation.resume()
        }
    }

    /// Returns when at least `count` saves have started.
    func waitForStartedSaves(count: Int) async {
        if startedCount >= count { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            startWaiters.append(StartWaiter(count: count, continuation: continuation))
        }
    }

    // MARK: - SessionStoring

    func loadAll() async -> [Session] {
        saved
    }

    func save(_ session: Session, savedAt: Date) async throws {
        startedCount += 1
        startedTitles.append(session.title)
        let ready = startWaiters.filter { $0.count <= startedCount }
        startWaiters.removeAll { $0.count <= startedCount }
        for waiter in ready {
            waiter.continuation.resume()
        }

        if holds {
            await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
                held.append(continuation)
            }
        }
        if refuses {
            throw CocoaError(.fileWriteUnknown)
        }
        saved.append(session)
        savedTimes.append(savedAt)
    }

    func delete(id: UUID) async throws {
        saved.removeAll { $0.id == id }
    }

    func contains(id: UUID) async -> Bool {
        saved.contains { $0.id == id }
    }

    @discardableResult
    func closeInterruptedSessions() async -> Int {
        0
    }
}

/// Says whether a task of the test has finished.
@MainActor private final class Flag {
    var isSet = false
}

/// Latest value wins; `flush` waits; saves are in order.
final class SessionPersistenceTests: XCTestCase {
    private func session(_ title: String, id: UUID = UUID()) -> Session {
        Session(id: id, title: title, startedAt: Date(timeIntervalSince1970: 1_790_000_000))
    }

    @MainActor
    func testASaveReachesTheStoreWithTheClocksTime() async {
        let store = RecordingSessionStore()
        let clock = ManualClock()
        let persistence = SessionPersistence(store: store, clock: clock)
        let first = session("First")

        persistence.schedule(first)
        await persistence.flush()

        let saved = await store.saved
        XCTAssertEqual(saved, [first])
        let times = await store.savedTimes
        XCTAssertEqual(times, [clock.now()])
    }

    @MainActor
    func testTheLatestValueWinsWhileASaveIsRunning() async {
        let store = RecordingSessionStore()
        let persistence = SessionPersistence(store: store, clock: ManualClock())
        let id = UUID()
        await store.setHold(true)

        persistence.schedule(session("One", id: id))
        await store.waitForStartedSaves(count: 1)
        // Three more are asked for while the first is with the store: only the newest is kept.
        persistence.schedule(session("Two", id: id))
        persistence.schedule(session("Three", id: id))
        persistence.schedule(session("Four", id: id))

        await store.setHold(false)
        await store.release()
        await persistence.flush()

        let saved = await store.saved
        XCTAssertEqual(saved.map(\.title), ["One", "Four"])
        let started = await store.startedTitles
        XCTAssertEqual(started, ["One", "Four"], "Two and Three never reached the store.")
    }

    @MainActor
    func testFlushWaitsForTheRunningAndTheQueuedSave() async {
        let store = RecordingSessionStore()
        let persistence = SessionPersistence(store: store, clock: ManualClock())
        await store.setHold(true)
        persistence.schedule(session("One"))
        await store.waitForStartedSaves(count: 1)
        persistence.schedule(session("Two"))

        let flushed = Flag()
        let flushing = Task {
            await persistence.flush()
            flushed.isSet = true
        }
        for _ in 0..<50 {
            await Task.yield()
        }
        XCTAssertFalse(flushed.isSet, "A save is running and another is queued.")

        // The first save ends; the queued one starts and is held in turn.
        await store.release()
        await store.waitForStartedSaves(count: 2)
        for _ in 0..<50 {
            await Task.yield()
        }
        XCTAssertFalse(flushed.isSet, "The queued save is still running.")

        await store.release()
        await flushing.value
        XCTAssertTrue(flushed.isSet)
        let saved = await store.saved
        XCTAssertEqual(saved.map(\.title), ["One", "Two"])

        // With nothing queued or running, flush returns at once.
        await persistence.flush()
    }

    @MainActor
    func testSavesReachTheStoreInTheOrderTheyWereAskedFor() async {
        let store = RecordingSessionStore()
        let persistence = SessionPersistence(store: store, clock: ManualClock())
        let titles = ["One", "Two", "Three", "Four", "Five"]

        for title in titles {
            persistence.schedule(session(title))
            await persistence.flush()
        }

        let saved = await store.saved
        XCTAssertEqual(saved.map(\.title), titles)
    }

    @MainActor
    func testOneSaveRunsAtATime() async {
        let store = RecordingSessionStore()
        let persistence = SessionPersistence(store: store, clock: ManualClock())
        await store.setHold(true)

        persistence.schedule(session("One"))
        await store.waitForStartedSaves(count: 1)
        persistence.schedule(session("Two"))
        for _ in 0..<50 {
            await Task.yield()
        }
        let startedWhileHeld = await store.startedTitles
        XCTAssertEqual(startedWhileHeld, ["One"], "The second save waits for the first.")

        await store.setHold(false)
        await store.release()
        await persistence.flush()
        let started = await store.startedTitles
        XCTAssertEqual(started, ["One", "Two"])
    }

    @MainActor
    func testARefusedSaveIsDroppedAndTheNextOneStillRuns() async {
        let store = RecordingSessionStore()
        let persistence = SessionPersistence(store: store, clock: ManualClock())
        await store.setRefuses(true)

        persistence.schedule(session("Refused"))
        await persistence.flush()
        var saved = await store.saved
        XCTAssertTrue(saved.isEmpty)

        await store.setRefuses(false)
        persistence.schedule(session("Accepted"))
        await persistence.flush()
        saved = await store.saved
        XCTAssertEqual(saved.map(\.title), ["Accepted"])
    }
}
