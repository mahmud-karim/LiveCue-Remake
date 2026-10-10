using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// W-design 4.2 and 4.9: when the relay stops, the one token that fires makes a run in flight kill its
/// process and end. The request is not answered; the desktop is told with the code RELAY_STOPPING.
/// </summary>
public sealed class AnswerStopTests
{
    [Fact]
    public async Task A_run_in_flight_when_the_relay_stops_is_killed_and_reported_before_the_relay_has_stopped()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Backend.NeverReturns = true;

        var request = host.PostAsync(AnswerTesting.Assist, Bodies.Assist("in-flight"));
        await AnswerTesting.RunsStartedAsync(host, 1);
        await host.Relay.StopAsync(CancellationToken.None);

        // The connection went with the listener: the phone sees a transport failure, never an answer.
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => request);
        Assert.Equal(1, host.Backend.Cancelled);

        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var events = host.Events.Snapshot().ToList();
        var failed = Assert.Single(events.OfType<RequestFailed>());
        Assert.Equal(("in-flight", RelayCodes.RelayStopping, "The PC relay is stopping."), (failed.RequestId, failed.Code, failed.Message));
        Assert.True(events.IndexOf(failed) < events.FindIndex(relayEvent => relayEvent is RelayStopped));
        Assert.DoesNotContain(events, relayEvent => relayEvent is RequestCompleted);
    }
}
