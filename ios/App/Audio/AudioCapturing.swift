import Foundation

enum AudioTarget: Equatable, Sendable {
    /// Little-endian Int16 mono bytes: Meta 24,000, PC speech 16,000.
    case pcm16(sampleRate: Double)
    /// Mono Float samples: on-device 16,000.
    case float32(sampleRate: Double)
}

enum AudioChunk: Sendable {
    case pcm16(Data)
    case float32([Float])
}

struct AudioCaptureRequest: Equatable, Sendable {
    let target: AudioTarget
    /// 64 streaming, 128 on-device.
    let queueLimit: Int
}

enum AudioCaptureEvent: Equatable, Sendable {
    /// RMS 0...1 of a converted buffer, at most 24 per second.
    case level(Float)
    /// A converted buffer did not fit in the queue; sent once.
    case overflow
    /// An audio session interruption began.
    case interrupted
    /// Route change with the reason "old device unavailable".
    case routeLost
}

/// What the audio session and the engine said when a capture could not start. It holds no name of a
/// device or of a person: a route is given by its port types, such as "MicrophoneBuiltIn".
struct AudioStartReport: Equatable, Sendable {
    enum Stage: Equatable, Sendable {
        /// The audio session refused its category or its activation.
        case session(code: Int)
        /// The input had no usable format on any attempt, or no converter could be built for it.
        case input
        /// The engine refused to start.
        case engine(code: Int)
    }

    let stage: Stage
    /// What the audio session says about an input being available at all.
    let isInputAvailable: Bool
    /// The port types of the current route's inputs.
    let routeInputs: [String]
    /// The format of the input node at the last attempt; zero when it was never read.
    let sampleRate: Double
    let channelCount: Int
    /// False when no converter from the input format to the target could be built.
    let converterBuilt: Bool
}

enum AudioCaptureFailure: Error, Equatable, Sendable {
    /// Zero sample rate or zero channels: "No usable microphone."
    case noUsableMicrophone
    /// The audio session or the engine refused to start: same text.
    case couldNotStart
    /// What `AudioCapture` throws since 1.1.0: the same two causes, with what iOS said at that moment.
    /// `AudioInputDiagnosis.message(for:)` is its text.
    case unusable(AudioStartReport)
}

struct AudioCaptureStreams: Sendable {
    /// Bounded by `queueLimit`, oldest kept; finishes after `stop`.
    let chunks: AsyncStream<AudioChunk>
    /// Finishes after `stop`.
    let events: AsyncStream<AudioCaptureEvent>
}

@MainActor protocol AudioCapturing: AnyObject {
    var isRunning: Bool { get }
    /// Configures and activates the audio session (play-and-record, measurement, default to speaker,
    /// Bluetooth hands-free, haptics allowed while recording), taps the input node in its native format
    /// with a 2,048-frame buffer and starts the engine. An input that is not usable yet is read again
    /// with a new engine, three times in all, 0.2 s apart. Throws `AudioCaptureFailure`.
    func start(_ request: AudioCaptureRequest) throws -> AudioCaptureStreams
    /// Removes the tap, stops the engine and finishes both streams; buffers already queued stay readable.
    /// `releaseSession` deactivates the audio session, notifying other apps.
    func stop(releaseSession: Bool)
}
