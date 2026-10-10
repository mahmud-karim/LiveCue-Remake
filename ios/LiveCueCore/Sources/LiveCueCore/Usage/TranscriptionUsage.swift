import Foundation

/// The transcription usage of one session (spec 02 section 9).
public struct TranscriptionUsage: Codable, Equatable, Sendable {
    public var provider: String
    public var hourlyRateUSD: Double
    public var pricingDate: String
    public var firstTextMs: Double?
    public var finalizationMs: Double?
    public private(set) var streams: [SpeechStreamUsage]

    /// Rate 0.18 for "meta", otherwise 0; pricing date "2026-09-28".
    public init(provider: String = "meta") {
        self.provider = provider
        self.hourlyRateUSD = provider == "meta" ? 0.18 : 0
        self.pricingDate = "2026-09-28"
        self.firstTextMs = nil
        self.finalizationMs = nil
        self.streams = []
    }

    /// Appends a stream in state "streaming". A stream with that id that already exists is left alone.
    public mutating func begin(_ id: UUID) {
        guard !streams.contains(where: { $0.id == id }) else { return }
        streams.append(SpeechStreamUsage(id: id))
    }

    /// Ignored unless the stream exists and is "streaming". The two positions are cumulative: each only
    /// ever rises to the larger of the old and the new value, and a negative, NaN or infinite value is
    /// ignored. `hasTranscript` is OR-ed in.
    public mutating func update(_ id: UUID, sentMs: Double? = nil, processedMs: Double? = nil, hasTranscript: Bool = false) {
        guard let index = openStreamIndex(id) else { return }
        if let sentMs, Self.isPosition(sentMs) {
            streams[index].sentMs = max(streams[index].sentMs, sentMs)
        }
        if let processedMs, Self.isPosition(processedMs) {
            streams[index].processedMs = max(streams[index].processedMs ?? 0, processedMs)
        }
        if hasTranscript {
            streams[index].hasTranscript = true
        }
    }

    /// Ignored unless the stream exists and is "streaming". Terminal: nothing changes the stream afterwards.
    public mutating func finish(_ id: UUID, completed: Bool) {
        guard let index = openStreamIndex(id) else { return }
        streams[index].state = completed ? SpeechStreamUsage.completed : SpeechStreamUsage.interrupted
    }

    /// The same as the direct calls for the three stream events. `firstText` is applied once per session,
    /// `finalization` replaces the previous value. As for the positions, a time that is negative, NaN or
    /// infinite is ignored (it could not be saved: JSON has no such number).
    public mutating func apply(_ update: UsageUpdate) {
        switch update {
        case .begin(let id):
            begin(id)
        case .update(let id, let sentMs, let processedMs, let hasTranscript):
            self.update(id, sentMs: sentMs, processedMs: processedMs, hasTranscript: hasTranscript)
        case .finish(let id, let completed):
            finish(id, completed: completed)
        case .firstText(let milliseconds):
            if firstTextMs == nil, Self.isPosition(milliseconds) {
                firstTextMs = milliseconds
            }
        case .finalization(let milliseconds):
            if Self.isPosition(milliseconds) {
                finalizationMs = milliseconds
            }
        }
    }

    /// Every stream still "streaming" becomes "interrupted" (launch recovery after an app kill).
    public mutating func interruptOpenStreams() {
        for index in streams.indices where streams[index].state == SpeechStreamUsage.streaming {
            streams[index].state = SpeechStreamUsage.interrupted
        }
    }

    /// The sum of the billable whole seconds of every stream. Rounding down happens per stream.
    public var estimatedSeconds: Int {
        Self.count(streams.reduce(0.0) { $0 + Self.billableSeconds(of: $1) })
    }

    /// Estimated seconds at the stored hourly rate.
    public var estimatedUSD: Double {
        Double(estimatedSeconds) * hourlyRateUSD / 3_600
    }

    /// The sum of the whole processed seconds of every stream, whatever its state.
    public var reportedSeconds: Int {
        Self.count(streams.reduce(0.0) { $0 + Self.wholeSeconds($1.processedMs ?? 0) })
    }

    /// True when a stream is "interrupted", or is "completed" without a processed position.
    public var isIncomplete: Bool {
        streams.contains { stream in
            stream.state == SpeechStreamUsage.interrupted
                || (stream.state == SpeechStreamUsage.completed && stream.processedMs == nil)
        }
    }

    /// "$0.00155": five decimals, "." in every locale.
    public var formattedCost: String {
        "$" + String(format: "%.5f", locale: Locale(identifier: "en_US_POSIX"), estimatedUSD)
    }

    // MARK: - Rules

    private func openStreamIndex(_ id: UUID) -> Int? {
        streams.firstIndex { $0.id == id && $0.state == SpeechStreamUsage.streaming }
    }

    /// A cumulative position or a duration in milliseconds: finite and not negative.
    private static func isPosition(_ milliseconds: Double) -> Bool {
        milliseconds.isFinite && milliseconds >= 0
    }

    /// Spec 02 section 9.3. While streaming, the larger of sent and processed; an interrupted stream that
    /// produced no text is not billed; otherwise the processed audio, or the sent audio when the provider
    /// reported none.
    private static func billableSeconds(of stream: SpeechStreamUsage) -> Double {
        if stream.state == SpeechStreamUsage.streaming {
            return wholeSeconds(max(stream.sentMs, stream.processedMs ?? 0))
        }
        if stream.state == SpeechStreamUsage.interrupted, !stream.hasTranscript {
            return 0
        }
        return wholeSeconds(stream.processedMs ?? stream.sentMs)
    }

    /// Milliseconds rounded down to whole seconds, never negative. Kept as a `Double` so that a stored
    /// value of any size cannot overflow.
    private static func wholeSeconds(_ milliseconds: Double) -> Double {
        let seconds = (milliseconds / 1_000).rounded(.down)
        return seconds > 0 ? seconds : 0
    }

    /// A sum of whole seconds as an `Int`, saturating instead of trapping on a stored value out of range.
    private static func count(_ seconds: Double) -> Int {
        guard seconds > 0 else { return 0 }
        return Int(exactly: seconds) ?? Int.max
    }
}
