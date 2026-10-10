import Foundation
import LiveCueCore

/// Sessions on disk: one JSON file per session, `<session id>.json`, in the folder `Sessions` under
/// Application Support. Each file holds one session, encoded with `LiveCueJSON`, together with its
/// last-saved time, and is written atomically. The folder is protected "complete until first user
/// authentication". A file that does not decode is skipped silently on load.
///
/// A file name is always built from a session's id, never from text that came out of a file. A file
/// whose name is not the id of the session inside it is skipped like one that does not decode, so no
/// session can appear twice. Nothing is ever deleted except by `delete(id:)`.
actor FileSessionStore: SessionStoring {
    /// The folder under Application Support.
    static let folderName = "Sessions"
    /// Every file is replaced in one step and protected until the first unlock after a restart.
    static let fileWritingOptions: Data.WritingOptions = [.atomic, .completeFileProtectionUntilFirstUserAuthentication]
    static let folderProtection = FileProtectionType.completeUntilFirstUserAuthentication

    /// What one file holds.
    private struct StoredSession: Codable {
        var savedAt: Date
        var session: Session
    }

    private let directory: URL

    /// Opens the store, creating its folder when needed. `directory` nil means `Sessions` under
    /// Application Support; tests pass a temporary folder.
    static func open(directory: URL? = nil) throws -> FileSessionStore {
        let fileManager = FileManager.default
        let folder: URL
        if let directory {
            folder = directory
        } else {
            let support = try fileManager.url(
                for: .applicationSupportDirectory,
                in: .userDomainMask,
                appropriateFor: nil,
                create: true
            )
            folder = support.appendingPathComponent(folderName, isDirectory: true)
        }

        try fileManager.createDirectory(at: folder, withIntermediateDirectories: true)
        // Set on its own, so that a folder that was already there gets it too. A system without file
        // protection may refuse, and that is not a reason to lose history: every file asks for the
        // same protection when it is written.
        try? fileManager.setAttributes([.protectionKey: folderProtection], ofItemAtPath: folder.path)
        return FileSessionStore(directory: folder)
    }

    private init(directory: URL) {
        self.directory = directory
    }

    func loadAll() async -> [Session] {
        readAll().map(\.session).sorted { first, second in
            if first.startedAt != second.startedAt {
                return first.startedAt > second.startedAt
            }
            return first.id.uuidString < second.id.uuidString
        }
    }

    func save(_ session: Session, savedAt: Date) async throws {
        try write(StoredSession(savedAt: savedAt, session: session))
    }

    func delete(id: UUID) async throws {
        let url = fileURL(for: id)
        guard FileManager.default.fileExists(atPath: url.path) else { return }
        try FileManager.default.removeItem(at: url)
    }

    func contains(id: UUID) async -> Bool {
        FileManager.default.fileExists(atPath: fileURL(for: id).path)
    }

    @discardableResult
    func closeInterruptedSessions() async -> Int {
        var closed = 0
        for stored in readAll() where stored.session.endedAt == nil {
            let session = stored.session.closedAfterInterruption(at: stored.savedAt)
            do {
                try write(StoredSession(savedAt: stored.savedAt, session: session))
                closed += 1
            } catch {
                // The file stays as it was; the session is closed at the next launch.
            }
        }
        return closed
    }

    // MARK: - Files

    /// `<session id>.json`, the id as `uuidString`.
    private func fileURL(for id: UUID) -> URL {
        directory.appendingPathComponent(id.uuidString + ".json", isDirectory: false)
    }

    private func write(_ stored: StoredSession) throws {
        let data = try LiveCueJSON.encode(stored)
        try data.write(to: fileURL(for: stored.session.id), options: FileSessionStore.fileWritingOptions)
    }

    /// Every file of the folder that holds a session under its own id. Anything else is left alone.
    private func readAll() -> [StoredSession] {
        let fileManager = FileManager.default
        guard let urls = try? fileManager.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil) else {
            return []
        }
        var found: [StoredSession] = []
        for url in urls where url.pathExtension == "json" {
            guard let data = try? Data(contentsOf: url),
                  let stored = try? LiveCueJSON.decode(StoredSession.self, from: data),
                  url.lastPathComponent == stored.session.id.uuidString + ".json"
            else { continue }
            found.append(stored)
        }
        return found
    }
}
