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
        switch provider {
        case .meta:
            rows = [.metaKey(value: hasMetaKey ? "Saved on iPhone" : "Required", needsAttention: !hasMetaKey)]
        case .nemotron, .qwen3:
            rows = [.pcSpeechModel(value: pcModelLabel)]
        case .voz, .parakeet:
            rows = [.modelLibrary(value: isModelLoaded ? "Ready" : "Not loaded"), .benchmark]
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
            LCNavigationRow(route: Route.pcSpeechModel, title: "PC speech model", value: value, systemImage: "cpu")
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
