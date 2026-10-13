using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// The one deadline of an answer request (C-43; protocol 8.4; statement P2-059): 60 s counted from the
/// arrival of the request, with the catalogue check inside it. Time is the manual clock.
/// </summary>
public sealed class AnswerDeadlineTests
{
    [Theory]
    [InlineData(AnswerTesting.Assist)]
    [InlineData(AnswerTesting.Summary)]
    public async Task A_backend_that_never_returns_is_504_at_the_deadline_counted_from_arrival(string route)
    {
        await using var host = await RelayTestHost.StartAsync();
        var catalogue = host.Catalog.Gate = AnswerTesting.NewGate();
        host.Backend.NeverReturns = true;

        // The catalogue is delayed first: 20 s of the 60 are gone before the run starts.
        var request = host.PostAsync(route, Bodies.Assist());
        await Waiting.UntilAsync(() => host.Catalog.Calls == 1);
        host.Clock.Advance(TimeSpan.FromSeconds(20));
        catalogue.SetResult();
        await AnswerTesting.RunsStartedAsync(host, 1);

        // 59.999 s after the arrival nothing has happened.
        host.Clock.Advance(TimeSpan.FromSeconds(40) - TimeSpan.FromMilliseconds(1));
        await Task.Delay(150);
        Assert.False(request.IsCompleted);
        Assert.Equal(0, host.Backend.Cancelled);

        // At 60 s the run is killed and the request is answered: a time-out, not a failure.
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));

        await AnswerTesting.AssertErrorAsync(response, RelayErrors.AssistantTimeout);
        Assert.Equal(
            """{"error":"The local assistant request timed out.","code":"ASSISTANT_TIMEOUT"}""",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(1, host.Backend.Cancelled);
        Assert.Equal(TimeSpan.FromSeconds(60), ProtocolConstants.AnswerDeadline);

        var failed = await host.Events.WaitForAsync<RequestFailed>();
        await host.Events.SettleAsync();
        Assert.Equal((RelayCodes.AssistantTimeout, "The local assistant request timed out."), (failed.Code, failed.Message));
        Assert.Single(host.Events.OfType<RequestStarted>());
        Assert.Single(host.Events.OfType<RequestFailed>());
    }

    [Fact]
    public async Task A_catalogue_that_never_answers_is_504_at_the_deadline_and_no_request_event_is_raised()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Catalog.Gate = AnswerTesting.NewGate();

        var request = host.PostAsync(AnswerTesting.Assist, Bodies.Assist());
        await Waiting.UntilAsync(() => host.Catalog.Calls == 1);
        host.Clock.Advance(ProtocolConstants.AnswerDeadline - TimeSpan.FromMilliseconds(1));
        await Task.Delay(150);
        Assert.False(request.IsCompleted);
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));

        // The deadline passed before the request started: the answer is the same, the desktop saw nothing.
        await AnswerTesting.AssertErrorAsync(response, RelayErrors.AssistantTimeout);
        Assert.Empty(host.Backend.Runs);
        await host.Events.SettleAsync();
        Assert.Equal(0, host.Events.Count<RequestStarted>());
        Assert.Equal(0, host.Events.Count<RequestFailed>());
        await AnswerTesting.UntilTheSlotIsFreeAsync(host);
    }

    [Fact]
    public async Task A_run_that_returns_just_inside_the_deadline_is_answered()
    {
        await using var host = await RelayTestHost.StartAsync();
        var gate = host.Backend.Gate = AnswerTesting.NewGate();
        var timersWhileIdle = host.Clock.ScheduledTimers;

        var request = host.PostAsync(AnswerTesting.Assist, Bodies.Assist());
        await AnswerTesting.RunsStartedAsync(host, 1);
        Assert.True(host.Clock.ScheduledTimers > timersWhileIdle);
        host.Clock.Advance(ProtocolConstants.AnswerDeadline - TimeSpan.FromMilliseconds(1));
        gate.SetResult();
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var execution = (await AnswerTesting.ReadJsonAsync(response)).GetProperty("execution");
        Assert.Equal(59999, execution.GetProperty("codexMs").GetDouble());
        Assert.Equal(59999, execution.GetProperty("relayTotalMs").GetDouble());

        // The deadline's timer went with the request.
        await Waiting.UntilAsync(() => host.Clock.ScheduledTimers == timersWhileIdle);
    }
}
