import Foundation
import LiveCueCore

/// The end-of-session summary and its cancellation (section 4.4).
extension SessionController {
    /// Called at the end of T-16 with the session as it was saved. A summary is asked for only when the
    /// answer service selected at Stop is Codex, a pairing exists and the session has a segment.
    func startSummary(for ended: Session, assistant selection: AssistantConfiguration) {
        guard selection.provider == .codex, dependencies.pairingStore.load() != nil else { return }
        let service = dependencies.answers.service(for: selection.provider)
        guard service.supportsSummary,
              let built = ContextBuilder.summaryRequest(session: ended, assistant: selection),
              let request = try? WireBudget.fitted(built)
        else {
            return
        }

        let requestID = request.requestId
        let task = Task { [weak self] in
            // Any failure is silent and final.
            let reply = try? await service.summarize(request)
            await self?.finishSummary(reply, for: ended, requestID: requestID)
        }
        summaryRuns[ended.id] = SummaryRun(requestID: requestID, task: task)
    }

    /// Cancels the summary request of a stored session, if one is in flight. Called before a delete.
    func cancelSummary(forSession id: UUID) async {
        // Forgotten first, so that a reply that still arrives is dropped.
        guard let run = summaryRuns.removeValue(forKey: id) else { return }
        // The service cancels the task that runs the request; the relay client then sends the one DELETE.
        await dependencies.answers.service(for: .codex).cancel(requestId: run.requestID)
        run.task.cancel()
    }

    /// On success, when the session is still stored: its title and notes become the reply's.
    private func finishSummary(_ reply: SummaryResponse?, for ended: Session, requestID: UUID) async {
        // A summary that was cancelled is no longer in the table.
        guard summaryRuns[ended.id]?.requestID == requestID else { return }
        guard let reply else {
            summaryRuns[ended.id] = nil
            return
        }

        let isStored = await dependencies.sessions.contains(id: ended.id)
        guard summaryRuns[ended.id]?.requestID == requestID else { return }
        summaryRuns[ended.id] = nil
        // The session was deleted before the summary arrived: the result is dropped.
        guard isStored else { return }

        var summarised = ended
        summarised.title = reply.title
        summarised.notes = SessionNotes(
            summary: reply.summary,
            keyPoints: reply.keyPoints,
            actionItems: reply.actionItems
        )
        guard (try? await dependencies.sessions.save(summarised, savedAt: dependencies.clock.now())) != nil else {
            return
        }
        await dependencies.history.reload()
    }
}
