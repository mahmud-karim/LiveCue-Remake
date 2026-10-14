import CryptoKit
import Foundation
import LiveCueCore

/// Fetches one file of a model. The real one is a URL session download; tests use a fake.
protocol ModelFileTransport: Sendable {
    /// Downloads `url` to `destination`, where nothing is yet. `received` is called off the main actor
    /// with the bytes of this file that have arrived so far.
    func download(_ url: URL, to destination: URL, received: @escaping @Sendable (Int64) -> Void) async throws
}

/// Why a model could not be downloaded, in words for the alert of a failed preparation (E-12).
enum ModelDownloadFailure: LocalizedError, Equatable {
    /// A file arrived with another size or another content than the model names.
    case damagedFile
    /// The server answered, but not with the file.
    case server(status: Int)
    /// The transfer ended without a file and without saying why.
    case incomplete
    /// The app has no folder to keep the model in.
    case noFolder

    var errorDescription: String? {
        switch self {
        case .damagedFile:
            return "A model file did not arrive intact. Try again; the files already downloaded are kept."
        case .server(let status):
            return "The model could not be downloaded (server status \(status)). Try again later."
        case .incomplete:
            return "The model download did not finish. Try again; the files already downloaded are kept."
        case .noFolder:
            return "This iPhone gave the app no folder to keep the model in."
        }
    }
}

/// Downloads a model's files into its own folder, one at a time, and checks each before it counts.
///
/// A file arrives under a temporary name and is moved into place only when its size, and its SHA-256
/// where the manifest names one, are right. A file that is in place with the right size is not fetched
/// again, so an interrupted download goes on where it stopped the next time it is asked for. The marker
/// is written last: a folder without it is never loaded.
struct ModelDownloader: Sendable {
    let manifest: ModelManifest
    /// The folder the model's own folder is made in.
    let root: URL
    let transport: any ModelFileTransport

    /// Fluid Speech Mini under Application Support, fetched over a URL session.
    static func fluidSpeechMini() throws -> ModelDownloader {
        guard let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else {
            throw ModelDownloadFailure.noFolder
        }
        return ModelDownloader(manifest: .fluidSpeechMini, root: support, transport: URLSessionModelFileTransport())
    }

    var folder: URL {
        root.appendingPathComponent(manifest.folderName, isDirectory: true)
    }

    /// True once every file was checked and the marker was written.
    var isComplete: Bool {
        FileManager.default.fileExists(atPath: marker.path)
    }

    /// Makes sure every file is in place and returns the model's folder. Progress is in bytes of the
    /// whole model: checking, then downloading, then preparing once nothing is left to fetch.
    func prepare(progress: @escaping @Sendable (ModelPreparationProgress) -> Void) async throws -> URL {
        let files = FileManager.default
        let total = manifest.totalBytes
        progress(ModelPreparationProgress(fraction: 0, phase: .checking, counter: .none))

        try files.createDirectory(at: folder, withIntermediateDirectories: true)
        // The model can be downloaded again, so it stays out of backups. The flag covers what is inside.
        BackupExclusion.exclude(folder)

        let plan = ModelDownloadPlan(manifest: manifest) { path in
            ModelDownloader.size(of: place(of: path))
        }
        if !plan.isComplete {
            // The marker says that every file is here. It goes before anything else changes.
            try? files.removeItem(at: marker)
        }

        var completed = plan.presentBytes
        for file in plan.missing {
            try Task.checkCancellation()
            progress(ModelDownloader.downloading(completed, of: total))
            try await fetch(file, before: completed, of: total, progress: progress)
            completed += file.bytes
        }

        if !isComplete {
            try Data(manifest.revision.utf8).write(to: marker, options: .atomic)
        }
        progress(ModelPreparationProgress(fraction: 1, phase: .preparing, counter: .bytes(completed: total, total: total)))
        return folder
    }

    // MARK: - Pieces

    private var marker: URL {
        folder.appendingPathComponent(ModelManifest.markerName, isDirectory: false)
    }

    /// Where the file of a manifest path belongs.
    private func place(of path: String) -> URL {
        var url = folder
        for part in path.split(separator: "/") {
            url.appendPathComponent(String(part), isDirectory: false)
        }
        return url
    }

    /// Downloads one file under its temporary name, checks it and moves it into place. After a failure
    /// neither the temporary file nor a file in place is left.
    private func fetch(
        _ file: ModelManifest.File,
        before completed: Int64,
        of total: Int64,
        progress: @escaping @Sendable (ModelPreparationProgress) -> Void
    ) async throws {
        guard let url = manifest.url(for: file) else {
            throw ModelDownloadFailure.incomplete
        }
        let files = FileManager.default
        let destination = place(of: file.path)
        let directory = destination.deletingLastPathComponent()
        let temporary = directory
            .appendingPathComponent(destination.lastPathComponent + ModelManifest.temporarySuffix, isDirectory: false)

        try files.createDirectory(at: directory, withIntermediateDirectories: true)
        // What an earlier try left behind, and a file of the wrong size in place.
        try? files.removeItem(at: temporary)
        try? files.removeItem(at: destination)

        do {
            let bytes = file.bytes
            try await transport.download(url, to: temporary) { received in
                progress(ModelDownloader.downloading(completed + min(max(0, received), bytes), of: total))
            }
            try ModelDownloader.check(file, at: temporary)
            try files.moveItem(at: temporary, to: destination)
        } catch {
            try? files.removeItem(at: temporary)
            throw error
        }
    }

    /// Throws `damagedFile` unless the file at `url` has the size the manifest names and, where it names
    /// one, the SHA-256.
    private static func check(_ file: ModelManifest.File, at url: URL) throws {
        guard let size = size(of: url) else {
            throw ModelDownloadFailure.incomplete
        }
        guard file.problem(size: size) == nil else {
            throw ModelDownloadFailure.damagedFile
        }
        guard file.sha256 != nil else { return }
        guard file.problem(sha256: try FileDigest.sha256(of: url)) == nil else {
            throw ModelDownloadFailure.damagedFile
        }
    }

    /// The size of the file at `url`; nil when there is none.
    private static func size(of url: URL) -> Int64? {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: url.path),
              let size = attributes[.size] as? NSNumber else {
            return nil
        }
        return size.int64Value
    }

    private static func downloading(_ completed: Int64, of total: Int64) -> ModelPreparationProgress {
        ModelPreparationProgress(
            fraction: total > 0 ? Double(completed) / Double(total) : 0,
            phase: .downloading,
            counter: .bytes(completed: completed, total: total)
        )
    }
}

enum FileDigest {
    /// The SHA-256 of a file as 64 lower-case hexadecimal digits. The file is read a megabyte at a
    /// time, so a large model file never sits in memory whole.
    static func sha256(of url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }

        var hasher = SHA256()
        while true {
            let isAtEnd = try autoreleasepool { () -> Bool in
                guard let chunk = try handle.read(upToCount: chunkBytes), !chunk.isEmpty else {
                    return true
                }
                hasher.update(data: chunk)
                return false
            }
            if isAtEnd {
                break
            }
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    private static let chunkBytes = 1_048_576
}
