import Foundation
import LiveCueCore
import XCTest

/// The manifest of Fluid Speech Mini (D-29), what a download still has to fetch, and what makes a
/// downloaded file the wrong one.
final class ModelManifestTests: XCTestCase {
    private typealias File = ModelManifest.File

    private let small = ModelManifest(
        name: "sample",
        repository: "owner/sample-model",
        revision: "0123456789abcdef0123456789abcdef01234567",
        files: [
            File(path: "A.mlmodelc/weights/weight.bin", bytes: 1_000, sha256: String(repeating: "ab", count: 32)),
            File(path: "A.mlmodelc/metadata.json", bytes: 200),
            File(path: "vocab.json", bytes: 30)
        ]
    )

    // MARK: - Fluid Speech Mini

    func testFluidSpeechMiniHasTwentyOneFilesOfTheKnownTotal() {
        let manifest = ModelManifest.fluidSpeechMini

        XCTAssertEqual(manifest.files.count, 21)
        XCTAssertEqual(manifest.totalBytes, 252_710_832)
        XCTAssertEqual(manifest.repository, "FluidVoice-Labs/fluid-speech-mini-coreml")
        XCTAssertEqual(manifest.revision, "d81c3160dfe5a5e1f8f7267164a07d30617a3a1a")
        XCTAssertEqual(manifest.folderName, "fluid-speech-mini-d81c3160dfe5")
        XCTAssertEqual(Set(manifest.files.map(\.path)).count, 21, "No path is named twice.")
        // About what the catalogue tells the user.
        XCTAssertEqual(Int((Double(manifest.totalBytes) / 1_000_000).rounded()), TranscriptionModel.fluid.approximateMegabytes)
    }

    func testFluidSpeechMiniIsFourCompiledModelsAndAVocabulary() {
        let manifest = ModelManifest.fluidSpeechMini
        let parts = ["analytics/coremldata.bin", "coremldata.bin", "metadata.json", "model.mil", "weights/weight.bin"]
        var expected: [String] = []
        for model in ["Decoder", "Encoder", "JointDecision", "Preprocessor"] {
            expected.append(contentsOf: parts.map { "\(model).mlmodelc/\($0)" })
        }
        expected.append("parakeet_vocab.json")

        XCTAssertEqual(manifest.files.map(\.path), expected)
        XCTAssertEqual(manifest.files.first { $0.path == "Encoder.mlmodelc/weights/weight.bin" }?.bytes, 233_096_704)
        XCTAssertEqual(manifest.files.first { $0.path == "parakeet_vocab.json" }?.bytes, 151_122)
        for file in manifest.files {
            XCTAssertGreaterThan(file.bytes, 0, file.path)
            XCTAssertFalse(file.path.hasPrefix("/"), file.path)
            XCTAssertFalse(file.path.contains(".."), file.path)
        }
    }

    func testEveryBinaryFileOfFluidSpeechMiniNamesItsHash() {
        let hexadecimal = Set("0123456789abcdef")
        for file in ModelManifest.fluidSpeechMini.files {
            // The publisher names a hash for the binary files and none for the text files.
            XCTAssertEqual(file.sha256 != nil, file.path.hasSuffix(".bin"), file.path)
            if let sha256 = file.sha256 {
                XCTAssertEqual(sha256.count, 64, file.path)
                XCTAssertTrue(sha256.allSatisfy { hexadecimal.contains($0) }, file.path)
            }
        }
        XCTAssertEqual(ModelManifest.fluidSpeechMini.files.filter { $0.sha256 != nil }.count, 12)
        XCTAssertEqual(
            ModelManifest.fluidSpeechMini.files.first { $0.path == "Encoder.mlmodelc/weights/weight.bin" }?.sha256,
            "cf0b61022de8c698984a8c7185c2d093fa744694621881ff062c46fee2546131"
        )
    }

    func testAFileIsFetchedFromThePinnedRevision() throws {
        let manifest = ModelManifest.fluidSpeechMini
        let vocabulary = try XCTUnwrap(manifest.files.last)
        XCTAssertEqual(
            manifest.url(for: vocabulary)?.absoluteString,
            "https://huggingface.co/FluidVoice-Labs/fluid-speech-mini-coreml/resolve/d81c3160dfe5a5e1f8f7267164a07d30617a3a1a/parakeet_vocab.json"
        )
        let weights = try XCTUnwrap(manifest.files.first { $0.path == "Decoder.mlmodelc/weights/weight.bin" })
        XCTAssertEqual(
            manifest.url(for: weights)?.absoluteString,
            "https://huggingface.co/FluidVoice-Labs/fluid-speech-mini-coreml/resolve/d81c3160dfe5a5e1f8f7267164a07d30617a3a1a/Decoder.mlmodelc/weights/weight.bin"
        )
        for file in manifest.files {
            XCTAssertEqual(manifest.url(for: file)?.scheme, "https", file.path)
            XCTAssertEqual(manifest.url(for: file)?.host, "huggingface.co", file.path)
        }
    }

    // MARK: - The folder and the names

    func testTheFolderIsNamedForTheModelAndTheRevision() {
        XCTAssertEqual(small.folderName, "sample-0123456789ab")
        XCTAssertEqual(small.totalBytes, 1_230)
        XCTAssertEqual(ModelManifest.revisionPrefixLength, 12)
        XCTAssertEqual(ModelManifest.markerName, ".complete")
        XCTAssertEqual(ModelManifest.temporarySuffix, ".download")

        // Another revision is another folder, so files of two revisions never mix.
        let later = ModelManifest(name: "sample", repository: "owner/sample-model", revision: "ffff456789abcdef", files: [])
        XCTAssertEqual(later.folderName, "sample-ffff456789ab")
        XCTAssertEqual(later.totalBytes, 0)
    }

    // MARK: - The plan

    func testNothingInPlaceMeansEverythingIsFetched() {
        let plan = ModelDownloadPlan(manifest: small) { _ in nil }

        XCTAssertEqual(plan.missing, small.files)
        XCTAssertEqual(plan.presentBytes, 0)
        XCTAssertEqual(plan.totalBytes, 1_230)
        XCTAssertFalse(plan.isComplete)
    }

    func testAFileInPlaceWithTheRightSizeIsNotFetchedAgain() {
        let sizes: [String: Int64] = ["A.mlmodelc/weights/weight.bin": 1_000, "vocab.json": 30]
        let plan = ModelDownloadPlan(manifest: small) { sizes[$0] }

        XCTAssertEqual(plan.missing.map(\.path), ["A.mlmodelc/metadata.json"])
        XCTAssertEqual(plan.presentBytes, 1_030)
        XCTAssertFalse(plan.isComplete)
    }

    func testAFileOfAnotherSizeIsFetchedAgain() {
        // Cut short, empty, and too long.
        let sizes: [String: Int64] = ["A.mlmodelc/weights/weight.bin": 999, "A.mlmodelc/metadata.json": 0, "vocab.json": 31]
        let plan = ModelDownloadPlan(manifest: small) { sizes[$0] }

        XCTAssertEqual(plan.missing, small.files, "In manifest order.")
        XCTAssertEqual(plan.presentBytes, 0)
    }

    func testEverythingInPlaceIsComplete() {
        let sizes = Dictionary(uniqueKeysWithValues: small.files.map { ($0.path, $0.bytes) })
        let plan = ModelDownloadPlan(manifest: small) { sizes[$0] }

        XCTAssertTrue(plan.isComplete)
        XCTAssertTrue(plan.missing.isEmpty)
        XCTAssertEqual(plan.presentBytes, plan.totalBytes)
    }

    // MARK: - The checks of a downloaded file

    func testAFileOfTheWrongSizeIsAProblem() {
        let file = small.files[0]
        XCTAssertNil(file.problem(size: 1_000))
        XCTAssertEqual(file.problem(size: 999), .size(path: file.path, expected: 1_000, found: 999))
        XCTAssertEqual(file.problem(size: 0), .size(path: file.path, expected: 1_000, found: 0))
        XCTAssertEqual(file.problem(size: 1_001), .size(path: file.path, expected: 1_000, found: 1_001))
    }

    func testAFileWithAnotherHashIsAProblem() {
        let file = small.files[0]
        XCTAssertNil(file.problem(sha256: String(repeating: "ab", count: 32)))
        XCTAssertNil(file.problem(sha256: String(repeating: "AB", count: 32)), "Upper and lower case are the same digit.")
        XCTAssertEqual(file.problem(sha256: String(repeating: "ac", count: 32)), .hash(path: file.path))
        XCTAssertEqual(file.problem(sha256: ""), .hash(path: file.path))
    }

    func testAFileWithoutANamedHashAcceptsAny() {
        let file = small.files[1]
        XCTAssertNil(file.sha256)
        XCTAssertNil(file.problem(sha256: String(repeating: "00", count: 32)))
        XCTAssertNil(file.problem(sha256: ""))
    }
}
