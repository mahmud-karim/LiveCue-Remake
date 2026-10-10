using System.Net;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-017 (protocol 5.5, presence): every request that passes E-3 tells the desktop that a phone
/// was seen, whatever its route and outcome. The self-check request is the one exception.
/// </summary>
public sealed class PresenceTests
{
    private const string SelfCheckHeader = "X-LiveCue-Self-Check";

    [Fact]
    public async Task One_event_per_authenticated_request_and_none_for_the_current_nonce()
    {
        await using var host = await RelayTestHost.StartAsync();
        var expected = 0;

        // Each HTTP route once, whatever it answers.
        foreach (var route in Wire.HttpRoutes)
        {
            using var response = await host.SendAsync(host.Request(
                new HttpMethod(route.Method), Wire.TargetOf(route), json: route.ReadsBody ? "{}" : null));
            expected++;
        }

        // Answers of the edge itself count too: 404, the protocol mismatch, a body where none belongs.
        using var notFound = await host.GetAsync("/v1/unknown");
        using var mismatch = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", protocol: "1"));
        using var notUpgrade = await host.GetAsync("/v1/speech");
        expected += 3;

        // An authenticated speech upgrade counts.
        using (var socket = await host.OpenSpeechAsync())
        {
            expected++;
        }

        // A caller without the token does not.
        using var refused = await host.SendAsync(host.Request(HttpMethod.Get, "/v1/health", authorization: null));
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notUpgrade.StatusCode);
        Assert.Equal(expected, host.Events.Count<PhoneSeen>());

        // The self-check calls back into the relay with the nonce of the check in progress: no event.
        string? nonce = null;
        HttpStatusCode? selfCheckStatus = null;
        host.SelfCheck.Handler = async (call, _) =>
        {
            nonce = call.Nonce;
            using var response = await host.SendAsync(WithNonce(host, call.Nonce));
            selfCheckStatus = response.StatusCode;
            return response.StatusCode == HttpStatusCode.OK;
        };
        await host.Relay.ShowPairingAsync(CancellationToken.None);
        var completed = await host.Events.WaitForAsync<SelfCheckCompleted>();
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, selfCheckStatus);
        Assert.Equal(SelfCheckResult.Verified, completed.Result);
        Assert.Equal(expected, host.Events.Count<PhoneSeen>());

        // A wrong nonce, the nonce of a check that is over, and a repeated header are a phone like any other.
        using var wrong = await host.SendAsync(WithNonce(host, "not-the-nonce"));
        using var stale = await host.SendAsync(WithNonce(host, nonce!));
        using var twice = await host.SendAsync(WithNonce(host, nonce!, nonce!));
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Equal(expected + 3, host.Events.Count<PhoneSeen>());
    }

    [Fact]
    public async Task The_event_carries_the_time_of_the_relays_clock_and_nothing_of_the_caller()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Clock.Advance(TimeSpan.FromSeconds(42));

        var request = host.Request(HttpMethod.Get, "/v1/health");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.9");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc");
        using var response = await host.SendAsync(request);
        var seen = await host.Events.WaitForAsync<PhoneSeen>();

        Assert.Equal(new PhoneSeen(host.Clock.GetUtcNow()), seen);
        Assert.DoesNotContain("203.0.113.9", seen.ToString(), StringComparison.Ordinal);
    }

    private static HttpRequestMessage WithNonce(RelayTestHost host, params string[] values)
    {
        var request = host.Request(HttpMethod.Get, "/v1/health");
        request.Headers.TryAddWithoutValidation(SelfCheckHeader, values);
        return request;
    }
}
