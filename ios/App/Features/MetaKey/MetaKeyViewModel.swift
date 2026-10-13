import Foundation
import Observation

/// Meta API key (screens 9): save, test and remove the key.
///
/// The key itself is only ever in the draft while it is typed and in the Keychain once saved. The
/// actions and their messages are those of `SpeechSettings`.
@MainActor @Observable final class MetaKeyViewModel {
    var keyDraft: String = ""

    private let speech: SpeechSettings

    init(speech: SpeechSettings) {
        self.speech = speech
    }

    var hasKey: Bool {
        speech.hasMetaKey
    }

    var stateChip: StatusChipModel {
        if speech.hasMetaKey {
            return StatusChipModel("Key saved", tone: .success, mark: .check)
        }
        return StatusChipModel("No key saved", tone: .neutral, mark: .ring)
    }

    var message: InlineMessage? {
        speech.metaKeyMessage
    }

    var isTesting: Bool {
        speech.isTestingMetaKey
    }

    /// The draft is not empty and no test is running.
    var canSave: Bool {
        !keyDraft.isEmpty && !speech.isTestingMetaKey
    }

    /// Clears the draft.
    func appeared() {
        keyDraft = ""
    }

    /// Clears the draft on success.
    func save() {
        if speech.saveMetaKey(keyDraft) {
            keyDraft = ""
        }
    }

    func test() async {
        await speech.testMetaKey()
    }

    func remove() {
        speech.removeMetaKey()
    }
}
