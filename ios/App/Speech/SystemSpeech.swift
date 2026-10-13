import Foundation

// Provider `apple` (D-28): the recogniser built into iOS 26. It sits behind a protocol like the other
// on-device engines, so that everything except one file is tested with a fake and runs on iOS 18.

/// What the app knows about the recogniser built into iOS.
enum SystemSpeechAvailability: Equatable, Sendable {
    /// The system has not been asked yet.
    case unknown
    /// It cannot be used here. The reason is shown as it is.
    case unavailable(reason: String)
    /// It can be used once its language asset is downloaded.
    case needsDownload
    /// The language asset is on the phone.
    case installed
}

/// One result of the system recogniser.
struct SystemSpeechResult: Equatable, Sendable {
    let text: String
    /// False for a tentative text, which the next result replaces.
    let isFinal: Bool
    /// The audio the result covers, in seconds since `begin`. Zero when the system does not say.
    let startSeconds: TimeInterval
    let endSeconds: TimeInterval
}

/// Apple.
protocol SystemSpeechRecognizing: Sendable {
    /// What is known without asking the system: `.unavailable` on an iOS that has no such recogniser,
    /// `.unknown` where the system has to be asked.
    var knownAvailability: SystemSpeechAvailability { get }
    /// Asks the system. It shows no prompt and downloads nothing.
    func availability() async -> SystemSpeechAvailability
    /// Downloads and installs the language asset when it is missing. `progress` is called off the main
    /// actor.
    func install(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws
    /// Starts one recognition. The stream ends after `finish` or `cancel`, and throws when the
    /// recogniser fails.
    func begin() async throws -> AsyncThrowingStream<SystemSpeechResult, any Error>
    /// 16 kHz mono; text arrives through the stream of `begin`.
    func process(samples: [Float]) async throws
    /// Finalises everything given so far. The stream delivers the last results and then ends.
    func finish() async throws
    /// Ends the recognition at once. The stream ends.
    func cancel() async
}

/// An error of the system recogniser with a text of its own.
struct SystemSpeechFailure: LocalizedError, Equatable {
    let errorDescription: String?

    init(_ text: String) {
        errorDescription = text
    }
}

/// The texts of the system recogniser that are not the system's own.
enum SystemSpeechText {
    static let requiresNewerSystem = "Requires iOS 26 or later"
    static let unsupportedDevice = "Not supported on this iPhone"
    static let noEnglish = "English is not available for Apple speech on this iPhone"
    static let stoppedLastTime = "Apple speech closed the app last time. Restart LiveCue to try again."
    static let checking = "Checking Apple speech…"
    static let notDownloaded = "Not downloaded"
    static let notInstalledYet = "The speech asset is not installed yet. Try again in a moment."
}

/// The recogniser of an iPhone that has none: iOS 18 to 25. Nothing here touches the system.
struct UnavailableSystemSpeech: SystemSpeechRecognizing {
    private let reason: String

    init(reason: String = SystemSpeechText.requiresNewerSystem) {
        self.reason = reason
    }

    var knownAvailability: SystemSpeechAvailability {
        .unavailable(reason: reason)
    }

    func availability() async -> SystemSpeechAvailability {
        .unavailable(reason: reason)
    }

    func install(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws {
        throw SystemSpeechFailure(reason)
    }

    func begin() async throws -> AsyncThrowingStream<SystemSpeechResult, any Error> {
        throw SystemSpeechFailure(reason)
    }

    func process(samples: [Float]) async throws {}

    func finish() async throws {}

    func cancel() async {}
}

/// Remembers, across launches, a question to the system recogniser that was never answered.
///
/// The app runs inside a container app whose identity and permissions are the container's. Should the
/// system ever end the process for asking, the next launch would ask again and end again. The mark is
/// set before the question and removed after the answer; a launch that finds it set does not ask.
struct SpeechProbeMark: Sendable {
    static let key = "appleSpeechProbeUnanswered"

    private let defaults: UserDefaults

    init(defaults: UserDefaults) {
        self.defaults = defaults
    }

    /// True when the last question was never answered. The mark is removed, so the launch after this
    /// one asks again.
    func takeUnanswered() -> Bool {
        let unanswered = defaults.bool(forKey: SpeechProbeMark.key)
        if unanswered {
            defaults.removeObject(forKey: SpeechProbeMark.key)
        }
        return unanswered
    }

    func asking() {
        defaults.set(true, forKey: SpeechProbeMark.key)
        // Written out now: the mark is only of use if it outlives a process that ends right after.
        _ = defaults.synchronize()
    }

    func answered() {
        defaults.removeObject(forKey: SpeechProbeMark.key)
    }
}
