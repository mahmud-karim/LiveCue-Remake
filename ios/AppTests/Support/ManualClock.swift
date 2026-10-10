import Foundation
@testable import LiveCueRemake

/// A clock that only moves when a test says so.
///
/// `sleep(seconds:)` suspends until `advance(by:)` has moved time past its deadline, or until its task is
/// cancelled (it then throws `CancellationError`). A sleep of zero or less returns at once. The state is
/// guarded by a lock, because `now()` and `uptime()` are synchronous.
final class ManualClock: AppClock, @unchecked Sendable {
    private struct Sleeper {
        let id: Int
        let deadline: TimeInterval
        let continuation: CheckedContinuation<Void, any Error>
    }

    private struct Waiter {
        let count: Int
        let continuation: CheckedContinuation<Void, Never>
    }

    private let lock = NSLock()
    private let start: Date
    private var elapsed: TimeInterval = 0
    private var nextID = 0
    private var sleepers: [Sleeper] = []
    private var waiters: [Waiter] = []

    init(now: Date = Date(timeIntervalSince1970: 1_790_000_000)) {
        start = now
    }

    // MARK: - AppClock

    func now() -> Date {
        let seconds = lock.withLock { elapsed }
        let milliseconds = ((start.timeIntervalSince1970 + seconds) * 1_000).rounded()
        return Date(timeIntervalSince1970: milliseconds / 1_000)
    }

    func uptime() -> TimeInterval {
        lock.withLock { elapsed }
    }

    func sleep(seconds: TimeInterval) async throws {
        try Task.checkCancellation()
        guard seconds > 0 else { return }

        let id = lock.withLock { () -> Int in
            nextID += 1
            return nextID
        }

        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, any Error>) in
                let (cancelled, satisfied) = lock.withLock { () -> (Bool, [Waiter]) in
                    if Task.isCancelled {
                        return (true, [])
                    }
                    sleepers.append(Sleeper(id: id, deadline: elapsed + seconds, continuation: continuation))
                    let waiting = sleepers.count
                    let ready = waiters.filter { $0.count <= waiting }
                    waiters.removeAll { $0.count <= waiting }
                    return (false, ready)
                }
                if cancelled {
                    continuation.resume(throwing: CancellationError())
                }
                for waiter in satisfied {
                    waiter.continuation.resume()
                }
            }
        } onCancel: {
            self.cancelSleeper(id)
        }
    }

    // MARK: - Driving time

    /// How many sleeps are waiting.
    var sleeperCount: Int {
        lock.withLock { sleepers.count }
    }

    /// Moves `now` and `uptime` forward and wakes the sleepers whose time has come, in deadline order,
    /// yielding between them so that a woken task can run on and sleep again inside the same advance.
    func advance(by seconds: TimeInterval) async {
        let target = uptime() + max(0, seconds)
        while let continuation = popDue(upTo: target) {
            continuation.resume()
            await settle()
        }
        await settle()
    }

    /// Returns when at least `count` sleeps are waiting.
    func waitForSleepers(count: Int = 1) async {
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            let ready = lock.withLock { () -> Bool in
                if sleepers.count >= count {
                    return true
                }
                waiters.append(Waiter(count: count, continuation: continuation))
                return false
            }
            if ready {
                continuation.resume()
            }
        }
    }

    // MARK: - Internals

    private func settle() async {
        for _ in 0..<20 {
            await Task.yield()
        }
    }

    /// Removes the earliest sleeper due at or before `target` and moves time to its deadline. With none
    /// due, moves time to `target` and returns nil.
    private func popDue(upTo target: TimeInterval) -> CheckedContinuation<Void, any Error>? {
        lock.withLock { () -> CheckedContinuation<Void, any Error>? in
            var earliest: Int?
            for index in sleepers.indices where sleepers[index].deadline <= target {
                if let current = earliest {
                    let candidate = (sleepers[index].deadline, sleepers[index].id)
                    if candidate < (sleepers[current].deadline, sleepers[current].id) {
                        earliest = index
                    }
                } else {
                    earliest = index
                }
            }
            guard let earliest else {
                elapsed = max(elapsed, target)
                return nil
            }
            let sleeper = sleepers.remove(at: earliest)
            elapsed = max(elapsed, sleeper.deadline)
            return sleeper.continuation
        }
    }

    private func cancelSleeper(_ id: Int) {
        let sleeper = lock.withLock { () -> Sleeper? in
            guard let index = sleepers.firstIndex(where: { $0.id == id }) else { return nil }
            return sleepers.remove(at: index)
        }
        sleeper?.continuation.resume(throwing: CancellationError())
    }
}
