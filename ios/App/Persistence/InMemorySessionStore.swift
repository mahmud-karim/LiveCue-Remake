import Foundation
import LiveCueCore

/// The session store of fixture mode and of tests. Nothing leaves memory.
actor InMemorySessionStore: SessionStoring {
    private struct Row {
        var session: Session
        var savedAt: Date
    }

    private var rows: [UUID: Row] = [:]

    init() {}

    func loadAll() async -> [Session] {
        rows.values.map(\.session).sorted { first, second in
            if first.startedAt != second.startedAt {
                return first.startedAt > second.startedAt
            }
            return first.id.uuidString < second.id.uuidString
        }
    }

    func save(_ session: Session, savedAt: Date) async throws {
        rows[session.id] = Row(session: session, savedAt: savedAt)
    }

    func delete(id: UUID) async throws {
        rows[id] = nil
    }

    func contains(id: UUID) async -> Bool {
        rows[id] != nil
    }

    @discardableResult
    func closeInterruptedSessions() async -> Int {
        var closed = 0
        for (id, row) in rows where row.session.endedAt == nil {
            rows[id] = Row(session: row.session.closedAfterInterruption(at: row.savedAt), savedAt: row.savedAt)
            closed += 1
        }
        return closed
    }
}
