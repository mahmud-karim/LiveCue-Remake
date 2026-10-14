import Foundation

/// A speech model the app downloads by itself: its files at one pinned revision, the size of each, and
/// its SHA-256 where the publisher names one.
public struct ModelManifest: Equatable, Sendable {
    public struct File: Equatable, Sendable {
        /// Below the model's folder, with "/" between its parts.
        public let path: String
        public let bytes: Int64
        /// 64 hexadecimal digits; nil when the publisher names none.
        public let sha256: String?

        public init(path: String, bytes: Int64, sha256: String? = nil) {
            self.path = path
            self.bytes = bytes
            self.sha256 = sha256
        }

        /// What is wrong with a copy of this size, or nil.
        public func problem(size: Int64) -> ModelFileProblem? {
            size == bytes ? nil : .size(path: path, expected: bytes, found: size)
        }

        /// What is wrong with a copy that has this SHA-256, or nil. A file without a named hash accepts
        /// any. Upper and lower case are the same digit.
        public func problem(sha256 found: String) -> ModelFileProblem? {
            guard let sha256 else { return nil }
            return sha256.lowercased() == found.lowercased() ? nil : .hash(path: path)
        }
    }

    /// The first part of the model's folder name.
    public let name: String
    /// "<owner>/<repository>" on Hugging Face.
    public let repository: String
    public let revision: String
    public let files: [File]

    public init(name: String, repository: String, revision: String, files: [File]) {
        self.name = name
        self.repository = repository
        self.revision = revision
        self.files = files
    }

    public var totalBytes: Int64 {
        files.reduce(0) { $0 + $1.bytes }
    }

    /// The name and the first 12 characters of the revision, so another revision gets another folder.
    public var folderName: String {
        "\(name)-\(revision.prefix(ModelManifest.revisionPrefixLength))"
    }

    /// Where a file is fetched from: the repository at the pinned revision.
    public func url(for file: File) -> URL? {
        URL(string: "\(ModelManifest.host)/\(repository)/resolve/\(revision)/\(file.path)")
    }

    public static let host = "https://huggingface.co"
    public static let revisionPrefixLength = 12
    /// Written into the folder last. Without it the model is not loaded.
    public static let markerName = ".complete"
    /// A file is downloaded under its name with this ending and moved into place once it was checked.
    public static let temporarySuffix = ".download"
}

/// Why a downloaded file is not the one the manifest names.
public enum ModelFileProblem: Error, Equatable, Sendable {
    case size(path: String, expected: Int64, found: Int64)
    case hash(path: String)
}

/// What a download still has to fetch, from the sizes of the files that are in place.
public struct ModelDownloadPlan: Equatable, Sendable {
    /// In manifest order.
    public let missing: [ModelManifest.File]
    /// The bytes of the files that are in place with the right size.
    public let presentBytes: Int64
    public let totalBytes: Int64

    public var isComplete: Bool {
        missing.isEmpty
    }

    /// `sizeInPlace` gives the size of the file at a manifest path, or nil when there is none. A file of
    /// another size is fetched again.
    public init(manifest: ModelManifest, sizeInPlace: (String) -> Int64?) {
        var missing: [ModelManifest.File] = []
        var present: Int64 = 0
        for file in manifest.files {
            if sizeInPlace(file.path) == file.bytes {
                present += file.bytes
            } else {
                missing.append(file)
            }
        }
        self.missing = missing
        self.presentBytes = present
        self.totalBytes = manifest.totalBytes
    }
}

extension ModelManifest {
    /// Fluid Speech Mini by FluidVoice (CC BY 4.0), in Core ML form: four compiled models and the
    /// vocabulary. 21 files, 252,710,832 bytes.
    public static let fluidSpeechMini = ModelManifest(
        name: "fluid-speech-mini",
        repository: "FluidVoice-Labs/fluid-speech-mini-coreml",
        revision: "d81c3160dfe5a5e1f8f7267164a07d30617a3a1a",
        files: [
            File(path: "Decoder.mlmodelc/analytics/coremldata.bin", bytes: 243, sha256: "d4beee43a6eab29b2adcc298f2e8582e8fb79d5fe3aa9f1ab692ade681ca369d"),
            File(path: "Decoder.mlmodelc/coremldata.bin", bytes: 491, sha256: "6094d06d05c1ca4599c7b13c9a4ca7c00826ee2396796cf3084ce602a9884468"),
            File(path: "Decoder.mlmodelc/metadata.json", bytes: 3_373),
            File(path: "Decoder.mlmodelc/model.mil", bytes: 13_411),
            File(path: "Decoder.mlmodelc/weights/weight.bin", bytes: 11_845_120, sha256: "d173e381488d07fd7648e2ccecbfb1bceff0457d75461550b26ba7fa2d23309a"),
            File(path: "Encoder.mlmodelc/analytics/coremldata.bin", bytes: 243, sha256: "3a3caa2c6aec983d94a5659eff09a00f77077147ea6726b8ddfdf022f0238ce9"),
            File(path: "Encoder.mlmodelc/coremldata.bin", bytes: 427, sha256: "494d8e2ce30b37bcdf494b3ba7ddf6e1bc921b9a59e369a9efd44d013ca3bf7d"),
            File(path: "Encoder.mlmodelc/metadata.json", bytes: 2_906),
            File(path: "Encoder.mlmodelc/model.mil", bytes: 712_643),
            File(path: "Encoder.mlmodelc/weights/weight.bin", bytes: 233_096_704, sha256: "cf0b61022de8c698984a8c7185c2d093fa744694621881ff062c46fee2546131"),
            File(path: "JointDecision.mlmodelc/analytics/coremldata.bin", bytes: 243, sha256: "9e767d69e1092b27b28d9f236b9cbcded58ff3891e320cba7855e2c24e803d69"),
            File(path: "JointDecision.mlmodelc/coremldata.bin", bytes: 462, sha256: "284b235c08cd4769202af017f854deace69c0209c29f2253a2073bb68d58d38d"),
            File(path: "JointDecision.mlmodelc/metadata.json", bytes: 2_790),
            File(path: "JointDecision.mlmodelc/model.mil", bytes: 7_547),
            File(path: "JointDecision.mlmodelc/weights/weight.bin", bytes: 6_350_284, sha256: "f991f65979c1b3fee3db39a645aa87e6bef1a304ed1816d074b17479bea797fc"),
            File(path: "Preprocessor.mlmodelc/analytics/coremldata.bin", bytes: 243, sha256: "c9beeb989c8d66f8be11df59bc6df277ec76cee404f6865b46243835ef562f6d"),
            File(path: "Preprocessor.mlmodelc/coremldata.bin", bytes: 486, sha256: "dbde3f2300842c1fd51ef3ff948a0bcffe65ffd2dca10707f2509f32c1d65b1d"),
            File(path: "Preprocessor.mlmodelc/metadata.json", bytes: 2_841),
            File(path: "Preprocessor.mlmodelc/model.mil", bytes: 28_181),
            File(path: "Preprocessor.mlmodelc/weights/weight.bin", bytes: 491_072, sha256: "129b76e3aeafa8afa3ea76d995b964b145fe83700d579f6ff42c4c38fa0968ea"),
            File(path: "parakeet_vocab.json", bytes: 151_122)
        ]
    )
}
