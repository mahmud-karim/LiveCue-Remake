using System.Net;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// Statement P2-051 for the answer routes: while the relay is paused, routes 7 and 8 are refused at R-1,
/// before anything else is looked at, and routes 3 and 9 are served. A run that began before the pause
/// continues.
/// </summary>
public sealed class AnswerPauseTests
{
    [Fact]
    public async Task Paused_refuses_assist_and_summary_and_serves_models_and_cancel()
    {
        await using var host = await RelayTestHost.StartAsync();
        await host.Relay.SetAcceptingAsync(false);

        using var assist = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist());
        using var summary = await host.PostAsync(AnswerTesting.Summary, Bodies.Summary());

        // R-1 comes first: neither a broken body nor a missing id changes the answer.
        using var brokenBody = await host.PostAsync(AnswerTesting.Assist, "{");
        using var noRequestId = await host.PostAsync(AnswerTesting.Summary, "{}");
        using var models = await host.GetAsync("/v1/models");
        using var cancel = await AnswerTesting.CancelAsync(host, Bodies.RequestId);

        await AnswerTesting.AssertErrorAsync(assist, RelayErrors.RelayPaused);
        await AnswerTesting.AssertErrorAsync(summary, RelayErrors.RelayPaused);
        await AnswerTesting.AssertErrorAsync(brokenBody, RelayErrors.RelayPaused);
        await AnswerTesting.AssertErrorAsync(noRequestId, RelayErrors.RelayPaused);
        Assert.Equal(
            """{"error":"PC relay is paused. Resume it in LiveCue Desktop.","code":"RELAY_PAUSED"}""",
            await assist.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, models.StatusCode);
        await AnswerTesting.AssertErrorAsync(cancel, RelayErrors.RequestNotFound);
        Assert.Empty(host.Backend.Runs);

        // Resumed, the same request is served.
        await host.Relay.SetAcceptingAsync(true);
        using var resumed = await host.PostAsync(AnswerTesting.Assist, Bodies.Assist());
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
    }

    [Fact]
    public async Task A_run_that_began_before_the_pause_continues_and_can_still_be_cancelled()
    {
        await using var host = await RelayTestHost.StartAsync();
        var gate = host.Backend.Gate = AnswerTesting.NewGate();

        var first = host.PostAsync(AnswerTesting.Assist, Bodies.Assist("before-the-pause"));
        await AnswerTesting.RunsStartedAsync(host, 1);
        await host.Relay.SetAcceptingAsync(false);
        gate.SetResult();
        using var completed = await first;

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        // The same with a run that the phone cancels while the relay is paused.
        await host.Relay.SetAcceptingAsync(true);
        host.Backend.Gate = AnswerTesting.NewGate();
        var second = host.PostAsync(AnswerTesting.Summary, Bodies.Summary("cancelled-while-paused"));
        await AnswerTesting.RunsStartedAsync(host, 2);
        await host.Relay.SetAcceptingAsync(false);
        using var cancel = await AnswerTesting.CancelAsync(host, "cancelled-while-paused");
        using var cancelled = await second;

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        await AnswerTesting.AssertErrorAsync(cancelled, RelayErrors.RequestCancelled);
    }
}
