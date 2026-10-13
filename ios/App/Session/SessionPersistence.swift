import Foundation
import LiveCueCore

/// Latest value wins: a save requested while one is running replaces any queued one.
///
/// One save runs at a time, so the store receives them in the order they were asked for. A save that
/// the store refuses is dropped; the next one carries the whole session again.
@MainActor final class SessionPersistence {
    private let store: any SessionStoring
    private let clock: any AppClock
    /// The newest session that has not been handed to the store yet.
    private var queued: Session?
    private var isSaving = false
    private var flushWaiters: [CheckedContinuation<Void, Never>] = []

    init(store: any SessionStoring, clock: any AppClock) {
        self.store = store
        self.clock = clock
    }

    func schedule(_ session: Session) {
        queued = session
        guard !isSaving else { return }
        isSaving = true
        Task { await self.drain() }
    }

    /// Returns when nothing is queued or running.
    func flush() async {
        guard isSaving || queued != nil else { return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            flushWaiters.append(continuation)
        }
    }

    private func drain() async {
        while let next = queued {
            queued = nil
            try? await store.save(next, savedAt: clock.now())
        }
        isSaving = false

        let waiting = flushWaiters
        flushWaiters = []
        for continuation in waiting {
            continuation.resume()
        }
    }
}
