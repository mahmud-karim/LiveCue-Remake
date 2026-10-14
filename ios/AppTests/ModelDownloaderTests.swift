import CryptoKit
import LiveCueCore
import XCTest
@testable import LiveCueRemake

/// The model downloader (D-29) with a fake transport and a made-up model of three small files: what is
/// fetched and what is skipped, the checks before a file counts, the marker, and the progress in bytes.
/// Nothing here opens a connection.
final class ModelDownloaderTests: XCTestCase {
    /// The reports of one preparation. The downloader reports off the main actor.
    private final class ProgressLog: @unchecked Sendable {
        private let lock = NSLock()
        private var reports: [ModelPreparationProgress] = []

        func add(_ report: ModelPreparationProgress) {
            lock.withLock { () -> Void in
                reports.append(report)
            }
        }

        var all: [ModelPreparationProgress] {
            lock.withLock { reports }
        }
    }

    private let root = FileManager.default.temporaryDirectory
        .appendingPathComponent("ModelDownloaderTests-\(UUID().uuidString)", isDirectory: true)

    private let weights = Data(repeating: 7, count: 1_000)
    private let metadata = Data(repeating: 0x20, count: 200)
    private let vocabulary = Data(repeating: 0x7B, count: 30)

    private var manifest: ModelManifest {
        ModelManifest(
            name: "sample",
            repository: "owner/sample-model",
            revision: "0123456789abcdef0123456789abcdef01234567",
            files: [
                ModelManifest.File(path: "A.mlmodelc/weights/weight.bin", bytes: 1_000, sha256: ModelDownloaderTests.sha256(weights)),
                ModelManifest.File(path: "A.mlmodelc/metadata.json", bytes: 200),
                ModelManifest.File(path: "vocab.json", bytes: 30)
            ]
        )
    }

    override func setUpWithError() throws {
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root)
    }

    // MARK: - A whole download

    func testEveryFileIsFetchedCheckedAndPutInPlace() async throws {
        let transport = await servingEverything()
        let downloader = ModelDownloader(manifest: manifest, root: root, transport: transport)
        let log = ProgressLog()
        XCTAssertFalse(downloader.isComplete)

        let folder = try await downloader.prepare { log.add($0) }

        XCTAssertEqual(folder.standardizedFileURL, root.appendingPathComponent("sample-0123456789ab", isDirectory: true).standardizedFileURL)
        XCTAssertEqual(folder.standardizedFileURL, downloader.folder.standardizedFileURL)
        XCTAssertEqual(try Data(contentsOf: place("A.mlmodelc/weights/weight.bin")), weights)
        XCTAssertEqual(try Data(contentsOf: place("A.mlmodelc/metadata.json")), metadata)
        XCTAssertEqual(try Data(contentsOf: place("vocab.json")), vocabulary)
        XCTAssertEqual(leftovers(), [], "No temporary file is left.")
        XCTAssertTrue(downloader.isComplete)
        XCTAssertEqual(try String(contentsOf: marker, encoding: .utf8), manifest.revision)
        XCTAssertEqual(try folder.resourceValues(forKeys: [.isExcludedFromBackupKey]).isExcludedFromBackup, true)

        // One request per file, in manifest order, at the pinned revision.
        let requested = await transport.requested
        XCTAssertEqual(requested, manifest.files.map { manifest.url(for: $0)?.absoluteString ?? "" })
        XCTAssertTrue(requested.allSatisfy { $0.contains("/resolve/0123456789abcdef0123456789abcdef01234567/") })
    }

    func testProgressCountsTheBytesOfTheWholeModel() async throws {
        let transport = await servingEverything()
        let downloader = ModelDownloader(manifest: manifest, root: root, transport: transport)
        let log = ProgressLog()

        _ = try await downloader.prepare { log.add($0) }

        let reports = log.all
        XCTAssertEqual(reports.first, ModelPreparationProgress(fraction: 0, phase: .checking, counter: .none))
        XCTAssertEqual(reports.last, ModelPreparationProgress(fraction: 1, phase: .preparing, counter: .bytes(completed: 1_230, total: 1_230)))
        XCTAssertEqual(reports.map(\.phase).filter { $0 == .checking }.count, 1)
        XCTAssertEqual(reports.map(\.phase).filter { $0 == .preparing }.count, 1)

        // In between: bytes done of 1,230, never going back, the transport's own steps included.
        let downloading = reports.dropFirst().dropLast()
        XCTAssertTrue(downloading.allSatisfy { $0.phase == .downloading })
        let completed = downloading.map { report -> Int64 in
            guard case .bytes(let done, let total) = report.counter else { return -1 }
            XCTAssertEqual(total, 1_230)
            return done
        }
        XCTAssertEqual(completed, [0, 500, 1_000, 1_000, 1_100, 1_200, 1_200, 1_215, 1_230])
        XCTAssertEqual(downloading.last?.fraction, 1)
        XCTAssertEqual(downloading.first?.fraction, 0)
    }

    // MARK: - Going on where it stopped

    func testAFileInPlaceWithTheRightSizeIsNotFetchedAgain() async throws {
        let transport = await servingEverything()
        let downloader = ModelDownloader(manifest: manifest, root: root, transport: transport)
        // What an interrupted download left: the first file whole, the second cut short, and no marker.
        try write(weights, to: "A.mlmodelc/weights/weight.bin")
        try write(Data(repeating: 0x20, count: 120), to: "A.mlmodelc/metadata.json")
        let log = ProgressLog()

        _ = try await downloader.prepare { log.add($0) }

        let requested = await transport.requested
        XCTAssertEqual(requested.count, 2)
        XCTAssertFalse(requested.contains { $0.hasSuffix("weight.bin") }, "The file that was whole is kept.")
        XCTAssertEqual(try Data(contentsOf: place("A.mlmodelc/metadata.json")), metadata, "The file that was cut short is replaced.")
        XCTAssertTrue(downloader.isComplete)
        // The bytes that were already there count from the first report on.
        XCTAssertEqual(log.all.dropFirst().first?.counter, .bytes(completed: 1_000, total: 1_230))
    }

    func testAModelThatIsCompleteIsNotFetchedAtAll() async throws {
        let transport = await servingEverything()
        let downloader = ModelDownloader(manifest: manifest, root: root, transport: transport)
        _ = try await downloader.prepare { _ in }
        let log = ProgressLog()

        // "Reload".
        let folder = try await downloader.prepare { log.add($0) }

        let requested = await transport.requested
        XCTAssertEqual(requested.count, 3, "Nothing was asked for a second time.")
        XCTAssertEqual(folder.standardizedFileURL, downloader.folder.standardizedFileURL)
        XCTAssertEqual(log.all, [
            ModelPreparationProgress(fraction: 0, phase: .checking, counter: .none),
            ModelPreparationProgress(fraction: 1, phase: .preparing, counter: .bytes(completed: 1_230, total: 1_230))
        ])
        XCTAssertTrue(downloader.isComplete)
    }

    func testAMissingFileTakesTheMarkerAwayUntilItIsBack() async throws {
        let transport = await servingEverything()
        let downloader = ModelDownloader(manifest: manifest, root: root, transport: transport)
        _ = try await downloader.prepare { _ in }
        try FileManager.default.removeItem(at: place("vocab.json"))
        await transport.setFailure(URLError(.notConnectedToInternet), for: url(of: "vocab.json"))

        // The marker is there, a file is not, and it cannot be fetched: the model does not count as complete.
        await assertFails(downloader) { error in
            XCTAssertEqual((error as? URLError)?.code, .notConnectedToInternet)
        }
        XCTAssertFalse(downloader.isComplete)

        await transport.setFailure(nil, for: url(of: "vocab.json"))
        _ = try await downloader.prepare { _ in }
        XCTAssertTrue(downloader.isComplete)
        XCTAssertEqual(try Data(contentsOf: place("vocab.json")), vocabulary)
    }

    // MARK: - Files that are not the right ones

    func testAFileOfTheWrongSizeFailsAndLeavesNoFileInPlace() async throws {
        let transport = await servingEverything()
        // 150 of 200 bytes.
        await transport.setBody(Data(repeating: 0x20, count: 150), for: url(of: "A.mlmodelc/metadata.json"))
        let downloader = ModelDownloader(manifest: manifest, root: root, transport: transport)

        await assertFails(downloader) { error in
            XCTAssertEqual(error as? ModelDownloadFailure, .damagedFile)
        }

        XCTAssertFalse(exists("A.mlmodelc/metadata.json"), "A file that failed its check is never in place.")
        XCTAssertEqual(leftovers(), [], "Nor is its temporary file.")
        XCTAssertTrue(exists("A.mlmodelc/weights/weight.bin"), "The file before it stays.")
        XCTAssertFalse(exists("vocab.json"), "The download stopped at the failure.")
        XCTAssertFalse(downloader.isComplete, "The marker is written last.")

        // Pressing the button again goes on with the files that are missing.
        await transport.setBody(metadata, for: url(of: "A.mlmodelc/metadata.json"))
        _ = try await downloader.prepare { _ in }
        let requested = await transport.requested
        XCTAssertEqual(requested.filter { $0.hasSuffix("weight.bin") }.count, 1)
        XCTAssertEqual(requested.count, 4)
        XCTAssertTrue(downloader.isComplete)
    }

    func testAFileWithTheWrongContentFailsItsHash() async throws {
        let transport = await servingEverything()
        // The right size, another content.
        await transport.setBody(Data(repeating: 8, count: 1_000), for: url(of: "A.mlmodelc/weights/weight.bin"))
        let downloader = ModelDownloader(manifest: manifest, root: root, transport: transport)

        await assertFails(downloader) { error in
            XCTAssertEqual(error as? ModelDownloadFailure, .damagedFile)
        }

        XCTAssertFalse(exists("A.mlmodelc/weights/weight.bin"))
        XCTAssertEqual(leftovers(), [])
        XCTAssertFalse(downloader.isComplete)
        let requested = await transport.requested
        XCTAssertEqual(requested.count, 1)
    }

    func testAFileOfTheWrongSizeInPlaceIsRemovedBeforeItIsFetchedAgain() async throws {
        let transport = await servingEverything()
        await transport.setFailure(FakeEngineFailure(), for: url(of: "vocab.json"))
        let downloader = ModelDownloader(manifest: manifest, root: root, transport: transport)
        try write(Data(repeating: 0x7B, count: 12), to: "vocab.json")
        try write(Data(repeating: 1, count: 5), to: "vocab.json" + ModelManifest.temporarySuffix)

        await assertFails(downloader) { error in
            XCTAssertTrue(error is FakeEngineFailure)
        }

        XCTAssertFalse(exists("vocab.json"), "The file of the wrong size does not stay where a whole one belongs.")
        XCTAssertEqual(leftovers(), [], "What an earlier try left behind is gone too.")
        XCTAssertFalse(downloader.isComplete)
    }

    // MARK: - The words of a failure

    func testEveryFailureHasWordsOfItsOwn() {
        let failures: [ModelDownloadFailure] = [.damagedFile, .server(status: 404), .incomplete, .noFolder]
        let texts = failures.map(\.localizedDescription)
        XCTAssertEqual(Set(texts).count, failures.count)
        XCTAssertEqual(
            ModelDownloadFailure.damagedFile.localizedDescription,
            "A model file did not arrive intact. Try again; the files already downloaded are kept."
        )
        XCTAssertEqual(
            ModelDownloadFailure.server(status: 404).localizedDescription,
            "The model could not be downloaded (server status 404). Try again later."
        )
        // The alert of a failed preparation shows these words as they are.
        XCTAssertTrue(EngineLoadFailure.readable(ModelDownloadFailure.incomplete) is ModelDownloadFailure)
    }

    // MARK: - The hash of a file

    func testTheHashOfAFileIsItsSHA256() throws {
        try write(Data("abc".utf8), to: "abc.txt")
        XCTAssertEqual(
            try FileDigest.sha256(of: place("abc.txt")),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        )

        // A file of more than one read, and an empty one.
        let large = Data((0..<2_500_000).map { UInt8(truncatingIfNeeded: $0) })
        try write(large, to: "large.bin")
        XCTAssertEqual(try FileDigest.sha256(of: place("large.bin")), ModelDownloaderTests.sha256(large))
        try write(Data(), to: "empty.bin")
        XCTAssertEqual(
            try FileDigest.sha256(of: place("empty.bin")),
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        )
        XCTAssertThrowsError(try FileDigest.sha256(of: place("missing.bin")))
    }

    // MARK: - Helpers

    private static func sha256(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    /// A transport that has the right body for every file of the manifest.
    private func servingEverything() async -> FakeModelFileTransport {
        let transport = FakeModelFileTransport()
        await transport.setBody(weights, for: url(of: "A.mlmodelc/weights/weight.bin"))
        await transport.setBody(metadata, for: url(of: "A.mlmodelc/metadata.json"))
        await transport.setBody(vocabulary, for: url(of: "vocab.json"))
        return transport
    }

    private func url(of path: String) -> URL? {
        manifest.files.first { $0.path == path }.flatMap { manifest.url(for: $0) }
    }

    private var folder: URL {
        root.appendingPathComponent(manifest.folderName, isDirectory: true)
    }

    private var marker: URL {
        folder.appendingPathComponent(ModelManifest.markerName, isDirectory: false)
    }

    private func place(_ path: String) -> URL {
        folder.appendingPathComponent(path, isDirectory: false)
    }

    private func exists(_ path: String) -> Bool {
        FileManager.default.fileExists(atPath: place(path).path)
    }

    private func write(_ data: Data, to path: String) throws {
        let url = place(path)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: url)
    }

    /// Every temporary file below the model's folder.
    private func leftovers() -> [String] {
        guard let found = FileManager.default.enumerator(at: folder, includingPropertiesForKeys: nil) else { return [] }
        var names: [String] = []
        for case let url as URL in found where url.lastPathComponent.hasSuffix(ModelManifest.temporarySuffix) {
            names.append(url.lastPathComponent)
        }
        return names
    }

    /// Runs a preparation that has to fail and hands its error to `check`.
    private func assertFails(
        _ downloader: ModelDownloader,
        _ check: (any Error) -> Void,
        file: StaticString = #filePath,
        line: UInt = #line
    ) async {
        do {
            _ = try await downloader.prepare { _ in }
            XCTFail("The preparation should have failed.", file: file, line: line)
        } catch {
            check(error)
        }
    }
}
