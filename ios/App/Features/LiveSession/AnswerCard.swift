import LiveCueCore
import SwiftUI

/// One saved answer, with its timing disclosure, "Copy answer" and the same-text retry.
struct AnswerCard: View {
    private let turn: AssistantTurn
    private let isLatest: Bool
    private let canRetry: Bool
    private let onRetry: @MainActor () -> Void
    private let onCopy: @MainActor () -> Void

    init(
        turn: AssistantTurn,
        isLatest: Bool,
        canRetry: Bool,
        onRetry: @escaping @MainActor () -> Void,
        onCopy: @escaping @MainActor () -> Void
    ) {
        self.turn = turn
        self.isLatest = isLatest
        self.canRetry = canRetry
        self.onRetry = onRetry
        self.onCopy = onCopy
    }

    var body: some View {
        // STUB(WP-10)
        Card(prominent: isLatest) {
            Text(turn.answer).lcText(.cue)
        }
    }
}
