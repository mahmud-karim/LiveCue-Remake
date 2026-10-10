import Foundation
import LiveCueCore

/// Wall-clock time, monotonic time and sleeping, replaceable in tests. Nothing in the app calls `Date()`,
/// `Task.sleep` or `ProcessInfo.systemUptime` outside this file.
protocol AppClock: Sendable {
    /// Rounded to whole milliseconds.
    func now() -> Date
    /// Monotonic seconds.
    func uptime() -> TimeInterval
    /// Throws `CancellationError` when the task is cancelled.
    func sleep(seconds: TimeInterval) async throws
}

struct SystemClock: AppClock {
    init() {}

    func now() -> Date {
        LiveCueJSON.roundedToMilliseconds(Date())
    }

    func uptime() -> TimeInterval {
        ProcessInfo.processInfo.systemUptime
    }

    func sleep(seconds: TimeInterval) async throws {
        // Clamped to a day, so that no value can overflow the nanosecond count.
        let clamped = min(max(0, seconds), 86_400)
        try await Task.sleep(nanoseconds: UInt64(clamped * 1_000_000_000))
    }
}

extension AppClock {
    /// The same clock for core clients.
    var coreTiming: CoreTiming {
        CoreTiming(
            uptime: { self.uptime() },
            sleep: { seconds in try await self.sleep(seconds: seconds) }
        )
    }
}
