using System.Net;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>Protocol P2-010 (step E-7): at most C-31 authenticated HTTP requests are in flight.</summary>
public sealed class RequestGaugeTests
{
    private const int Cap = ProtocolConstants.MaxRequestsInFlight;

    [Fact]
    public async Task One_request_beyond_the_cap_is_429_and_a_finished_one_frees_a_place()
    {
        await using var host = await RelayTestHost.StartAsync();
        var gauge = ((RelayHost)host.Relay).Edge!.Gauge;
        var gate = host.Catalog.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // One below the cap is held on route 3 by the gated catalogue: there is still a place.
        var held = Enumerable.Range(0, Cap - 1).Select(_ => host.GetAsync("/v1/models")).ToList();
        await Wire.UntilAsync(() => gauge.InFlight == Cap - 1, "fifteen requests are in flight");
        using var lastPlace = await host.GetAsync("/v1/health");

        // The cap itself is held: one more is refused, whatever its route.
        held.Add(host.GetAsync("/v1/models"));
        await Wire.UntilAsync(() => gauge.InFlight == Cap, "sixteen requests are in flight");
        using var beyond = await host.GetAsync("/v1/health");
        using var beyondWithBody = await host.PostAsync("/v1/pair/verify", "{}");

        // The steps before E-7 still answer as they always do.
        using var noToken = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", authorization: null));
        using var unknown = await host.GetAsync("/v1/unknown");
        using var mismatch = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", protocol: "1"));

        // After the held requests have finished, the next one is served.
        gate.SetResult();
        var finished = await Task.WhenAll(held);
        await Wire.UntilAsync(() => gauge.InFlight == 0, "the gauge is empty again");
        using var after = await host.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, lastPlace.StatusCode);
        await Wire.AssertErrorAsync(beyond, 429, "TOO_MANY_REQUESTS");
        await Wire.AssertErrorAsync(beyondWithBody, 429, "TOO_MANY_REQUESTS");
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        await Wire.AssertErrorAsync(unknown, 404, "NOT_FOUND");
        await Wire.AssertErrorAsync(mismatch, 400, "PROTOCOL_MISMATCH");
        Assert.All(finished, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        foreach (var response in finished)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_place_is_left_however_a_request_ends()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var edge = await EdgeHarness.StartAsync(routes =>
        {
            routes[RouteId.Health] = new ScriptedRoute(_ => throw new InvalidOperationException("failure"));
            routes[RouteId.Models] = new ScriptedRoute(_ => throw new RelayException(RelayErrors.AssistantCatalogUnavailable));
            routes[RouteId.LocalModelStatus] = new ScriptedRoute(context => Task.Delay(Timeout.InfiniteTimeSpan, context.Work));
            routes[RouteId.CancelRequest] = new ScriptedRoute(async context =>
            {
                await release.Task;
                await context.WriteJsonAsync(200, "{}"u8.ToArray());
            });
        });
        var gauge = edge.Pipeline.Gauge;

        // An unexpected failure, a coded error, a body that is not JSON.
        using var failed = await edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/health"));
        using var coded = await edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/models"));
        using var badBody = await edge.SendAsync(edge.Request(HttpMethod.Post, "/v1/pair/verify", "["));
        await Wire.UntilAsync(() => gauge.InFlight == 0, "the three ended requests left the gauge");

        // A caller that goes away while its request is held.
        using (var raw = await edge.OpenRawAsync())
        {
            await raw.SendAsync(edge.RawHead("GET /v1/local-model HTTP/1.1"));
            await Wire.UntilAsync(() => gauge.InFlight == 1, "the request is held");
        }

        await Wire.UntilAsync(() => gauge.InFlight == 0, "the abandoned request left the gauge");

        // A request that outlasts its deadline.
        var late = edge.SendAsync(edge.Request(HttpMethod.Get, "/v1/local-model"));
        await Wire.UntilAsync(() => gauge.InFlight == 1, "the request is held");
        edge.Clock.Advance(ProtocolConstants.LocalModelStatusDeadline);
        using var deadline = await late.WaitAsync(TimeSpan.FromSeconds(10));
        await Wire.UntilAsync(() => gauge.InFlight == 0, "the late request left the gauge");

        // A request that simply completes.
        var slow = edge.SendAsync(edge.Request(HttpMethod.Delete, "/v1/requests/abc"));
        await Wire.UntilAsync(() => gauge.InFlight == 1, "the request is held");
        release.SetResult();
        using var completed = await slow.WaitAsync(TimeSpan.FromSeconds(10));
        await Wire.UntilAsync(() => gauge.InFlight == 0, "the completed request left the gauge");

        await Wire.AssertErrorAsync(failed, 500, "INTERNAL_ERROR");
        await Wire.AssertErrorAsync(coded, 503, "ASSISTANT_CATALOG_UNAVAILABLE");
        await Wire.AssertErrorAsync(badBody, 400, "INVALID_JSON");
        await Wire.AssertErrorAsync(deadline, 504, "DEADLINE_EXCEEDED");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public void The_gauge_counts_to_its_capacity_and_no_further()
    {
        var gauge = new RequestGauge(2);

        Assert.True(gauge.TryEnter());
        Assert.True(gauge.TryEnter());
        Assert.False(gauge.TryEnter());
        Assert.Equal(2, gauge.InFlight);
        gauge.Leave();
        Assert.True(gauge.TryEnter());
        gauge.Leave();
        gauge.Leave();
        Assert.Equal(0, gauge.InFlight);
    }
}
