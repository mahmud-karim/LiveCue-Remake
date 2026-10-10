import Foundation
import Observation

/// Meta API key (screens 9).
@MainActor @Observable final class MetaKeyViewModel {
    var keyDraft: String = ""

    init(speech: SpeechSettings) {
        // STUB(WP-11)
    }

    var hasKey: Bool { false } // STUB(WP-11)
    var stateChip: StatusChipModel { StatusChipModel("", tone: .neutral, mark: .none) } // STUB(WP-11)
    var message: InlineMessage? { nil } // STUB(WP-11)
    var isTesting: Bool { false } // STUB(WP-11)
    /// The draft is not empty and no test is running.
    var canSave: Bool { false } // STUB(WP-11)

    /// Clears the draft.
    func appeared() {
        // STUB(WP-11)
    }

    /// Clears the draft on success.
    func save() {
        // STUB(WP-11)
    }

    func test() async {
        // STUB(WP-11)
    }

    func remove() {
        // STUB(WP-11)
    }
}
