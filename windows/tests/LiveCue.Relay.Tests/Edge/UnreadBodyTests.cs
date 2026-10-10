using System.Globalization;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Test plan ST-20 (W-design K-4, security review SEC-15): no unread body stays on a connection that is
/// kept. The proxy in front of the relay may reuse one loopback connection for different callers, so
/// what is left of one caller's body must never be read as the next caller's request.
///
/// The cases here are the answers the edge and route 2 write before the body is read. The two that need
/// the real answer area (RELAY_PAUSED on routes 7 and 8, ASSISTANT_BUSY) are in the conformance sweep.
/// </summary>
public sealed class UnreadBodyTests
{
    [Theory]
    [InlineData("401", 401, "UNAUTHORIZED")]
    [InlineData("404", 404, "NOT_FOUND")]
    [InlineData("PROTOCOL_MISMATCH", 400, "PROTOCOL_MISMATCH")]
    [InlineData("429 at the gauge", 429, "TOO_MANY_REQUESTS")]
    [InlineData("413 by declared length", 413, "PAYLOAD_TOO_LARGE")]
    [InlineData("RELAY_PAUSED on route 5", 503, "RELAY_PAUSED")]
    [InlineData("400 for a body where none belongs", 400, "INVALID_REQUEST")]
    public async Task An_answer_before_the_body_closes_the_connection(string which, int status, string code)
    {
        await using var host = await RelayTestHost.StartAsync();
        var gauge = ((RelayHost)host.Relay).Edge!.Gauge;
        var bearer = ("Authorization", TestTokens.Bearer(host.Token));
        var protocol = ("X-LiveCue-Protocol", "2");

        // The second request is complete and valid: on a kept connection it would be answered 200.
        var second = host.RawHead("GET /v1/health HTTP/1.1");

        // The client declares N bytes and sends N/2. For the small N the second request is longer than
        // the missing half, so that it would complete the body if anything still counted it.
        var declared = which.StartsWith("413", StringComparison.Ordinal) ? 200_000 : 128;
        Assert.True(second.Length > 64);
        var length = ("Content-Length", declared.ToString(CultureInfo.InvariantCulture));
        var head = which switch
        {
            "401" => Wire.Head(host.Port, "POST /v1/assist HTTP/1.1", protocol, length),
            "404" => Wire.Head(host.Port, "POST /v1/nowhere HTTP/1.1", bearer, protocol, length),
            "PROTOCOL_MISMATCH" => Wire.Head(host.Port, "POST /v1/assist HTTP/1.1", bearer, ("X-LiveCue-Protocol", "1"), length),
            "RELAY_PAUSED on route 5" => Wire.Head(host.Port, "POST /v1/local-model/start HTTP/1.1", bearer, protocol, length),
            "400 for a body where none belongs" => Wire.Head(host.Port, "GET /v1/health HTTP/1.1", bearer, protocol, length),
            _ => Wire.Head(host.Port, "POST /v1/pair/verify HTTP/1.1", bearer, protocol, length),
        };

        var authenticatedBefore = 0;
        var held = new List<Task<HttpResponseMessage>>();
        if (which.StartsWith("429", StringComparison.Ordinal))
        {
            host.Catalog.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            held = [.. Enumerable.Range(0, ProtocolConstants.MaxRequestsInFlight).Select(_ => host.GetAsync("/v1/models"))];
            await Wire.UntilAsync(() => gauge.InFlight == ProtocolConstants.MaxRequestsInFlight, "the gauge is full");
            authenticatedBefore = ProtocolConstants.MaxRequestsInFlight;
        }
        else if (which.StartsWith("RELAY_PAUSED", StringComparison.Ordinal))
        {
            await host.Relay.SetAcceptingAsync(false);
        }

        using var raw = await host.OpenRawAsync();

        // The read waits before anything is sent, as a proxy's does: the server may reset the connection
        // right after its answer, and a reset discards what a client has not read yet.
        var reading = Wire.ReadRawAsync(raw);
        await raw.SendAsync(head + new string('x', declared / 2));
        var first = await reading;
        try
        {
            await raw.SendAsync(second);
        }
        catch (IOException)
        {
            // The server has closed already.
        }

        var afterwards = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(15));
        var closed = await raw.IsClosedByServerAsync(TimeSpan.FromSeconds(15));
        await host.Events.SettleAsync();

        // Exactly one response arrives, and it says that the connection ends here.
        Assert.NotNull(first);
        Assert.StartsWith($"HTTP/1.1 {status} ", Wire.StatusLine(first), StringComparison.Ordinal);
        Assert.Contains($"\"code\":\"{code}\"", Wire.BodyOf(first), StringComparison.Ordinal);
        Assert.Contains("Connection: close", Wire.HeaderLines(first));

        // The server closes, and the second request is never answered: nothing more arrives, and no
        // second authenticated request reached the edge.
        Assert.Null(afterwards);
        Assert.True(closed);
        Assert.Equal(authenticatedBefore + (status == 401 ? 0 : 1), host.Events.Count<PhoneSeen>());

        if (held.Count > 0)
        {
            host.Catalog.Gate!.SetResult();
            foreach (var response in await Task.WhenAll(held))
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task An_answer_after_the_whole_body_keeps_the_connection()
    {
        await using var host = await RelayTestHost.StartAsync();
        using var raw = await host.OpenRawAsync();

        // The body was read to its end, so nothing of it is left: the next request is served.
        await raw.SendAsync(host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "2")) + "[]");
        var invalidJson = await Wire.ReadRawAsync(raw);
        await raw.SendAsync(host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "2")) + "{}");
        var served = await Wire.ReadRawAsync(raw);
        await raw.SendAsync(host.RawHead("GET /v1/health HTTP/1.1"));
        var health = await Wire.ReadRawAsync(raw);

        Assert.Equal(Wire.InvalidJson, Wire.BodyOf(invalidJson!));
        Assert.DoesNotContain("Connection: close", Wire.HeaderLines(invalidJson!));
        Assert.Equal("""{"ok":true}""", Wire.BodyOf(served!));
        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine(health!));
    }

    [Fact]
    public async Task A_body_made_of_a_whole_second_request_is_a_body_and_nothing_else()
    {
        // The smuggling attempt itself: the "body" of a refused request is a complete, valid request.
        await using var host = await RelayTestHost.StartAsync();
        var hidden = host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "2")) + "{}";

        using var raw = await host.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw);
        await raw.SendAsync(
            Wire.Head(host.Port, "POST /v1/assist HTTP/1.1", ("Content-Length", hidden.Length.ToString(CultureInfo.InvariantCulture)))
            + hidden);
        var first = await reading;
        var afterwards = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(15));
        await host.Events.SettleAsync();

        Wire.AssertFixed401(Wire.WithoutDate(first!), closing: true);
        Assert.Null(afterwards);
        Assert.Equal(0, host.Events.Count<PhoneSeen>());
    }
}
