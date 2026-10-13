import LiveCueCore
import SwiftUI

/// The details of one compared answer, with its timing shown open.
///
/// Question and answer came from a model and are drawn as they are: nothing in them is read as
/// formatting or as a link.
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
                    Text(verbatim: turn.answer)
                        .lcText(.cue)
                        .fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
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
