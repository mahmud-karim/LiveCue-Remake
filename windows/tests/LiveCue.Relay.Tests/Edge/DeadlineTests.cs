using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-018 (protocol 5.5): each route has a relay deadline, counted from the arrival of the
/// request headers on the relay's clock. The work here is a handler that never ends by itself, so the
/// test does not depend on what another area does with its time.
/// </summary>
public sealed class DeadlineTests
{
    public static TheoryData<int> EveryHttpRoute() => [.. Wire.HttpRoutes.Select(route => (int)route.Id)];

    [Theory]
    [MemberData(nameof(EveryHttpRoute))]
    public async Task Work_past_the_route_deadline_is_504(int routeNumber)
    {
        var id = (RouteId)routeNumber;
        var route = RouteTable.Row(id);
        var working = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var edge = await EdgeHarness.StartAsync(routes => routes[id] = new ScriptedRoute(async context =>
        {
            if (context.Route.ReadsBody)
            {
                await context.ReadJsonBodyAsync();
            }

            working.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.Work);
        }));

        var pending = edge.SendAsync(edge.RequestFor(route));
        await working.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Just before the deadline nothing has been answered.
        edge.Clock.Advance(route.Deadline - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);

        edge.Clock.Advance(TimeSpan.FromMilliseconds(1));
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        // Routes 7 and 8 have a code of their own for it.
        await Wire.AssertErrorAsync(
            response, 504, id is RouteId.Assist or RouteId.SessionSummary ? "ASSISTANT_TIMEOUT" : "DEADLINE_EXCEEDED");
        await Wire.UntilAsync(() => edge.Pipeline.Gauge.InFlight == 0, "the request left the gauge");
    }

    [Fact]
    public void The_table_holds_the_deadlines_of_the_protocol()
    {
        Assert.Equal(
            [5, 5, 40, 5, 10, 10, 60, 60, 5],
            Wire.HttpRoutes.Select(route => (int)route.Deadline.TotalSeconds));
    }

    [Fact]
    public async Task A_stalled_body_on_a_route_with_a_shorter_deadline_is_504()
    {
        // Route 2 through the whole relay: its deadline (5 s) passes before the body limit C-41 (10 s),
        // so the deadline answers (protocol 5.4 rule 3).
        await using var host = await RelayTestHost.StartAsync();
        var timersAtRest = host.Clock.ScheduledTimers;

        using var raw = await host.OpenRawAsync();
        await raw.SendAsync(host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "10")) + "{\"a\"");
        await Wire.UntilAsync(() => host.Clock.ScheduledTimers == timersAtRest + 2, "the body is being read");
        host.Clock.Advance(ProtocolConstants.PairVerifyDeadline);
        var response = await raw.ReadResponseAsync();

        Assert.NotNull(response);
        Assert.Equal(504, response.Status);
        Assert.Equal(Wire.DeadlineExceeded, response.Body);
        Assert.Equal("close", response.Header("Connection"));
    }

    [Fact]
    public async Task The_speech_route_has_no_deadline()
    {
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var edge = await EdgeHarness.StartAsync(routes => routes[RouteId.Speech] = new ScriptedRoute(async context =>
        {
            using var socket = await context.AcceptWebSocketAsync();
            open.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.Work);
        }));

        using var raw = await edge.OpenRawAsync();
        await raw.SendAsync(edge.RawHead("GET /v1/speech HTTP/1.1", Wire.UpgradeHeaders));
        var upgrade = await Wire.ReadRawAsync(raw, hasBody: false);
        await open.Task.WaitAsync(TimeSpan.FromSeconds(10));
        edge.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal("HTTP/1.1 101 Switching Protocols", Wire.StatusLine(upgrade!));
        Assert.False(await raw.IsClosedByServerAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(1, edge.Pipeline.Active);
        Assert.Equal(0, edge.Pipeline.Gauge.InFlight);
    }
}
