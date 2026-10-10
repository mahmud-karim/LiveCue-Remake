using System.IO.Pipelines;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Step P-0 (W-design 4.3, protocol C-30): every open connection is counted, upgraded or not, and one
/// beyond the cap is closed by the server before a byte of it is parsed.
/// </summary>
public sealed class ConnectionGateTests
{
    private const int Cap = ProtocolConstants.MaxConnections;

    [Fact]
    public async Task One_connection_beyond_the_cap_is_aborted_and_an_ended_one_frees_a_place()
    {
        var gate = new ConnectionGate(2);
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var calls = 0;
        var pending = new Queue<TaskCompletionSource>([first, second]);
        var wrapped = gate.Wrap(_ =>
        {
            calls++;
            return pending.Count > 0 ? pending.Dequeue().Task : Task.CompletedTask;
        });

        var one = new RecordingConnection();
        var two = new RecordingConnection();
        var three = new RecordingConnection();
        var four = new RecordingConnection();
        var runOne = wrapped(one);
        var runTwo = wrapped(two);

        // The third is refused: aborted, and never handed to the server core.
        await wrapped(three);
        Assert.True(three.Aborted);
        Assert.Equal(2, calls);
        Assert.Equal(2, gate.Open);
        Assert.False(one.Aborted);
        Assert.False(two.Aborted);

        // One ends, by failing: its place is free all the same.
        first.SetException(new IOException("gone"));
        await Assert.ThrowsAsync<IOException>(() => runOne);
        Assert.Equal(1, gate.Open);
        await wrapped(four);
        Assert.False(four.Aborted);
        Assert.Equal(3, calls);

        second.SetResult();
        await runTwo;
        Assert.Equal(0, gate.Open);
    }

    [Fact]
    public async Task The_cap_counts_plain_and_upgraded_connections_together()
    {
        // Two speech sockets are open (the handler accepts and keeps them). Kestrel's own two limits
        // count them apart from plain connections and would still admit 64 of those; the gate admits 62.
        await using var edge = await EdgeHarness.StartAsync(routes => routes[RouteId.Speech] = new ScriptedRoute(async context =>
        {
            using var socket = await context.AcceptWebSocketAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.Work);
        }));
        var gate = edge.Pipeline.Connections;
        var open = new List<RawHttp>();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var socket = await edge.OpenRawAsync();
                open.Add(socket);
                await socket.SendAsync(edge.RawHead("GET /v1/speech HTTP/1.1", Wire.UpgradeHeaders));
                var upgraded = await Wire.ReadRawAsync(socket, hasBody: false);
                Assert.Equal("HTTP/1.1 101 Switching Protocols", Wire.StatusLine(upgraded!));
            }

            // Connections that have not sent a byte yet count as well.
            while (open.Count < Cap)
            {
                open.Add(await edge.OpenRawAsync());
            }

            await Wire.UntilAsync(() => gate.Open == Cap, "sixty-four connections are open");

            // The sixty-fifth is closed at once, without an answer.
            using (var beyond = await edge.OpenRawAsync())
            {
                Assert.True(await beyond.IsClosedByServerAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(Cap, gate.Open);
            }

            // The existing ones keep working.
            await open[^1].SendAsync(edge.RawHead("GET /v1/health HTTP/1.1"));
            Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine((await Wire.ReadRawAsync(open[^1]))!));

            // After one has closed, the next connection is served.
            open[^2].Dispose();
            open.RemoveAt(open.Count - 2);
            await Wire.UntilAsync(() => gate.Open == Cap - 1, "the closed connection left the gate");
            using var next = await edge.OpenRawAsync();
            await next.SendAsync(edge.RawHead("GET /v1/health HTTP/1.1"));
            Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine((await Wire.ReadRawAsync(next))!));
        }
        finally
        {
            foreach (var connection in open)
            {
                connection.Dispose();
            }
        }

        await Wire.UntilAsync(() => gate.Open == 0, "every connection left the gate");
    }

    /// <summary>A connection that only remembers whether it was aborted.</summary>
    private sealed class RecordingConnection : ConnectionContext
    {
        public bool Aborted { get; private set; }

        public override string ConnectionId { get; set; } = "test";

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();

        public override IDuplexPipe Transport { get; set; } = null!;

        public override void Abort(ConnectionAbortedException abortReason) => Aborted = true;
    }
}
