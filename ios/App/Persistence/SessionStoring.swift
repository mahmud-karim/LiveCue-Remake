import Foundation
import LiveCueCore

protocol SessionStoring: Sendable {
    /// Newest `startedAt` first. Sessions that fail to decode are skipped.
    func loadAll() async -> [Session]
    /// Inserts or replaces the session with this id and records `savedAt` as its last-saved time.
    func save(_ session: Session, savedAt: Date) async throws
    func delete(id: UUID) async throws
    func contains(id: UUID) async -> Bool
    /// Launch recovery: every stored session without an end time is replaced by
    /// `closedAfterInterruption(at:)` with its last-saved time. Returns how many were closed.
    @discardableResult func closeInterruptedSessions() async -> Int
}
