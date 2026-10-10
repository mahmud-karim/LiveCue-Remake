using System.Globalization;
using System.Net;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// The one slot of assist and summary requests (C-33; statement P2-056). While one request is in
/// flight a second one is 429 ASSISTANT_BUSY, on either route; the slot is free again however the first
/// one ends. After every answered ending the very next request is sent at once, with no waiting: the
/// slot is left before the response is written.
/// </summary>
public sealed class AnswerSlotTests
{
    [Fact]
    public async Task A_second_request_is_busy_and_the_slot_frees_after_success_failure_cancel_deadline_and_abort()
    {
        await using var host = await RelayTestHost.StartAsync();

        // Busy, in every combination of the two routes.
        foreach (var held in (string[])[AnswerTesting.Assist, AnswerTesting.Summary])
        {
            var gate = host.Backend.Gate = AnswerTesting.NewGate();
            var runs = host.Backend.Runs.Count;
            var first = host.PostAsync(held, Bodies.Assist("holds-the-slot"));
            await AnswerTesting.RunsStartedAsync(host, runs + 1);

            using var assist = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist("second"));
            using var summary = await host.PostAsync(AnswerTesting.Summary, Bodies.Summary("third"));
            await AnswerTesting.AssertErrorAsync(assist, RelayErrors.AssistantBusy);
            await AnswerTesting.AssertErrorAsync(summary, RelayErrors.AssistantBusy);
            Assert.Equal(runs + 1, host.Backend.Runs.Count);

            // Success frees the slot.
            gate.SetResult();
            using var succeeded = await first;
            Assert.Equal(HttpStatusCode.OK, succeeded.StatusCode);
            host.Backend.Gate = null;
            await AssertFreeAsync(host);
        }

        // Failure frees the slot.
        host.Backend.AssistResult = null;
        using (var failed = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist("fails")))
        {
            await AnswerTesting.AssertErrorAsync(failed, RelayErrors.AssistantFailed);
        }

        host.Backend.AssistResult = Bodies.ValidAssistOutput();
        await AssertFreeAsync(host);

        // A refusal before the run frees the slot: the body, the id, the selection.
        foreach (var refused in (string[])["{", "{}", """{"requestId":"r"}""", """{"requestId":"r","assistant":{"model":"gpt-none","reasoningEffort":"low"}}"""])
        {
            using var response = await host.PostAsync(AnswerTesting.Assist, refused);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertFreeAsync(host);
        }

        // Cancel frees the slot.
        host.Backend.Gate = AnswerTesting.NewGate();
        var before = host.Backend.Runs.Count;
        var toCancel = host.PostAsync(AnswerTesting.Assist, Bodies.Assist("to-cancel"));
        await AnswerTesting.RunsStartedAsync(host, before + 1);
        using (var cancel = await AnswerTesting.CancelAsync(host, "to-cancel"))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        }

        using (var cancelled = await toCancel)
        {
            await AnswerTesting.AssertErrorAsync(cancelled, RelayErrors.RequestCancelled);
        }

        host.Backend.Gate = null;
        await AssertFreeAsync(host);

        // The deadline frees the slot.
        host.Backend.NeverReturns = true;
        before = host.Backend.Runs.Count;
        var tooSlow = host.PostAsync(AnswerTesting.Summary, Bodies.Summary("too-slow"));
        await AnswerTesting.RunsStartedAsync(host, before + 1);
        host.Clock.Advance(ProtocolConstants.AnswerDeadline);
        using (var timedOut = await tooSlow)
        {
            await AnswerTesting.AssertErrorAsync(timedOut, RelayErrors.AssistantTimeout);
        }

        host.Backend.NeverReturns = false;
        await AssertFreeAsync(host);

        // A client that goes away frees the slot, with no response to wait for.
        host.Backend.NeverReturns = true;
        before = host.Backend.Runs.Count;
        var body = Bodies.Assist("goes-away");
        using (var raw = await host.OpenRawAsync())
        {
            await raw.SendAsync(host.RawHead(
                "POST /v1/assist HTTP/1.1",
                ("Content-Type", "application/json"),
                ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture))) + body);
            await AnswerTesting.RunsStartedAsync(host, before + 1);
        }

        host.Backend.NeverReturns = false;
        await AnswerTesting.UntilTheSlotIsFreeAsync(host);
        await AssertFreeAsync(host);

        // Every run that did not return by itself was ended: cancel, deadline, abort.
        Assert.Equal(3, host.Backend.Cancelled);
    }

    /// <summary>The next request, sent at once, is served: it runs and is answered 200.</summary>
    private static async Task AssertFreeAsync(RelayTestHost host)
    {
        using var next = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist("next"));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }
}
