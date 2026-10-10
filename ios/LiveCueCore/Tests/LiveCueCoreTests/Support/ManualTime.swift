import Foundation
import LiveCueCore

/// A clock that only moves when a test says so.
///
/// `timing.sleep` suspends until `advance(by:)` has moved time past its deadline, or until its task is
/// cancelled (it then throws `CancellationError`). A sleep of zero or less returns at once.
struct ManualTime: Sendable {
    private let state = ManualTimeState()

    init() {}

    /// The value to hand to the code under test.
    var timing: CoreTiming {
        CoreTiming(
            uptime: { [state] in state.uptime() },
            sleep: { [state] seconds in try await state.sleep(seconds) }
        )
    }

    /// Seconds advanced so far.
    var now: TimeInterval {
        state.uptime()
    }

    /// How many sleeps are waiting.
    var sleeperCount: Int {
        state.sleeperCount()
    }

    /// Moves time forward and wakes the sleepers whose time has come, in deadline order. After each wake
    /// it yields, so a woken task can run on and sleep again inside the same advance.
    func advance(by seconds: TimeInterval) async {
        let target = state.uptime() + max(0, seconds)
        while let continuation = state.popDue(upTo: target) {
            continuation.resume()
            await settle()
        }
        await settle()
    }

    /// Returns when at least `count` sleeps are waiting. Call it before `advance(by:)` when the code under
    /// test starts its sleep on another task.
    func waitForSleepers(count: Int = 1) async {
        await state.waitForSleepers(count: count)
    }

    private func settle() async {
        for _ in 0..<20 {
            await Task.yield()
        }
    }
}

/// The shared state behind `ManualTime`. Synchronous callers (`uptime`) need it, so it is guarded by a
/// lock, not an actor.
private final class ManualTimeState: @unchecked Sendable {
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
    private var now: TimeInterval = 0
    private var nextID = 0
    private var sleepers: [Sleeper] = []
    private var waiters: [Waiter] = []

    func uptime() -> TimeInterval {
        lock.withLock { now }
    }

    func sleeperCount() -> Int {
        lock.withLock { sleepers.count }
    }

    func sleep(_ seconds: TimeInterval) async throws {
        try Task.checkCancellation()
        guard seconds > 0 else { return }

        let id = lock.withLock { () -> Int in
            nextID += 1
            return nextID
        }

        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, any Error>) in
                let (cancelled, ready) = lock.withLock { () -> (Bool, [Waiter]) in
                    if Task.isCancelled {
                        return (true, [])
                    }
                    sleepers.append(Sleeper(id: id, deadline: now + seconds, continuation: continuation))
                    let waiting = sleepers.count
                    let satisfied = waiters.filter { $0.count <= waiting }
                    waiters.removeAll { $0.count <= waiting }
                    return (false, satisfied)
                }
                if cancelled {
                    continuation.resume(throwing: CancellationError())
                }
                for waiter in ready {
                    waiter.continuation.resume()
                }
            }
        } onCancel: {
            self.cancelSleeper(id)
        }
    }

    /// Removes the earliest sleeper due at or before `target` and moves time to its deadline. With none
    /// due, moves time to `target` and returns nil.
    func popDue(upTo target: TimeInterval) -> CheckedContinuation<Void, any Error>? {
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
                now = max(now, target)
                return nil
            }
            let sleeper = sleepers.remove(at: earliest)
            now = max(now, sleeper.deadline)
            return sleeper.continuation
        }
    }

    func waitForSleepers(count: Int) async {
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

    private func cancelSleeper(_ id: Int) {
        let sleeper = lock.withLock { () -> Sleeper? in
            guard let index = sleepers.firstIndex(where: { $0.id == id }) else { return nil }
            return sleepers.remove(at: index)
        }
        sleeper?.continuation.resume(throwing: CancellationError())
    }
}
