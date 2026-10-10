import XCTest
@testable import LiveCueRemake

/// Set, read, overwrite and delete an item under a test service; deleting an absent item succeeds.
///
/// This is also the check that the test build was signed: an unsigned simulator build fails here with
/// status -34018, because it has no entitlements (see `.github/workflows/ios.yml`).
final class KeychainStoreTests: XCTestCase {
    /// A service of its own, so nothing here touches an item of the app or of another test.
    private let store = KeychainStore(service: "com.mahmudkarim.LiveCueRemake.unit-tests.keychain-store")
    private let accounts = ["first-account", "second-account"]

    override func setUpWithError() throws {
        for account in accounts {
            try store.delete(account: account)
        }
    }

    override func tearDownWithError() throws {
        for account in accounts {
            try store.delete(account: account)
        }
    }

    @MainActor
    func testSetReadOverwriteAndDelete() async throws {
        let account = accounts[0]
        XCTAssertNil(store.get(account: account))

        do {
            try store.set("first value", account: account)
        } catch let failure as KeychainFailure {
            XCTFail("The Keychain refused the item with status \(failure.status). Status -34018 means the test build was not signed.")
            return
        }
        XCTAssertEqual(store.get(account: account), "first value")

        try store.set("second value · with a middle dot", account: account)
        XCTAssertEqual(store.get(account: account), "second value · with a middle dot")

        try store.delete(account: account)
        XCTAssertNil(store.get(account: account))
    }

    @MainActor
    func testDeletingAnAbsentItemSucceeds() async throws {
        let account = accounts[0]
        XCTAssertNil(store.get(account: account))
        XCTAssertNoThrow(try store.delete(account: account))
        XCTAssertNoThrow(try store.delete(account: account))
    }

    @MainActor
    func testAccountsAndServicesAreSeparate() async throws {
        try store.set("one", account: accounts[0])
        try store.set("two", account: accounts[1])
        XCTAssertEqual(store.get(account: accounts[0]), "one")
        XCTAssertEqual(store.get(account: accounts[1]), "two")

        let otherService = KeychainStore(service: "com.mahmudkarim.LiveCueRemake.unit-tests.keychain-store.other")
        XCTAssertNil(otherService.get(account: accounts[0]))
        try otherService.set("elsewhere", account: accounts[0])
        XCTAssertEqual(store.get(account: accounts[0]), "one")
        try otherService.delete(account: accounts[0])
        XCTAssertEqual(store.get(account: accounts[0]), "one")

        try store.delete(account: accounts[0])
        XCTAssertNil(store.get(account: accounts[0]))
        XCTAssertEqual(store.get(account: accounts[1]), "two")
    }

    @MainActor
    func testTheDefaultServiceIsTheAppsOwn() async {
        XCTAssertEqual(KeychainStore().service, "com.mahmudkarim.LiveCueRemake")
        XCTAssertEqual(StorageNames.keychainService, "com.mahmudkarim.LiveCueRemake")
        XCTAssertEqual(StorageNames.live.metaKeyAccount, "meta-api-key")
        XCTAssertEqual(StorageNames.live.openRouterKeyAccount, "openrouter-api-key")
        XCTAssertEqual(StorageNames.live.pairingAccount, "relayPairing")
        XCTAssertEqual(StorageNames.live.pairingFileName, "pairedPC.json")
        XCTAssertEqual(StorageNames.test.metaKeyAccount, "meta-api-key-ui-test")
        XCTAssertEqual(StorageNames.test.openRouterKeyAccount, "openrouter-api-key-ui-test")
        XCTAssertEqual(StorageNames.test.pairingAccount, "relayPairing-tests")
        XCTAssertEqual(StorageNames.test.pairingFileName, "pairedPC-tests.json")
    }
}
