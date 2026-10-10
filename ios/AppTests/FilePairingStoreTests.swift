import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The Keychain item is the authority for the pairing and the file is a fallback (ST-07); restore without
/// the Keychain copy, file protection and backup exclusion (AC-IOS-133, 141).
///
/// The Keychain items live under a service of this test's own and the file in a temporary folder. "A"
/// and "B" are two pairings with different hosts and the synthetic token.
final class FilePairingStoreTests: XCTestCase {
    private let keychain = KeychainStore(service: "com.mahmudkarim.LiveCueRemake.unit-tests.pairing-store")
    private let names = StorageNames.test
    private let directory = FileManager.default.temporaryDirectory
        .appendingPathComponent("FilePairingStoreTests-\(UUID().uuidString)", isDirectory: true)

    override func setUpWithError() throws {
        try keychain.delete(account: names.pairingAccount)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try keychain.delete(account: names.pairingAccount)
        try? FileManager.default.removeItem(at: directory)
    }

    // MARK: - Helpers

    private var fileURL: URL {
        directory.appendingPathComponent(names.pairingFileName, isDirectory: false)
    }

    private var keychainText: String? {
        keychain.get(account: names.pairingAccount)
    }

    private var fileText: String? {
        guard let data = try? Data(contentsOf: fileURL) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    private func pairing(_ name: String) throws -> PairingPayload {
        try PairingPayload.manual(address: "https://\(name).example.test:11443/", token: FixtureValues.pairingToken)
    }

    private func putInKeychain(_ text: String) throws {
        try keychain.set(text, account: names.pairingAccount)
    }

    private func putInFile(_ text: String) throws {
        try Data(text.utf8).write(to: fileURL)
    }

    private func makeStore() -> FilePairingStore {
        FilePairingStore(keychain: keychain, names: names, directory: directory)
    }

    /// The real Keychain item, so that a test can replace single calls with failing ones.
    private func realAccess() -> PairingKeychainAccess {
        PairingKeychainAccess(keychain: keychain, account: names.pairingAccount)
    }

    private func makeStore(access: PairingKeychainAccess) -> FilePairingStore {
        FilePairingStore(access: access, names: names, directory: directory)
    }

    // MARK: - Load

    @MainActor
    func testNothingStoredLoadsNothing() async {
        XCTAssertNil(makeStore().load())
        XCTAssertNil(keychainText)
        XCTAssertNil(fileText, "Loading nothing writes nothing.")
    }

    @MainActor
    func testTheKeychainItemWinsOverTheFileAndTheFileIsRewrittenFromIt() async throws {
        let a = try pairing("pc-a")
        let b = try pairing("pc-b")
        try putInKeychain(a.storedText)
        try putInFile(b.storedText)

        XCTAssertEqual(makeStore().load(), a)
        XCTAssertEqual(keychainText, a.storedText, "The Keychain still holds A.")
        XCTAssertEqual(fileText, a.storedText, "The file now holds A.")
    }

    @MainActor
    func testAMissingFileIsWrittenFromTheKeychainItem() async throws {
        let a = try pairing("pc-a")
        try putInKeychain(a.storedText)
        XCTAssertNil(fileText)

        XCTAssertEqual(makeStore().load(), a)
        XCTAssertEqual(fileText, a.storedText)
        XCTAssertEqual(keychainText, a.storedText)
    }

    @MainActor
    func testAFileThatHoldsSomethingElseIsRewrittenFromTheKeychainItem() async throws {
        let a = try pairing("pc-a")
        try putInKeychain(a.storedText)
        try putInFile("not a pairing")

        XCTAssertEqual(makeStore().load(), a)
        XCTAssertEqual(fileText, a.storedText)
    }

    @MainActor
    func testWithoutAKeychainItemTheFileIsThePairingAndTheKeychainIsNotWritten() async throws {
        let b = try pairing("pc-b")
        try putInFile(b.storedText)

        XCTAssertEqual(makeStore().load(), b)
        XCTAssertNil(keychainText, "The Keychain is never written from the file.")
        XCTAssertEqual(makeStore().load(), b)
        XCTAssertNil(keychainText)
        XCTAssertEqual(fileText, b.storedText)
    }

    @MainActor
    func testKeychainTextThatDoesNotParseFallsBackToTheFile() async throws {
        let b = try pairing("pc-b")
        try putInKeychain("{\"endpoint\":\"http://pc-a.example.test/\",\"token\":\"short\"}")
        try putInFile(b.storedText)

        XCTAssertEqual(makeStore().load(), b)
        XCTAssertEqual(
            keychainText,
            "{\"endpoint\":\"http://pc-a.example.test/\",\"token\":\"short\"}",
            "The Keychain is never written from the file."
        )
    }

    @MainActor
    func testFileTextThatDoesNotParseIsTreatedAsAbsent() async throws {
        try putInFile("{\"endpoint\":\"https://pc-b.example.test:11443/\"}")
        XCTAssertNil(makeStore().load())

        // Larger than a pairing can be.
        try putInFile(String(repeating: " ", count: RelayConstants.pairingPayloadBytes + 1))
        XCTAssertNil(makeStore().load())
        XCTAssertNil(keychainText)
    }

    @MainActor
    func testAKeychainThatCannotBeReadFallsBackToTheFile() async throws {
        let a = try pairing("pc-a")
        let b = try pairing("pc-b")
        try putInKeychain(a.storedText)
        try putInFile(b.storedText)

        var access = realAccess()
        access.read = { nil }
        XCTAssertEqual(makeStore(access: access).load(), b)
        XCTAssertEqual(keychainText, a.storedText, "The Keychain is never written from the file.")
        XCTAssertEqual(fileText, b.storedText)
    }

    // MARK: - Save

    @MainActor
    func testSaveWritesTheKeychainItemFirstAndThenTheFile() async throws {
        let a = try pairing("pc-a")
        let file = fileURL
        var access = realAccess()
        let write = access.write
        access.write = { text in
            XCTAssertFalse(FileManager.default.fileExists(atPath: file.path), "The Keychain item is written before the file.")
            try write(text)
        }

        try makeStore(access: access).save(a)

        XCTAssertEqual(keychainText, a.storedText)
        XCTAssertEqual(fileText, a.storedText)
        XCTAssertEqual(makeStore().load(), a)
    }

    @MainActor
    func testAfterEverySaveLoadReturnsTheSavedPairing() async throws {
        let a = try pairing("pc-a")
        let b = try pairing("pc-b")
        let store = makeStore()

        try store.save(a)
        XCTAssertEqual(store.load(), a)
        try store.save(b)
        XCTAssertEqual(store.load(), b)
        XCTAssertEqual(keychainText, b.storedText)
        XCTAssertEqual(fileText, b.storedText)
        XCTAssertEqual(makeStore().load(), b, "A store made later reads the same pairing.")
    }

    @MainActor
    func testTheStoredTextIsThePairingPayload() async throws {
        let a = try pairing("pc-a")
        try makeStore().save(a)

        XCTAssertEqual(fileText, "{\"endpoint\":\"https://pc-a.example.test:11443/\",\"token\":\"\(FixtureValues.pairingToken)\"}")
        XCTAssertEqual(try PairingPayload.parse(try XCTUnwrap(keychainText)), a)
        XCTAssertEqual(makeStore().load()?.endpoint, "https://pc-a.example.test:11443/", "Port and trailing slash are kept.")
    }

    @MainActor
    func testWhenTheKeychainWriteFailsTheFileIsWrittenAndTheOlderItemIsDeleted() async throws {
        let a = try pairing("pc-a")
        let b = try pairing("pc-b")
        try makeStore().save(a)

        var access = realAccess()
        access.write = { _ in throw KeychainFailure(status: -34018) }
        try makeStore(access: access).save(b)

        XCTAssertEqual(fileText, b.storedText, "The file must hold the pairing when the Keychain refused it.")
        XCTAssertNil(keychainText, "The older item is deleted, so that it cannot win at the next load.")
        XCTAssertEqual(makeStore(access: access).load(), b)
        XCTAssertEqual(makeStore().load(), b)
        XCTAssertNil(keychainText, "Loading from the file does not write the Keychain.")
    }

    @MainActor
    func testWhenTheOlderItemCanBeNeitherReplacedNorDeletedSaveThrowsAndTheFileHoldsTheKeychainPairing() async throws {
        let a = try pairing("pc-a")
        let b = try pairing("pc-b")
        try makeStore().save(a)

        var access = realAccess()
        access.write = { _ in throw KeychainFailure(status: -25299) }
        access.delete = { throw KeychainFailure(status: -25243) }
        let store = makeStore(access: access)

        XCTAssertThrowsError(try store.save(b)) { error in
            XCTAssertEqual(error as? KeychainFailure, KeychainFailure(status: -25299), "The refusal of the write is what the caller sees.")
        }
        XCTAssertEqual(keychainText, a.storedText)
        XCTAssertEqual(fileText, a.storedText, "The file again holds the Keychain's pairing.")
        XCTAssertEqual(store.load(), a)
        XCTAssertEqual(makeStore().load(), a)
    }

    @MainActor
    func testWithoutAUsableKeychainThePairingLivesInTheFile() async throws {
        // What a container without Keychain access looks like: every call is refused.
        let access = PairingKeychainAccess(
            read: { nil },
            write: { _ in throw KeychainFailure(status: -34018) },
            delete: { throw KeychainFailure(status: -34018) }
        )
        let a = try pairing("pc-a")
        let b = try pairing("pc-b")
        let store = makeStore(access: access)
        XCTAssertNil(store.load())

        try store.save(a)
        XCTAssertEqual(store.load(), a)
        try store.save(b)
        XCTAssertEqual(store.load(), b)
        XCTAssertEqual(fileText, b.storedText)
        XCTAssertNil(keychainText)
    }

    @MainActor
    func testWhenNeitherTheKeychainNorTheFileTakesThePairingSaveThrowsAndTheOldPairingStays() async throws {
        let a = try pairing("pc-a")
        let b = try pairing("pc-b")
        try makeStore().save(a)

        // A folder where the file should be: the file cannot be written.
        let blocked = directory.appendingPathComponent("blocked", isDirectory: true)
        try FileManager.default.createDirectory(
            at: blocked.appendingPathComponent(names.pairingFileName, isDirectory: true),
            withIntermediateDirectories: true
        )
        var access = realAccess()
        access.write = { _ in throw KeychainFailure(status: -34018) }
        let store = FilePairingStore(access: access, names: names, directory: blocked)

        XCTAssertThrowsError(try store.save(b))
        XCTAssertEqual(keychainText, a.storedText, "Nothing was deleted, because nothing was saved.")
        XCTAssertEqual(store.load(), a)
    }

    // MARK: - The file and the Keychain copy

    @MainActor
    func testThePairingIsRestoredWithoutTheKeychainCopy() async throws {
        let a = try pairing("pc-a")
        let store = makeStore()
        try store.save(a)

        store.removeKeychainCopy()
        XCTAssertNil(keychainText)
        XCTAssertEqual(store.load(), a, "The pairing is restored from the protected file.")
        XCTAssertEqual(makeStore().load(), a)
        XCTAssertNil(keychainText, "Restoring from the file does not write the Keychain.")

        // Removing a copy that is not there changes nothing.
        store.removeKeychainCopy()
        XCTAssertEqual(store.load(), a)
    }

    @MainActor
    func testTheFileIsExcludedFromBackupAndProtected() async throws {
        let a = try pairing("pc-a")
        let b = try pairing("pc-b")
        let store = makeStore()

        try store.save(a)
        XCTAssertEqual(try isExcludedFromBackup(), true)
        try assertFileProtection()

        // A second write puts a new file in the old one's place; the new one is excluded too.
        try store.save(b)
        XCTAssertEqual(try isExcludedFromBackup(), true)

        // And so is a file that `load` rewrote from the Keychain item.
        try FileManager.default.removeItem(at: fileURL)
        XCTAssertEqual(store.load(), b)
        XCTAssertEqual(try isExcludedFromBackup(), true)
        try assertFileProtection()

        XCTAssertTrue(FilePairingStore.fileWritingOptions.contains(.atomic))
        XCTAssertTrue(FilePairingStore.fileWritingOptions.contains(.completeFileProtectionUntilFirstUserAuthentication))
    }

    @MainActor
    func testTheDefaultFolderIsApplicationSupport() async throws {
        // Unique names, so that the pairing of the app and of the UI tests stays untouched.
        let unique = "unit-tests-\(UUID().uuidString)"
        let scratch = StorageNames.Names(
            pairingAccount: unique,
            metaKeyAccount: unique + "-meta",
            openRouterKeyAccount: unique + "-openrouter",
            pairingFileName: unique + ".json"
        )
        let support = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        let file = support.appendingPathComponent(scratch.pairingFileName, isDirectory: false)
        defer {
            try? keychain.delete(account: unique)
            try? FileManager.default.removeItem(at: file)
        }

        let a = try pairing("pc-a")
        try FilePairingStore(keychain: keychain, names: scratch).save(a)
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
        XCTAssertEqual(FilePairingStore(keychain: keychain, names: scratch).load(), a)
    }

    // MARK: - File attributes

    private func isExcludedFromBackup() throws -> Bool? {
        // A new URL value each time: resource values are cached on the value they were read from.
        let url = URL(fileURLWithPath: fileURL.path)
        return try url.resourceValues(forKeys: [.isExcludedFromBackupKey]).isExcludedFromBackup
    }

    /// A simulator does not keep protection classes and reports none; a device reports the class asked for.
    private func assertFileProtection(file: StaticString = #filePath, line: UInt = #line) throws {
        let attributes = try FileManager.default.attributesOfItem(atPath: fileURL.path)
        if let protection = attributes[.protectionKey] as? FileProtectionType {
            XCTAssertEqual(protection, .completeUntilFirstUserAuthentication, file: file, line: line)
        }
    }
}
