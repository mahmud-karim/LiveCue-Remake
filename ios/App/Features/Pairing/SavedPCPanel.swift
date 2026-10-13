import SwiftUI

/// The Saved PC panel: state, address, message, "Reconnect" and the disclosure of the pairing options.
struct SavedPCPanel: View {
    @Environment(PairingViewModel.self) private var model

    private static let showOptions = "Pair another PC or scan a code"
    private static let hideOptions = "Hide pairing options"

    var body: some View {
        @Bindable var model = model

        Panel("Saved PC") {
            StatusChip(model.savedStateChip)
                .accessibilityIdentifier("saved-pc-state")
            SavedPCAddressLine(address: model.savedAddress)
            SavedPCMessageLine(text: model.savedMessage)
            LCButton(
                "Reconnect",
                systemImage: "arrow.clockwise",
                kind: .secondary,
                isBusy: model.isChecking,
                busyTitle: "Checking…",
                action: reconnect
            )
            .accessibilityIdentifier("reconnect-pc")
            // The two panels it reveals are below this one, so the row has no content of its own.
            DisclosureRow(
                model.showsOptions ? SavedPCPanel.hideOptions : SavedPCPanel.showOptions,
                isExpanded: $model.showsOptions,
                identifier: "show-pairing-options",
                collapsedLabel: SavedPCPanel.showOptions,
                expandedLabel: SavedPCPanel.hideOptions
            ) {
                EmptyView()
            }
        }
    }

    private func reconnect() {
        Task {
            await model.reconnect()
        }
    }
}

/// The saved address: monospaced and selectable, shown exactly as saved.
private struct SavedPCAddressLine: View {
    let address: String

    var body: some View {
        Text(verbatim: address)
            .lcText(.mono)
            .textSelection(.enabled)
            .frame(maxWidth: .infinity, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
            .accessibilityIdentifier("saved-pc-address")
    }
}

/// The connection message of the saved PC.
///
/// After a failed check it can be the relay's own text for a code this build does not know, so it is
/// drawn as it is: nothing in it is read as formatting or becomes a link.
struct SavedPCMessageLine: View {
    private let text: String

    init(text: String) {
        self.text = text
    }

    var body: some View {
        Text(verbatim: text)
            .lcText(.body)
            .frame(maxWidth: .infinity, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
    }
}
