import Foundation
import LiveCueCore

/// What Session detail shows. Pure, so it is unit-tested without a view.
struct SessionDetailContent: Equatable {
    struct Fact: Equatable, Identifiable {
        let id: String
        let key: String
        let value: String
    }

    let title: String
    /// Empty when no usage was recorded.
    let usageFacts: [Fact]
    /// S-14 or S-15, Meta only.
    let usageCaption: String?
    let summary: String?
    let keyPoints: [String]
    let actionItems: [String]
    let segments: [TranscriptSegment]
    let turns: [AssistantTurn]

    init(session: Session) {
        title = session.title
        usageFacts = SessionDetailContent.facts(of: session.transcriptionUsage)
        usageCaption = SessionDetailContent.caption(of: session.transcriptionUsage)
        let summaryText = session.notes.summary.trimmingCharacters(in: .whitespacesAndNewlines)
        summary = summaryText.isEmpty ? nil : summaryText
        keyPoints = SessionDetailContent.nonEmpty(session.notes.keyPoints)
        actionItems = SessionDetailContent.nonEmpty(session.notes.actionItems)
        segments = session.segments
        turns = session.assistantTurns
    }

    // MARK: - Usage (spec 01 section 1.12)

    /// The rows of "Transcription usage", in the order of the screen. Cost, reported audio, streams and
    /// the rate are shown for Meta only; the two timings only when they are known.
    private static func facts(of usage: TranscriptionUsage?) -> [Fact] {
        guard let usage else { return [] }
        let isMeta = usage.provider == SpeechProvider.meta.rawValue
        var facts: [Fact] = []

        if isMeta {
            facts.append(Fact(id: "cost", key: "Estimated cost", value: SessionTexts.cost(usage)))
        }
        let name = SpeechProvider(rawValue: usage.provider)?.displayName ?? "On-device"
        facts.append(Fact(id: "provider", key: "Provider", value: name))
        if let firstText = usage.firstTextMs {
            facts.append(Fact(id: "first-words", key: "First words from stream start", value: seconds(firstText)))
        }
        if let finalization = usage.finalizationMs {
            facts.append(Fact(id: "finalization", key: "Last stream finalization", value: seconds(finalization)))
        }
        if isMeta {
            facts.append(Fact(id: "reported", key: "Processed audio reported", value: "\(usage.reportedSeconds) seconds"))
            facts.append(Fact(id: "streams", key: "Streams", value: "\(usage.streams.count)"))
            facts.append(Fact(id: "rate", key: "Rate saved with session", value: rate(usage.hourlyRateUSD)))
        }
        return facts
    }

    private static func caption(of usage: TranscriptionUsage?) -> String? {
        guard let usage, usage.provider == SpeechProvider.meta.rawValue else { return nil }
        return usage.isIncomplete ? incompleteCaption : completeCaption
    }

    /// "1.20 s": milliseconds as seconds with two decimals, "." in every locale.
    private static func seconds(_ milliseconds: Double) -> String {
        String(format: "%.2f s", locale: Locale(identifier: "en_US_POSIX"), milliseconds / 1_000)
    }

    /// "$0.18 / hour"
    private static func rate(_ hourlyRateUSD: Double) -> String {
        String(format: "$%.2f / hour", locale: Locale(identifier: "en_US_POSIX"), hourlyRateUSD)
    }

    private static func nonEmpty(_ items: [String]) -> [String] {
        items.filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }

    /// S-14.
    private static let completeCaption = "Calculated from reported audio usage; not a confirmed charge. Credits and billing adjustments may change your bill."
    /// S-15.
    private static let incompleteCaption = "Connection ended before all usage could be confirmed. This is an incomplete estimate, not a Meta invoice."
}
