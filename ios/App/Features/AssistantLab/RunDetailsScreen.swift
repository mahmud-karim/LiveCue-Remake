import LiveCueCore
import SwiftUI

/// The details of one compared answer, with its timing shown open.
///
/// Question and answer came from a model. The answer is drawn through `MarkupText`: its formatting is
/// shown as styling and nothing in it is ever a link. The question is drawn as it is.
struct RunDetailsScreen: View {
    private let turnID: UUID

    @Environment(AssistantLabViewModel.self) private var model

    init(turnID: UUID) {
        self.turnID = turnID
    }

    var body: some View {
        ScreenScaffold {
            if let turn = model.turn(id: turnID) {
                if let question = AnswerFormat.visibleQuestion(of: turn) {
                    Panel("Detected question") {
                        Text(verbatim: question)
                            .lcText(.body)
                            .fixedSize(horizontal: false, vertical: true)
                            .textSelection(.enabled)
                    }
                }
                Panel("Answer") {
                    MarkupText(turn.answer, role: .cue)
                }
                Panel("Response timing") {
                    TimingView(turn: turn)
                }
            }
        }
        .navigationTitle("Run details")
        .navigationBarTitleDisplayMode(.inline)
    }
}
