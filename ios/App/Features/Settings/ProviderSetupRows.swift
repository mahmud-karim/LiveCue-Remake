import LiveCueCore
import SwiftUI

/// The setup rows of the selected speech provider. Shared by Settings and Transcription.
struct ProviderSetupState: Equatable {
    enum Row: Equatable {
        /// "Saved on iPhone" / "Required"
        case metaKey(value: String, needsAttention: Bool)
        /// The home label.
        case pcSpeechModel(value: String)
        /// "Ready" / "Not loaded"
        case modelLibrary(value: String)
        case benchmark
    }

    let rows: [Row]

    init(provider: SpeechProvider, hasMetaKey: Bool, pcModelLabel: String, isModelLoaded: Bool) {
        // STUB(WP-11)
        rows = []
    }
}

struct ProviderSetupRows: View {
    private let state: ProviderSetupState

    init(state: ProviderSetupState) {
        self.state = state
    }

    var body: some View {
        // STUB(WP-11)
        EmptyView()
    }
}
