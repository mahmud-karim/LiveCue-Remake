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

enum AudioCaptureFailure: Error, Equatable, Sendable {
    /// Zero sample rate or zero channels: "No usable microphone."
    case noUsableMicrophone
    /// The audio session or the engine refused to start: same text.
    case couldNotStart
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
    /// with a 2,048-frame buffer and starts the engine. Throws `AudioCaptureFailure`.
    func start(_ request: AudioCaptureRequest) throws -> AudioCaptureStreams
    /// Removes the tap, stops the engine and finishes both streams; buffers already queued stay readable.
    /// `releaseSession` deactivates the audio session, notifying other apps.
    func stop(releaseSession: Bool)
}
