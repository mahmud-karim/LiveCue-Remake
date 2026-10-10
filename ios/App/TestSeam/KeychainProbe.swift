import Foundation
import Security

/// Adds, reads back and deletes one Keychain item, to show whether Keychain calls work in this build.
///
/// It runs only under `-ui-testing`. The project is unsigned by default, and an unsigned simulator
/// build fails here with -34018; CI signs test builds ad hoc, and this is the check that it did.
/// The item has no access group and a service name of its own, as every Keychain item of this app must.
enum KeychainProbe {
    private static let service = "com.mahmudkarim.LiveCueRemake.ui-testing.keychain-probe"
    private static let account = "probe"

    /// "available" after a full round trip, otherwise the step that failed and its status code.
    static func run() -> String {
        let secret = Data("probe".utf8)
        let item: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
        // Clear what an interrupted earlier run may have left behind.
        _ = SecItemDelete(item as CFDictionary)

        var addition = item
        addition[kSecValueData as String] = secret
        addition[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        let added = SecItemAdd(addition as CFDictionary, nil)
        guard added == errSecSuccess else { return "add failed (\(added))" }

        var query = item
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var found: CFTypeRef?
        let copied = SecItemCopyMatching(query as CFDictionary, &found)
        guard copied == errSecSuccess else { return "read failed (\(copied))" }
        guard (found as? Data) == secret else { return "read returned other data" }

        let deleted = SecItemDelete(item as CFDictionary)
        guard deleted == errSecSuccess else { return "delete failed (\(deleted))" }
        return "available"
    }
}
