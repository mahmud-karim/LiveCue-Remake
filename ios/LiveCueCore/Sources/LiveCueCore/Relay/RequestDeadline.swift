import Foundation

/// A total time limit for one request: the operation raced against `CoreTiming.sleep`.
///
/// Internal helper of the two HTTP clients. The transport's own timeout is an idle timeout; the limits
/// that the protocol and the OpenRouter contract give as total times are enforced here.
enum RequestDeadline {
    enum Outcome<Value: Sendable>: Sendable {
        /// The operation ended first. When the calling task was cancelled, this is what the cancelled
        /// operation returned.
        case finished(Value)
        /// The limit passed first. The operation was cancelled and has ended.
        case timedOut
    }

    private enum Event<Value: Sendable>: Sendable {
        case value(Value)
        case limitPassed
        /// The sleep ended without the limit passing: its task was cancelled.
        case sleepEnded
    }

    /// Runs `operation` for at most `seconds`.
    ///
    /// The operation must end soon after its task is cancelled; every `HTTPTransport` does. When the
    /// calling task is cancelled the sleep ends early and the operation's own answer is returned, so
    /// the caller can tell cancellation (protocol 13.6 row 1) from the limit (row 3).
    static func run<Value: Sendable>(
        seconds: TimeInterval,
        timing: CoreTiming,
        operation: @escaping @Sendable () async -> Value
    ) async -> Outcome<Value> {
        await withTaskGroup(of: Event<Value>.self, returning: Outcome<Value>.self) { group in
            group.addTask {
                let value = await operation()
                return Event<Value>.value(value)
            }
            group.addTask {
                do {
                    try await timing.sleep(seconds)
                    return Event<Value>.limitPassed
                } catch {
                    return Event<Value>.sleepEnded
                }
            }

            while let event = await group.next() {
                switch event {
                case .value(let value):
                    group.cancelAll()
                    return Outcome<Value>.finished(value)
                case .limitPassed:
                    group.cancelAll()
                    return Outcome<Value>.timedOut
                case .sleepEnded:
                    continue
                }
            }
            // Not reached: the operation's task always delivers a value before the group is empty.
            return Outcome<Value>.timedOut
        }
    }
}
