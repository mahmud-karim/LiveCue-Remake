using System.Net;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Test plan ST-19 and protocol P2-019 (protocol 5.5): three totals of refused requests, told to the
/// desktop in one statistics event, at most once per W-13 on the relay's clock and only when a total
/// changed. Nothing is raised per refused request (P2-005 still holds).
/// </summary>
public sealed class EdgeStatisticsTests
{
    private static readonly TimeSpan Interval = ProtocolConstants.EdgeStatisticsInterval;

    [Fact]
    public async Task A_thousand_of_each_kind_give_exact_counts()
    {
        const int each = 1_000;
        await using var host = await RelayTestHost.StartAsync(options => options.Mode = ConnectionMode.Private);
        var edge = ((RelayHost)host.Relay).Edge!;

        // N refused for a browser header or a malformed Authorization, M with a well-formed token that is
        // not accepted, K with the accepted token and the Funnel header; and, between them, requests
        // that are served or answered 404, which count nowhere.
        var requests = new List<Func<HttpRequestMessage>>();
        for (var index = 0; index < each; index++)
        {
            var kind = index % 5;
            requests.Add(kind switch
            {
                0 => () => With(host.Request(HttpMethod.Get, "/v1/health"), "Origin", "https://bad.test"),
                1 => () => With(host.Request(HttpMethod.Post, "/v1/assist"), "Sec-Fetch-Site", "cross-site"),
                2 => () => host.Request(HttpMethod.Get, "/v1/health", authorization: null),
                3 => () => host.Request(HttpMethod.Get, "/v1/models", "Bearer " + host.Token + "="),
                _ => () => host.Request(HttpMethod.Delete, "/v1/requests/x", "bearer " + host.Token),
            });
            requests.Add(() => host.Request(
                HttpMethod.Get, "/v1/health", TestTokens.Bearer(TestTokens.WellFormedUnknown)));
            requests.Add(index % 2 == 0
                ? () => With(host.Request(HttpMethod.Get, "/v1/health"), "Tailscale-Funnel-Request", "?1")
                : () => With(
                    With(host.Request(HttpMethod.Get, "/v1/health"), "Tailscale-Funnel-Request", "?1"),
                    "Origin",
                    "https://bad.test"));
            if (index % 10 == 0)
            {
                requests.Add(() => host.Request(HttpMethod.Get, "/v1/health"));
                requests.Add(() => host.Request(HttpMethod.Get, "/v1/unknown"));
            }
        }

        var served = 0;
        await Parallel.ForEachAsync(
            requests,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (create, _) =>
            {
                using var response = await host.SendAsync(create());
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    Interlocked.Increment(ref served);
                }
            });
        await host.Events.SettleAsync();

        Assert.Equal(each, edge.Malformed);
        Assert.Equal(each, edge.TokenNotAccepted);
        Assert.Equal(each, edge.FunnelInPrivateMode);
        Assert.Equal(2 * (each / 10), served);

        // No event so far besides the start and the presence of the requests that were authenticated.
        Assert.Empty(host.Events.OfType<EdgeStatistics>());
        Assert.Equal(served, host.Events.Count<PhoneSeen>());
        Assert.Equal(served + 1, host.Events.Snapshot().Count);

        host.Clock.Advance(Interval);
        await host.Events.SettleAsync();

        Assert.Equal(
            new EdgeStatistics(host.Clock.GetUtcNow(), each, each, each),
            Assert.Single(host.Events.OfType<EdgeStatistics>()));
    }

    [Fact]
    public async Task At_most_one_event_per_interval_and_none_without_a_change()
    {
        await using var host = await RelayTestHost.StartAsync();
        var start = host.Clock.GetUtcNow();

        // Nothing was refused: no event, however long the relay runs.
        host.Clock.Advance(Interval * 3);
        await host.Events.SettleAsync();
        Assert.Empty(host.Events.OfType<EdgeStatistics>());

        // Three refusals. Until the interval ends there is still no event.
        for (var index = 0; index < 3; index++)
        {
            using var refused = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", authorization: null));
        }

        host.Clock.Advance(Interval - TimeSpan.FromSeconds(1));
        await host.Events.SettleAsync();
        Assert.Empty(host.Events.OfType<EdgeStatistics>());

        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await host.Events.SettleAsync();
        Assert.Equal(
            new EdgeStatistics(start + (Interval * 4), 3, 0, 0),
            Assert.Single(host.Events.OfType<EdgeStatistics>()));

        // No change, no event.
        host.Clock.Advance(Interval * 5);
        await host.Events.SettleAsync();
        Assert.Single(host.Events.OfType<EdgeStatistics>());

        // Many refusals inside one interval are one event, with the totals since the relay started.
        for (var index = 0; index < 20; index++)
        {
            using var refused = await host.SendAsync(host.Request(
                HttpMethod.Get, "/v1/health", TestTokens.Bearer(TestTokens.WellFormedUnknown)));
        }

        using var funnel = await host.SendAsync(
            With(host.Request(HttpMethod.Get, "/v1/health"), "Tailscale-Funnel-Request", "?1"));
        host.Clock.Advance(Interval);
        await host.Events.SettleAsync();

        var events = host.Events.OfType<EdgeStatistics>();
        Assert.Equal(2, events.Count);
        Assert.Equal(new EdgeStatistics(start + (Interval * 10), 3, 20, 1), events[1]);
        Assert.True(events[1].At - events[0].At >= Interval);
    }

    [Fact]
    public async Task Stop_publishes_the_last_totals()
    {
        await using var host = await RelayTestHost.StartAsync();
        using (await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", authorization: null)))
        {
        }

        host.Clock.Advance(Interval);
        using (await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", "Bearer nonsense")))
        {
        }

        using (await host.SendAsync(host.Request(
            HttpMethod.Get, "/v1/health", TestTokens.Bearer(TestTokens.WellFormedUnknown))))
        {
        }

        // Well inside the interval: the periodic event would not come yet, the stop publishes it.
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        await host.Relay.StopAsync(CancellationToken.None);
        await host.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var events = host.Events.Snapshot();
        var statistics = host.Events.OfType<EdgeStatistics>();
        Assert.Equal(2, statistics.Count);
        Assert.Equal((1L, 0L, 0L), (statistics[0].Malformed, statistics[0].TokenNotAccepted, statistics[0].FunnelInPrivateMode));

        // The last totals come just before the last event of the instance.
        Assert.Equal(new EdgeStatistics(host.Clock.GetUtcNow(), 2, 1, 0), statistics[1]);
        Assert.Same(statistics[1], events[^2]);
        Assert.Equal(new RelayStopped(host.Clock.GetUtcNow(), RelayStopReason.Requested), events[^1]);
    }

    [Fact]
    public async Task A_stop_without_a_change_publishes_no_totals()
    {
        await using var quiet = await RelayTestHost.StartAsync();
        await quiet.Relay.StopAsync(CancellationToken.None);
        await quiet.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        await using var reported = await RelayTestHost.StartAsync();
        using (await reported.SendAsync(reported.Request(HttpMethod.Get, "/v1/health", authorization: null)))
        {
        }

        reported.Clock.Advance(Interval);
        await reported.Relay.StopAsync(CancellationToken.None);
        await reported.Events.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(quiet.Events.OfType<EdgeStatistics>());
        Assert.Single(reported.Events.OfType<EdgeStatistics>());
        Assert.IsType<RelayStopped>(reported.Events.Snapshot()[^1]);
    }

    [Fact]
    public async Task No_event_is_raised_per_request()
    {
        await using var host = await RelayTestHost.StartAsync();

        for (var index = 0; index < 60; index++)
        {
            using var none = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", authorization: null));
            using var unknown = await host.SendAsync(host.Request(
                HttpMethod.Post, "/v1/assist", TestTokens.Bearer(TestTokens.WellFormedUnknown), json: Bodies.Assist()));
            using var browser = await host.SendAsync(
                With(host.Request(HttpMethod.Get, "/v1/health"), "Origin", "https://bad.test"));
            using var funnel = await host.SendAsync(
                With(host.Request(HttpMethod.Get, "/v1/health"), "Tailscale-Funnel-Request", "?1"));
        }

        // Just short of the interval, 240 refusals have raised nothing at all.
        host.Clock.Advance(Interval - TimeSpan.FromMilliseconds(1));
        await host.Events.SettleAsync();

        Assert.IsType<RelayStarted>(Assert.Single(host.Events.Snapshot()));
        Assert.Empty(host.Backend.Runs);
        Assert.Equal(0, host.Catalog.Calls);
        Assert.Equal(0, host.Lab.ProbeCalls);
    }

    [Fact]
    public async Task The_totals_belong_to_the_instance_and_public_mode_never_counts_funnel()
    {
        await using var first = await RelayTestHost.StartAsync(options => options.Mode = ConnectionMode.Public);
        using (await first.SendAsync(With(first.Request(HttpMethod.Get, "/v1/health"), "Tailscale-Funnel-Request", "?1")))
        {
        }

        using (await first.SendAsync(With(
            With(first.Request(HttpMethod.Get, "/v1/health"), "Tailscale-Funnel-Request", "?1"), "Origin", "x")))
        {
        }

        var firstEdge = ((RelayHost)first.Relay).Edge!;
        await using var second = await RelayTestHost.StartAsync();
        var secondEdge = ((RelayHost)second.Relay).Edge!;

        // In Public mode a Funnel-marked request is served; with a browser header it is "malformed".
        Assert.Equal((1L, 0L, 0L), (firstEdge.Malformed, firstEdge.TokenNotAccepted, firstEdge.FunnelInPrivateMode));
        Assert.Equal((0L, 0L, 0L), (secondEdge.Malformed, secondEdge.TokenNotAccepted, secondEdge.FunnelInPrivateMode));
    }

    private static HttpRequestMessage With(HttpRequestMessage request, string header, string value)
    {
        request.Headers.TryAddWithoutValidation(header, value);
        return request;
    }
}
