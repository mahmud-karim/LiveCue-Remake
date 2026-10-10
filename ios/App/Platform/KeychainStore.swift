import Foundation
import Security

struct KeychainFailure: Error, Equatable, Sendable {
    let status: Int32
}

/// Generic-password items of one service: accessible after first unlock, this device only, no access
/// group, no synchronisation. `set` updates an existing item and adds it when absent.
struct KeychainStore: Sendable {
    let service: String

    init(service: String = StorageNames.keychainService) {
        self.service = service
    }

    /// Throws `KeychainFailure`.
    func set(_ value: String, account: String) throws {
        let query = itemQuery(account: account)
        let attributes: [String: Any] = [
            kSecValueData as String: Data(value.utf8),
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        ]

        let updated = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if updated == errSecSuccess {
            return
        }
        guard updated == errSecItemNotFound else {
            throw KeychainFailure(status: updated)
        }

        var item = query
        for (key, attribute) in attributes {
            item[key] = attribute
        }
        let added = SecItemAdd(item as CFDictionary, nil)
        guard added == errSecSuccess else {
            throw KeychainFailure(status: added)
        }
    }

    func get(account: String) -> String? {
        var query = itemQuery(account: account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var found: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &found) == errSecSuccess,
              let data = found as? Data
        else { return nil }
        return String(data: data, encoding: .utf8)
    }

    /// Throws `KeychainFailure`; "not found" is success.
    func delete(account: String) throws {
        let status = SecItemDelete(itemQuery(account: account) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw KeychainFailure(status: status)
        }
    }

    private func itemQuery(account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
    }
}
