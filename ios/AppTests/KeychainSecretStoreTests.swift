import XCTest
@testable import LiveCueRemake

/// Keys under the test accounts round-trip and remove.
///
/// The items live under a Keychain service of this test's own, so nothing here touches a key of the app
/// or the placeholder key the test seam saves at launch.
final class KeychainSecretStoreTests: XCTestCase {
    private let keychain = KeychainStore(service: "com.mahmudkarim.LiveCueRemake.unit-tests.secret-store")
    private let names = StorageNames.test

    private var accounts: [String] {
        [
            StorageNames.test.metaKeyAccount, StorageNames.test.openRouterKeyAccount, StorageNames.test.pairingAccount,
            StorageNames.live.metaKeyAccount, StorageNames.live.openRouterKeyAccount, StorageNames.live.pairingAccount
        ]
    }

    override func setUpWithError() throws {
        for account in accounts {
            try keychain.delete(account: account)
        }
    }

    override func tearDownWithError() throws {
        for account in accounts {
            try keychain.delete(account: account)
        }
    }

    @MainActor
    func testKeysRoundTripUnderTheTestAccounts() async throws {
        let store = KeychainSecretStore(keychain: keychain, names: names)
        XCTAssertNil(store.read(.metaAPIKey))
        XCTAssertNil(store.read(.openRouterAPIKey))

        try store.save("meta-key-for-a-test", for: .metaAPIKey)
        try store.save("openrouter-key-for-a-test", for: .openRouterAPIKey)

        XCTAssertEqual(store.read(.metaAPIKey), "meta-key-for-a-test")
        XCTAssertEqual(store.read(.openRouterAPIKey), "openrouter-key-for-a-test")
        // Each key is one Keychain item under its test account, and nothing else was written.
        XCTAssertEqual(keychain.get(account: "meta-api-key-ui-test"), "meta-key-for-a-test")
        XCTAssertEqual(keychain.get(account: "openrouter-api-key-ui-test"), "openrouter-key-for-a-test")
        XCTAssertNil(keychain.get(account: "meta-api-key"))
        XCTAssertNil(keychain.get(account: "openrouter-api-key"))
        XCTAssertNil(keychain.get(account: StorageNames.test.pairingAccount))
    }

    @MainActor
    func testTheLiveNamesUseTheLiveAccounts() async throws {
        let store = KeychainSecretStore(keychain: keychain, names: StorageNames.live)
        try store.save("a-key", for: .metaAPIKey)
        try store.save("another-key", for: .openRouterAPIKey)

        XCTAssertEqual(keychain.get(account: "meta-api-key"), "a-key")
        XCTAssertEqual(keychain.get(account: "openrouter-api-key"), "another-key")
        XCTAssertNil(KeychainSecretStore(keychain: keychain, names: StorageNames.test).read(.metaAPIKey))
    }

    @MainActor
    func testSavingAgainReplacesTheKey() async throws {
        let store = KeychainSecretStore(keychain: keychain, names: names)
        try store.save("first", for: .metaAPIKey)
        try store.save("second", for: .metaAPIKey)
        XCTAssertEqual(store.read(.metaAPIKey), "second")
    }

    @MainActor
    func testRemovingDeletesTheKeyAndAnAbsentKeyIsNotAnError() async throws {
        let store = KeychainSecretStore(keychain: keychain, names: names)
        try store.save("meta-key-for-a-test", for: .metaAPIKey)
        try store.save("openrouter-key-for-a-test", for: .openRouterAPIKey)

        try store.remove(.metaAPIKey)
        XCTAssertNil(store.read(.metaAPIKey))
        XCTAssertNil(keychain.get(account: "meta-api-key-ui-test"))
        XCTAssertEqual(store.read(.openRouterAPIKey), "openrouter-key-for-a-test", "Each kind has its own item.")

        XCTAssertNoThrow(try store.remove(.metaAPIKey))
        try store.remove(.openRouterAPIKey)
        XCTAssertNil(store.read(.openRouterAPIKey))
    }

    @MainActor
    func testAKeyIsReadByAnotherStoreOverTheSameKeychain() async throws {
        try KeychainSecretStore(keychain: keychain, names: names).save("kept-across-launches", for: .openRouterAPIKey)
        let later = KeychainSecretStore(keychain: keychain, names: names)
        XCTAssertEqual(later.read(.openRouterAPIKey), "kept-across-launches")

        let elsewhere = KeychainStore(service: "com.mahmudkarim.LiveCueRemake.unit-tests.secret-store.other")
        XCTAssertNil(KeychainSecretStore(keychain: elsewhere, names: names).read(.openRouterAPIKey))
    }

    @MainActor
    func testEveryKindOfSecretHasAnAccount() async throws {
        let store = KeychainSecretStore(keychain: keychain, names: names)
        for kind in SecretKind.allCases {
            try store.save("value", for: kind)
            XCTAssertEqual(store.read(kind), "value")
            try store.remove(kind)
            XCTAssertNil(store.read(kind))
        }
    }
}
