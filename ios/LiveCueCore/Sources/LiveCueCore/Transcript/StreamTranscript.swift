import Foundation

/// The caption merge state machine: spec 02 section 6 with CORE-A7 and CORE-A8. A fresh value is made for
/// every stream.
public struct StreamTranscript: Sendable {
    /// The turn most recently started.
    private var activeTurn: Int?
    /// The latest partial text of each unfinished turn.
    private var partials: [Int: String] = [:]
    /// The start of each started turn, in seconds on the provider's processed-audio clock.
    private var starts: [Int: Double] = [:]
    /// Turns already finalised.
    private var completed: Set<Int> = []

    public init() {}

    /// Non-empty partials of unfinished turns, ascending turn number, joined by one space.
    public var partialText: String {
        partials.keys.sorted()
            .compactMap { partials[$0] }
            .filter { !$0.isEmpty }
            .joined(separator: " ")
    }

    /// True while a turn that started has not received its final text. The end of a stream is judged by
    /// it (IOS-A11): with no such turn, nothing that was heard can still be missing.
    public var hasUnfinishedTurn: Bool {
        starts.keys.contains { !completed.contains($0) }
    }

    /// `transcript` updates the turn its `turn` names when that turn is open (started and not completed),
    /// otherwise the active turn when it is open, otherwise nothing. `speechComplete` for a turn not yet
    /// completed marks it completed and returns a segment when the trimmed text is not empty
    /// (start = the turn's recorded start or 0, end = processedMs / 1000).
    public mutating func apply(_ event: StreamTranscriptEvent) -> TranscriptSegment? {
        switch event {
        case .speechStart(let turn, let processedMs):
            guard let turn else { return nil }
            activeTurn = turn
            starts[turn] = processedMs / 1000
            return nil

        case .transcript(let turn, let text, _):
            // Partials are cumulative: each one replaces the turn's previous partial.
            if let named = turn, isOpen(named) {
                partials[named] = text ?? ""
            } else if let active = activeTurn, isOpen(active) {
                partials[active] = text ?? ""
            }
            return nil

        case .speechComplete(let turn, let text, let processedMs):
            guard let turn, !completed.contains(turn) else { return nil }
            completed.insert(turn)
            partials[turn] = nil

            // The final text replaces the partial entirely. An empty final still completes the turn.
            let trimmed = (text ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            guard !trimmed.isEmpty else { return nil }
            return TranscriptSegment(
                text: trimmed,
                startSeconds: starts[turn] ?? 0,
                endSeconds: processedMs / 1000
            )

        case .other:
            return nil
        }
    }

    /// Started and not completed.
    private func isOpen(_ turn: Int) -> Bool {
        starts[turn] != nil && !completed.contains(turn)
    }
}
