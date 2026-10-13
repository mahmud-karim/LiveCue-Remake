import Foundation
import LiveCueCore

/// The numbers of an answer as text. Every number is formatted with the POSIX locale, so the decimal
/// separator is "." everywhere.
enum AnswerFormat {
    /// "1.482 s" from milliseconds. A value that is negative or not a number shows as zero.
    static func seconds(milliseconds: Double, digits: Int) -> String {
        fixed(milliseconds / 1_000, digits: digits) + " s"
    }

    /// "38 ms"
    static func milliseconds(_ value: Double) -> String {
        fixed(value, digits: 0) + " ms"
    }

    /// "$0.000412"
    static func dollars(_ credits: Double) -> String {
        "$" + fixed(credits, digits: 6)
    }

    /// "0.000412 credits"
    static func credits(_ credits: Double) -> String {
        fixed(credits, digits: 6) + " credits"
    }

    /// The cost a service reported for this answer; nil when it reported none.
    static func reportedCost(of turn: AssistantTurn) -> Double? {
        guard let cost = turn.performance?.execution?.usage?.costCredits, cost.isFinite, cost >= 0 else {
            return nil
        }
        return cost
    }

    /// "1.48 s", the total time of a card; nil without a timing record.
    static func total(of turn: AssistantTurn) -> String? {
        guard let performance = turn.performance else {
            return nil
        }
        return seconds(milliseconds: performance.totalMs, digits: 2)
    }

    /// "1.48 s · $0.000412", or the time alone when no cost was reported; nil without a timing record.
    static func totalAndCost(of turn: AssistantTurn) -> String? {
        guard let total = total(of: turn) else {
            return nil
        }
        guard let cost = reportedCost(of: turn) else {
            return total
        }
        return total + " · " + dollars(cost)
    }

    /// The detected question as a card shows it: nil for the sentinel of OpenRouter answers and for an
    /// empty text.
    static func visibleQuestion(of turn: AssistantTurn) -> String? {
        let question = turn.detectedQuestion
        if question.isEmpty || question == AssistantTurn.latestConversation {
            return nil
        }
        return question
    }

    private static func fixed(_ value: Double, digits: Int) -> String {
        let shown = value.isFinite ? max(0, value) : 0
        return String(format: "%.\(digits)f", locale: Locale(identifier: "en_US_POSIX"), shown)
    }
}
