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

    public mutating func begin(_ id: UUID) {
        // STUB(WP-02)
    }

    public mutating func update(_ id: UUID, sentMs: Double? = nil, processedMs: Double? = nil, hasTranscript: Bool = false) {
        // STUB(WP-02)
    }

    public mutating func finish(_ id: UUID, completed: Bool) {
        // STUB(WP-02)
    }

    public mutating func apply(_ update: UsageUpdate) {
        // STUB(WP-02)
    }

    public mutating func interruptOpenStreams() {
        // STUB(WP-02)
    }

    public var estimatedSeconds: Int {
        // STUB(WP-02)
        0
    }

    public var estimatedUSD: Double {
        // STUB(WP-02)
        0
    }

    public var reportedSeconds: Int {
        // STUB(WP-02)
        0
    }

    public var isIncomplete: Bool {
        // STUB(WP-02)
        false
    }

    /// "$0.00155": five decimals, "." in every locale.
    public var formattedCost: String {
        // STUB(WP-02)
        "$0.00000"
    }
}
