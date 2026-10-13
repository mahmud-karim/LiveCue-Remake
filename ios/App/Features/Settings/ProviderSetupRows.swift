import LiveCueCore
import SwiftUI

/// The setup rows of the selected speech provider. Shared by Settings and Transcription.
struct ProviderSetupState: Equatable {
    enum Row: Equatable {
        /// "Saved on iPhone" / "Required"
        case metaKey(value: String, needsAttention: Bool)
        /// The home label.
        case pcSpeechModel(value: String)
        /// "Ready" / "Not loaded"; for Apple "Ready", "Not downloaded" or why it cannot be used.
        case modelLibrary(value: String)
        case benchmark
    }

    let rows: [Row]

    /// `modelNote` is Apple's: why its recogniser cannot start, when that is not a missing download.
    init(provider: SpeechProvider, hasMetaKey: Bool, pcModelLabel: String, isModelLoaded: Bool, modelNote: String? = nil) {
        switch provider {
        case .meta:
            rows = [.metaKey(value: hasMetaKey ? "Saved on iPhone" : "Required", needsAttention: !hasMetaKey)]
        case .nemotron, .qwen3:
            rows = [.pcSpeechModel(value: pcModelLabel)]
        case .voz, .parakeet:
            rows = [.modelLibrary(value: isModelLoaded ? "Ready" : "Not loaded"), .benchmark]
        case .apple:
            let missing = modelNote ?? SystemSpeechText.notDownloaded
            rows = [.modelLibrary(value: isModelLoaded ? "Ready" : missing), .benchmark]
        }
    }
}

/// One navigating row per thing the provider needs. Inside a panel the rows join the rows around them.
struct ProviderSetupRows: View {
    private let state: ProviderSetupState

    init(state: ProviderSetupState) {
        self.state = state
    }

    var body: some View {
        ForEach(Array(state.rows.enumerated()), id: \.offset) { item in
            ProviderSetupRow(row: item.element)
        }
    }
}

private struct ProviderSetupRow: View {
    let row: ProviderSetupState.Row

    var body: some View {
        switch row {
        case .metaKey(let value, let needsAttention):
            LCNavigationRow(
                route: Route.metaKey,
                title: "Meta API key",
                value: value,
                valueTone: needsAttention ? StatusTone.warning : nil,
                systemImage: "key",
                identifier: "meta-key-settings"
            )
        case .pcSpeechModel(let value):
            // The home label goes below the title: "Stopped · open Settings" does not fit beside it.
            LCNavigationRow(route: Route.pcSpeechModel, title: "PC speech model", systemImage: "cpu") {
                Text(verbatim: value)
            }
        case .modelLibrary(let value):
            LCNavigationRow(
                route: Route.modelLibrary,
                title: "Model Library",
                value: value,
                systemImage: "arrow.down.circle"
            )
        case .benchmark:
            LCNavigationRow(route: Route.benchmark, title: "Speech benchmark", systemImage: "chart.bar")
        }
    }
}
