using System.Diagnostics;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

// The notes of W-design 4.3 that can only be seen by waiting for a timer of the server core, which
// runs on real time whatever the relay's clock is. Each class holds one test, so that they wait side by
// side. All are in the category "Slow".

/// <summary>W-design K-6 with C-40: request headers that never complete.</summary>
public sealed class KestrelHeaderTimeoutTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public async Task K6_headers_that_never_complete_are_a_bare_408_and_the_connection_ends()
    {
        await using var host = await RelayTestHost.StartAsync();
        using var raw = await host.OpenRawAsync();
        var watch = Stopwatch.StartNew();

        // The request line, two headers with the accepted token, and never the blank line.
        await raw.SendAsync(host.RawHead("GET /v1/health HTTP/1.1")[..^2]);
        var response = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(30));
        var waited = watch.Elapsed;
        var closed = await raw.IsClosedByServerAsync(TimeSpan.FromSeconds(10));
        await host.Events.SettleAsync();

        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 408 Request Timeout", Wire.StatusLine(response));
        Assert.Equal(string.Empty, Wire.BodyOf(response));
        Assert.Contains("Connection: close", Wire.HeaderLines(response));
        Assert.True(closed);

        // Not before C-40, and soon after it (the server core looks once a second).
        Assert.InRange(waited, ProtocolConstants.HeadersTimeout - TimeSpan.FromSeconds(1), ProtocolConstants.HeadersTimeout + TimeSpan.FromSeconds(8));
        Assert.IsType<RelayStarted>(Assert.Single(host.Events.Snapshot()));
    }
}

/// <summary>W-design K-5: a body below the minimum data rate of protocol 5.1.</summary>
public sealed class KestrelBodyRateTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public async Task K5_a_body_below_the_minimum_rate_is_the_coded_408()
    {
        // The relay's clock does not move here, so its own body limit (C-41) cannot be what answers: the
        // server core gives up on the body after the grace period, and the relay turns that into its
        // coded error because no response had started.
        await using var host = await RelayTestHost.StartAsync();
        using var raw = await host.OpenRawAsync();
        var watch = Stopwatch.StartNew();

        await raw.SendAsync(host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "100000")) + "{");
        var response = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(30));
        var waited = watch.Elapsed;
        var closed = await raw.IsClosedByServerAsync(TimeSpan.FromSeconds(15));

        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 408 Request Timeout", Wire.StatusLine(response));
        Assert.Equal(Wire.RequestTimeout, Wire.BodyOf(response));
        Assert.Contains("Connection: close", Wire.HeaderLines(response));
        Assert.True(closed);
        Assert.InRange(waited, ProtocolConstants.MinBodyRateGrace - TimeSpan.FromSeconds(1), ProtocolConstants.MinBodyRateGrace + TimeSpan.FromSeconds(8));
        await Wire.UntilAsync(() => ((LiveCue.Relay.Core.RelayHost)host.Relay).Edge!.Gauge.InFlight == 0, "the request left the gauge");
    }
}

/// <summary>W-design K-4: what the server core does with a body nobody read.</summary>
public sealed class KestrelDrainTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public async Task K4_a_refused_body_that_never_arrives_holds_the_connection_for_seconds_only()
    {
        await using var host = await RelayTestHost.StartAsync();
        using var raw = await host.OpenRawAsync();
        var watch = Stopwatch.StartNew();

        // Refused at once, with ten bytes of a declared 10,000 sent. The second request is far shorter
        // than what is missing, so the server core, which discards the rest of a body before it closes,
        // takes it for body. It waits a few seconds for more, then closes. It never answers twice.
        await raw.SendAsync(Wire.Head(host.Port, "POST /v1/assist HTTP/1.1", ("Content-Length", "10000")) + "0123456789");
        var first = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(5));
        var answeredAfter = watch.Elapsed;
        await raw.SendAsync(host.RawHead("GET /v1/health HTTP/1.1"));
        var afterwards = await Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(30));
        var closedAfter = watch.Elapsed;
        await host.Events.SettleAsync();

        Wire.AssertFixed401(Wire.WithoutDate(first!), closing: true);
        Assert.True(answeredAfter < TimeSpan.FromSeconds(2));
        Assert.Null(afterwards);
        Assert.True(closedAfter < TimeSpan.FromSeconds(15));
        Assert.Equal(0, host.Events.Count<PhoneSeen>());
    }
}

/// <summary>W-design K-2 with C-57: the keep-alive frame of an open speech socket.</summary>
public sealed class KestrelKeepAliveFrameTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public async Task K2_the_keep_alive_frame_is_an_unsolicited_pong()
    {
        // A handler that accepts the socket and then only reads, so that nothing but the framework's own
        // keep-alive is ever written to it.
        await using var edge = await EdgeHarness.StartAsync(routes => routes[RouteId.Speech] = new ScriptedRoute(async context =>
        {
            using var socket = await context.AcceptWebSocketAsync();
            var buffer = new byte[256];
            while (!context.Work.IsCancellationRequested)
            {
                await socket.ReceiveAsync(buffer.AsMemory(), context.Work);
            }
        }));

        using var raw = await edge.OpenRawAsync();
        await raw.SendAsync(edge.RawHead("GET /v1/speech HTTP/1.1", Wire.UpgradeHeaders));
        var upgraded = await Wire.ReadRawAsync(raw, hasBody: false);
        var watch = Stopwatch.StartNew();

        // The first bytes the server writes after the upgrade.
        var frame = new byte[2];
        using var limit = new CancellationTokenSource(ProtocolConstants.KeepAliveFrameInterval + TimeSpan.FromSeconds(15));
        await raw.Stream.ReadExactlyAsync(frame, limit.Token);
        var waited = watch.Elapsed;

        Assert.Equal("HTTP/1.1 101 Switching Protocols", Wire.StatusLine(upgraded!));

        // FIN and opcode 0xA (Pong), no mask, no payload: not a Ping, so nothing is required of the phone.
        Assert.Equal([0x8A, 0x00], frame);
        Assert.InRange(waited, ProtocolConstants.KeepAliveFrameInterval - TimeSpan.FromSeconds(2), ProtocolConstants.KeepAliveFrameInterval + TimeSpan.FromSeconds(10));
    }
}
