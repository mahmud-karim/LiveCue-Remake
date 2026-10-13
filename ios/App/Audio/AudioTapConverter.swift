import AVFoundation

/// Owns the `AVAudioConverter` and the two continuations of one capture, and builds the tap block off the
/// main actor, so the block that runs on the audio thread is not main-actor isolated.
///
/// Every buffer the tap delivers is converted to the target format and handed to the chunk queue, which
/// holds at most `queueLimit` buffers. A buffer that does not fit is refused, and the capture says so
/// once with `.overflow`. Nothing is kept anywhere else: audio is never written to disk.
///
/// Why it is safe to share: the continuations are thread-safe by themselves, and everything that changes
/// after `init` is changed under `lock`.
final class AudioTapConverter: @unchecked Sendable {
    /// What the capture hands to its caller.
    let streams: AudioCaptureStreams

    private let target: AudioTarget
    private let outputFormat: AVAudioFormat
    private let chunks: AsyncStream<AudioChunk>.Continuation
    private let events: AsyncStream<AudioCaptureEvent>.Continuation
    /// A level is published when at least this many converted frames passed since the last one.
    private let framesPerLevel: Int

    private let lock = NSLock()
    // Guarded by `lock`.
    private var inputFormat: AVAudioFormat
    private var converter: AVAudioConverter
    /// The buffer the converter has not taken yet, during one `convert`.
    private var pendingInput: AVAudioPCMBuffer?
    private var framesSinceLevel: Int
    private var overflowed = false

    /// nil when the input format is unusable or cannot be converted to the target.
    init?(inputFormat: AVAudioFormat, request: AudioCaptureRequest) {
        let commonFormat: AVAudioCommonFormat
        let sampleRate: Double
        switch request.target {
        case .pcm16(let rate):
            commonFormat = .pcmFormatInt16
            sampleRate = rate
        case .float32(let rate):
            commonFormat = .pcmFormatFloat32
            sampleRate = rate
        }

        guard inputFormat.sampleRate > 0, inputFormat.channelCount > 0, sampleRate > 0,
              let outputFormat = AVAudioFormat(commonFormat: commonFormat, sampleRate: sampleRate, channels: 1, interleaved: false),
              let converter = AVAudioConverter(from: inputFormat, to: outputFormat) else {
            return nil
        }

        let (chunkStream, chunks) = AsyncStream<AudioChunk>.makeStream(
            bufferingPolicy: .bufferingOldest(max(1, request.queueLimit))
        )
        let (eventStream, events) = AsyncStream<AudioCaptureEvent>.makeStream(bufferingPolicy: .unbounded)
        let framesPerLevel = max(1, Int((sampleRate / AudioSessionPolicy.levelsPerSecond).rounded(.up)))

        self.streams = AudioCaptureStreams(chunks: chunkStream, events: eventStream)
        self.target = request.target
        self.outputFormat = outputFormat
        self.chunks = chunks
        self.events = events
        self.framesPerLevel = framesPerLevel
        self.inputFormat = inputFormat
        self.converter = converter
        self.pendingInput = nil
        // The first buffer publishes a level at once.
        self.framesSinceLevel = framesPerLevel
    }

    /// The block for `installTap`. It is made here, outside any actor, and captures only the converter.
    nonisolated static func makeTapBlock(for converter: AudioTapConverter) -> AVAudioNodeTapBlock {
        return { buffer, _ in
            converter.process(buffer)
        }
    }

    /// Reports an interruption or a lost route. Called from the notification's thread.
    func send(_ event: AudioCaptureEvent) {
        events.yield(event)
    }

    /// Ends both streams. Buffers already queued stay readable.
    func finish() {
        chunks.finish()
        events.finish()
    }

    // MARK: - Levels

    /// RMS of 16-bit samples as a fraction of full scale, 0...1.
    static func level(ofPCM16 samples: UnsafeBufferPointer<Int16>) -> Float {
        guard !samples.isEmpty else { return 0 }
        var sum = 0.0
        for sample in samples {
            let value = Double(sample) / 32_768
            sum += value * value
        }
        return clamped((sum / Double(samples.count)).squareRoot())
    }

    /// RMS of float samples, 0...1.
    static func level(ofFloat samples: UnsafeBufferPointer<Float>) -> Float {
        guard !samples.isEmpty else { return 0 }
        var sum = 0.0
        for sample in samples {
            let value = Double(sample)
            sum += value * value
        }
        return clamped((sum / Double(samples.count)).squareRoot())
    }

    private static func clamped(_ value: Double) -> Float {
        guard value.isFinite else { return 0 }
        return Float(min(max(value, 0), 1))
    }

    // MARK: - The audio thread

    private func process(_ buffer: AVAudioPCMBuffer) {
        lock.lock()
        defer { lock.unlock() }

        guard buffer.frameLength > 0, let output = convert(buffer) else { return }
        let frames = Int(output.frameLength)
        guard frames > 0 else { return }

        let chunk: AudioChunk
        let level: Float
        switch target {
        case .pcm16:
            guard let channels = output.int16ChannelData else { return }
            let samples = UnsafeBufferPointer(start: channels[0], count: frames)
            level = AudioTapConverter.level(ofPCM16: samples)
            // The bytes of the samples as they are in memory: little-endian on every iPhone.
            chunk = .pcm16(Data(buffer: samples))
        case .float32:
            guard let channels = output.floatChannelData else { return }
            let samples = UnsafeBufferPointer(start: channels[0], count: frames)
            level = AudioTapConverter.level(ofFloat: samples)
            chunk = .float32(Array(samples))
        }

        // The queue keeps the oldest buffers: a buffer that does not fit is refused, and said once.
        if case .dropped = chunks.yield(chunk), !overflowed {
            overflowed = true
            events.yield(.overflow)
        }

        framesSinceLevel += frames
        if framesSinceLevel >= framesPerLevel {
            framesSinceLevel = 0
            events.yield(.level(level))
        }
    }

    /// Converts one tap buffer to the target format. The converter is kept between buffers, so the
    /// resampler runs on across them.
    private func convert(_ buffer: AVAudioPCMBuffer) -> AVAudioPCMBuffer? {
        // The hardware format can change under a running tap. The converter follows the buffers.
        if buffer.format != inputFormat {
            guard buffer.format.sampleRate > 0, buffer.format.channelCount > 0,
                  let replacement = AVAudioConverter(from: buffer.format, to: outputFormat) else {
                return nil
            }
            inputFormat = buffer.format
            converter = replacement
        }

        let ratio = outputFormat.sampleRate / inputFormat.sampleRate
        let capacity = AVAudioFrameCount((Double(buffer.frameLength) * ratio).rounded(.up)) + 64
        guard let output = AVAudioPCMBuffer(pcmFormat: outputFormat, frameCapacity: capacity) else { return nil }

        // The converter asks for input through the block. It gets this one buffer, then "no more for
        // now", which leaves it ready for the next buffer.
        pendingInput = buffer
        defer { pendingInput = nil }
        var failure: NSError?
        let status = converter.convert(to: output, error: &failure) { [self] _, inputStatus in
            guard let next = pendingInput else {
                inputStatus.pointee = .noDataNow
                return nil
            }
            pendingInput = nil
            inputStatus.pointee = .haveData
            return next
        }
        guard status != .error, failure == nil else { return nil }
        return output
    }
}
