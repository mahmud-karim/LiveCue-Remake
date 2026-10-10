import Foundation
import LiveCueCore

/// Sessions on disk: one JSON file per session, `<session id>.json`, in the folder `Sessions` under
/// Application Support. Each file holds one session, encoded with `LiveCueJSON`, together with its
/// last-saved time, and is written atomically. The folder is protected "complete until first user
/// authentication". A file that does not decode is skipped silently on load.
actor FileSessionStore: SessionStoring {
    /// Opens the store, creating its folder when needed.
    static func open() throws -> FileSessionStore {
        // STUB(WP-06)
        // Until this store exists the live wiring falls back to `InMemorySessionStore`.
        throw CocoaError(.featureUnsupported)
    }

    private init() {}

    func loadAll() async -> [Session] {
        // STUB(WP-06)
        []
    }

    func save(_ session: Session, savedAt: Date) async throws {
        // STUB(WP-06)
    }

    func delete(id: UUID) async throws {
        // STUB(WP-06)
    }

    func contains(id: UUID) async -> Bool {
        // STUB(WP-06)
        false
    }

    @discardableResult
    func closeInterruptedSessions() async -> Int {
        // STUB(WP-06)
        0
    }
}
