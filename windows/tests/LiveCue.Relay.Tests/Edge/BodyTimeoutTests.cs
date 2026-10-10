using System.Net;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-014 (protocol 5.4 rule 3): the whole body arrives within C-41, counted from the arrival of
/// the headers on the relay's clock. No test here sleeps for the limit.
/// </summary>
public sealed class BodyTimeoutTests
{
    [Fact]
    public async Task A_body_that_stalls_is_408_and_frees_what_it_held()
    {
        await using var host = await RelayTestHost.StartAsync();
        var gauge = ((RelayHost)host.Relay).Edge!.Gauge;
        var timersAtRest = host.Clock.ScheduledTimers;

        // Half of the declared body arrives, and then nothing.
        using var raw = await host.OpenRawAsync();
        await raw.SendAsync(host.RawHead("POST /v1/assist HTTP/1.1", ("Content-Length", "100")) + "{\"requestId\":");
        await Wire.UntilAsync(() => host.Clock.ScheduledTimers >= timersAtRest + 2, "the body is being read");
        Assert.Equal(1, gauge.InFlight);

        // One tick before the limit there is no answer yet.
        host.Clock.Advance(ProtocolConstants.BodyTimeout - TimeSpan.FromMilliseconds(1));
        Assert.False(await raw.IsClosedByServerAsync(TimeSpan.FromMilliseconds(100)));
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var response = await raw.ReadResponseAsync();

        Assert.NotNull(response);
        Assert.Equal(408, response.Status);
        Assert.Equal(Wire.RequestTimeout, response.Body);
        Assert.Equal("close", response.Header("Connection"));

        // Everything the request held is free: the gauge is empty and the whole cap can be taken again.
        await Wire.UntilAsync(() => gauge.InFlight == 0, "the request left the gauge");
        host.Catalog.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = Enumerable.Range(0, ProtocolConstants.MaxRequestsInFlight).Select(_ => host.GetAsync("/v1/models")).ToList();
        await Wire.UntilAsync(() => gauge.InFlight == ProtocolConstants.MaxRequestsInFlight, "the cap is taken again");
        host.Catalog.Gate.SetResult();
        foreach (var response200 in await Task.WhenAll(held))
        {
            Assert.Equal(HttpStatusCode.OK, response200.StatusCode);
            response200.Dispose();
        }

        // The answer area was never reached.
        Assert.Empty(host.Backend.Runs);
    }

    public static TheoryData<int, int, string> EveryBodyRoute() => new()
    {
        // Route, seconds on the relay's clock until the answer, and the code.
        { (int)RouteId.PairVerify, 5, "DEADLINE_EXCEEDED" },      // the deadline is shorter than C-41: 5.5 answers
        { (int)RouteId.LocalModelStart, 10, "REQUEST_TIMEOUT" },  // equal: C-41 wins the tie
        { (int)RouteId.LocalModelStop, 10, "REQUEST_TIMEOUT" },
        { (int)RouteId.Assist, 10, "REQUEST_TIMEOUT" },
        { (int)RouteId.SessionSummary, 10, "REQUEST_TIMEOUT" },
    };

    [Theory]
    [MemberData(nameof(EveryBodyRoute))]
    public async Task The_limit_counts_from_the_headers_and_wins_a_tie_with_the_route_deadline(
        int routeNumber, int seconds, string code)
    {
        var id = (RouteId)routeNumber;
        var route = RouteTable.Row(id);
        await using var edge = await EdgeHarness.StartAsync(routes => routes[id] = new ScriptedRoute(async context =>
        {
            var body = await context.ReadJsonBodyAsync();
            if (body.Error is { } error)
            {
                await context.WriteErrorAsync(error);
                return;
            }

            await context.WriteJsonAsync(200, "{}"u8.ToArray());
        }));

        using var raw = await edge.OpenRawAsync();
        await raw.SendAsync(edge.RawHead($"POST {route.Target} HTTP/1.1", ("Content-Length", "10")) + "{");
        await Wire.UntilAsync(() => edge.Clock.ScheduledTimers == 2, "the body is being read");
        edge.Clock.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1));
        Assert.False(await raw.IsClosedByServerAsync(TimeSpan.FromMilliseconds(50)));
        edge.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var response = await raw.ReadResponseAsync();

        Assert.NotNull(response);
        Assert.Equal(RelayErrors.ForCode(code)!.Value.Status, response.Status);
        Assert.Contains($"\"code\":\"{code}\"", response.Body, StringComparison.Ordinal);
        Assert.Equal("close", response.Header("Connection"));
        await Wire.UntilAsync(() => edge.Pipeline.Gauge.InFlight == 0, "the request left the gauge");
    }

    [Fact]
    public async Task A_body_that_completes_in_time_is_read_and_its_timer_is_gone()
    {
        await using var host = await RelayTestHost.StartAsync();
        var timersAtRest = host.Clock.ScheduledTimers;

        using var raw = await host.OpenRawAsync();
        await raw.SendAsync(host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "2")) + "{");
        await Wire.UntilAsync(() => host.Clock.ScheduledTimers == timersAtRest + 2, "the body is being read");
        host.Clock.Advance(TimeSpan.FromSeconds(4));
        await raw.SendAsync("}");
        var response = await raw.ReadResponseAsync();

        Assert.Equal(200, response!.Status);
        Assert.Equal("""{"ok":true}""", response.Body);

        // The body was read to its end, so the connection is kept.
        Assert.Null(response.Header("Connection"));
        await Wire.UntilAsync(() => host.Clock.ScheduledTimers == timersAtRest, "the timers of the request are gone");
    }
}
