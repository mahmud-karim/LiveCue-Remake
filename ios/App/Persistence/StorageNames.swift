import Foundation

/// Keys, accounts and file names, live and test.
enum StorageNames {
    static let keychainService = "com.mahmudkarim.LiveCueRemake"

    struct Names: Equatable, Sendable {
        let pairingAccount: String
        let metaKeyAccount: String
        let openRouterKeyAccount: String
        let pairingFileName: String
    }

    static let live = Names(
        pairingAccount: "relayPairing",
        metaKeyAccount: "meta-api-key",
        openRouterKeyAccount: "openrouter-api-key",
        pairingFileName: "pairedPC.json"
    )

    static let test = Names(
        pairingAccount: "relayPairing-tests",
        metaKeyAccount: "meta-api-key-ui-test",
        openRouterKeyAccount: "openrouter-api-key-ui-test",
        pairingFileName: "pairedPC-tests.json"
    )

    static let instructionKey = "assistantInstruction"
    static let speechProviderKey = "speechProvider"
    /// The current selection. Per provider: this key + ".codex" and + ".openrouter".
    static let configurationKey = "assistantConfiguration"
}
