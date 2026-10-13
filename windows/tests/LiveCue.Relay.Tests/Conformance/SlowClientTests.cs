using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Edge;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Conformance;

/// <summary>
/// Test plan 4.3, S-20 to S-24 and S-31: clients that are slow on purpose. Every test here waits for a
/// real timer of the server core, on the system clock, against the full relay; all are <c>Slow</c>.
/// What is proved is that each kind of slowness is bounded by a limit of protocol 2.4 and that nothing
/// stays held afterwards.
/// </summary>
[Trait("Category", "Slow")]
public sealed class SlowClientTests
{
    /// <summary>Room for a loaded machine around a limit of the server core, whose timer ticks once a second.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(6);

    [Theory]
    [InlineData("a head without its blank line")]
    [InlineData("a head sent one byte a second")]
    public async Task Slow_headers_are_cut_off(string what)
    {
        // S-20. The limit C-40 is on the whole head, however it is trickled.
        await using var relay = await ConformanceRelay.StartAsync();
        using var raw = await relay.OpenRawAsync();
        var head = relay.RawHead("GET /v1/health HTTP/1.1");
        var started = Stopwatch.StartNew();
        using var stop = new CancellationTokenSource();
        var trickle = Task.CompletedTask;

        switch (what)
        {
            case "a head without its blank line":
                await raw.SendAsync(head[..^2]);
                break;
            default:
                trickle = TrickleAsync(raw, head[..^2], TimeSpan.FromSeconds(1), stop.Token);
                break;
        }

        var answer = await Wire.ReadRawAsync(raw, timeout: ProtocolConstants.HeadersTimeout + Slack);
        var closed = await raw.IsClosedByServerAsync(Slack);
        var took = started.Elapsed;
        await stop.CancelAsync();
        await trickle;

        // A bare 408, or just the end of the connection; never before the limit, never long after it.
        Assert.True(answer is null || Wire.StatusLine(answer).StartsWith("HTTP/1.1 408 ", StringComparison.Ordinal), answer);
        Assert.True(answer is null || Wire.BodyOf(answer).Length == 0);
        Assert.True(closed, "The connection stayed open.");
        Assert.InRange(took, ProtocolConstants.HeadersTimeout - TimeSpan.FromSeconds(2), ProtocolConstants.HeadersTimeout + Slack);
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
        await Hostile.AssertUnharmedAsync(relay);
    }

    [Theory]
    [InlineData("/v1/assist")]
    [InlineData("/v1/local-model/start")]
    public async Task A_stalled_body_releases_everything(string target)
    {
        // S-21 in real time. The body stops after ten bytes. The answer is 408 by C-41 at the latest (the
        // server core's rate limit gives the same code a little earlier), and then nothing is held: no
        // place at the gauge, no answer slot.
        await using var relay = await ConformanceRelay.StartAsync();
        using var raw = await relay.OpenRawAsync();
        var started = Stopwatch.StartNew();
        await raw.SendAsync(relay.RawHead($"POST {target} HTTP/1.1", ("Content-Length", "200")) + "{\"model\":\"");
        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 1, "the request is in flight");

        if (target == "/v1/assist")
        {
            // The slot is taken before the body is read (protocol 8.2), so until the limit a second
            // request is refused as busy. That is as long as a stalled body can hold it.
            var (busy, text) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/session-summary", ConformanceRelay.AnswerBody("request-0002"));
            Assert.Equal(429, busy);
            Assert.Contains("ASSISTANT_BUSY", text, StringComparison.Ordinal);
        }

        var answer = await Wire.ReadRawAsync(raw, timeout: ProtocolConstants.BodyTimeout + Slack);
        var took = started.Elapsed;

        Assert.NotNull(answer);
        Assert.Equal("HTTP/1.1 408 Request Timeout", Wire.StatusLine(answer));
        Assert.Equal(Wire.RequestTimeout, Wire.BodyOf(answer));
        Assert.Contains("Connection: close", Wire.HeaderLines(answer));
        Assert.InRange(took, TimeSpan.FromSeconds(4), ProtocolConstants.BodyTimeout + Slack);
        Assert.True(await raw.IsClosedByServerAsync(Slack));

        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 0, "the gauge is empty");
        var (status, _) = await relay.ExchangeAsync(HttpMethod.Post, "/v1/assist", ConformanceRelay.AnswerBody("request-0003"));
        Assert.Equal(200, status);
        Assert.Empty(relay.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task A_trickled_body_is_408()
    {
        // S-22. Ten bytes a second is below the minimum rate of protocol 5.1: 408, coded or bare.
        await using var relay = await ConformanceRelay.StartAsync();
        using var raw = await relay.OpenRawAsync();
        using var stop = new CancellationTokenSource();
        var started = Stopwatch.StartNew();
        await raw.SendAsync(relay.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "100000")));
        var trickle = TrickleAsync(raw, "{\"pad\":\"" + new string('a', 5000), TimeSpan.FromMilliseconds(100), stop.Token);

        // Route 2 has a deadline of 5 s, shorter than C-41, so here the deadline answers first (504); a
        // route with a longer deadline gets the 408. Both are bounded, which is the point.
        var answer = await Wire.ReadRawAsync(raw, timeout: ProtocolConstants.BodyTimeout + Slack);
        var took = started.Elapsed;
        await stop.CancelAsync();
        await trickle;

        Assert.NotNull(answer);
        Assert.Matches("^HTTP/1.1 (408|504) ", Wire.StatusLine(answer));
        Assert.InRange(took, TimeSpan.FromSeconds(4), ProtocolConstants.BodyTimeout + Slack);

        using var longer = await relay.OpenRawAsync();
        using var stopLonger = new CancellationTokenSource();
        started.Restart();
        await longer.SendAsync(relay.RawHead("POST /v1/local-model/stop HTTP/1.1", ("Content-Length", "100000")));
        trickle = TrickleAsync(longer, "{\"pad\":\"" + new string('a', 5000), TimeSpan.FromMilliseconds(100), stopLonger.Token);
        answer = await Wire.ReadRawAsync(longer, timeout: ProtocolConstants.BodyTimeout + Slack);
        took = started.Elapsed;
        await stopLonger.CancelAsync();
        await trickle;

        Assert.NotNull(answer);
        Assert.StartsWith("HTTP/1.1 408 ", Wire.StatusLine(answer), StringComparison.Ordinal);
        Assert.True(Wire.BodyOf(answer).Length == 0 || Wire.BodyOf(answer) == Wire.RequestTimeout);
        Assert.InRange(took, TimeSpan.FromSeconds(4), ProtocolConstants.BodyTimeout + Slack);
        await Wire.UntilAsync(() => relay.Edge.Gauge.InFlight == 0, "the gauge is empty");
        await Hostile.AssertUnharmedAsync(relay);
    }

    [Fact]
    public async Task An_unread_response_does_not_hold_a_slot()
    {
        // S-23, the part that holds. Sixteen callers each ask for large answers on one connection and
        // never read a byte. The answer slot (C-33) is left before an answer is written, so while their
        // answers wait to be read the slot is free: a caller that does not read cannot keep Codex from the
        // next caller. And whatever such a caller holds, it holds only while it is connected: when the
        // callers go, every place at the gauge and every connection comes back.
        //
        // The part that does not hold is the test below this one.
        await using var deaf = await DeafCallers.StartAsync();

        Assert.Equal(0, deaf.AnswersInFlight);
        Assert.InRange(deaf.Host.Backend.Runs.Count, 3, 4 * ProtocolConstants.MaxRequestsInFlight);
        deaf.Hangup();
        await Wire.UntilAsync(() => deaf.Edge.Gauge.InFlight == 0, "the places came back", TimeSpan.FromSeconds(20));
        await Wire.UntilAsync(() => deaf.Edge.Connections.Open == 0, "the callers are gone", TimeSpan.FromSeconds(20));

        using var served = await deaf.Host.GetAsync("/v1/health");
        Assert.Equal(200, (int)served.StatusCode);
        using var answer = await deaf.Host.PostAsync("/v1/assist", Bodies.Assist("request-last"));
        Assert.Equal(200, (int)answer.StatusCode);
        await deaf.Host.Events.SettleAsync();
        Assert.Empty(deaf.Host.Events.OfType<Diagnostic>());
    }

    [Fact(Skip = OpenDefects.UnreadResponseHoldsTheGauge)]
    public async Task A_caller_that_does_not_read_gives_its_place_back_by_itself()
    {
        // S-23 as the test plan states it: "the connection is aborted by the server's response rate
        // limit; the request's slot is free". It is not, and this test fails when it is run.
        //
        // An answer that the caller does not read is a write that waits. No limit of the relay ends that
        // wait: the route deadline does not cover the write, and the server core's response rate limit
        // (240 bytes a second by default) allows a large answer more than an hour. Until then the request
        // keeps its place at the gauge (C-31). Sixteen such callers, each with the token, make every
        // other request 429 TOO_MANY_REQUESTS for as long as they stay connected.
        await using var deaf = await DeafCallers.StartAsync();

        // Expected: every place is free again within the time a body is allowed to take (C-41), while
        // the callers are still connected and still not reading.
        await Wire.UntilAsync(() => deaf.Edge.Gauge.InFlight == 0, "the places came back", ProtocolConstants.BodyTimeout + Slack);
        using var served = await deaf.Host.GetAsync("/v1/health");
        Assert.Equal(200, (int)served.StatusCode);
    }

    [Fact]
    public async Task A_caller_without_the_token_that_never_reads_holds_one_connection_and_nothing_else()
    {
        // The same behaviour from a caller that has no token: thousands of requests on one connection and
        // not one answer read. What holds: the relay stops reading that connection once its answers are
        // not taken (it never queues without bound), no place at the gauge is ever taken, nothing is
        // recorded but the count of refused requests, and when the caller goes the connection is free.
        //
        // What does not hold is the test below this one: how long the caller can stay.
        await using var relay = await ConformanceRelay.StartAsync();
        using var socket = await DeafWithoutTokenAsync(relay);

        Assert.Equal(1, relay.Edge.Connections.Open);
        Assert.Equal(0, relay.Edge.Gauge.InFlight);
        var answered = relay.Edge.Malformed;
        Assert.InRange(answered, 1, 20_000);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(answered, relay.Edge.Malformed);

        socket.Dispose();
        await Wire.UntilAsync(() => relay.Edge.Connections.Open == 0, "the connection is not counted");
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
        Assert.Empty(relay.Events.OfType<Diagnostic>());
        await Hostile.AssertUnharmedAsync(relay);
    }

    [Fact(Skip = OpenDefects.UnreadResponseHoldsTheGauge)]
    public async Task A_caller_without_the_token_that_never_reads_is_dropped_within_seconds()
    {
        // The second face of the same defect, and it needs no token. A connection whose answers are not
        // read should be dropped about as fast as one whose request does not arrive (C-40, C-42). It is
        // kept for minutes: the server core's response rate limit adds up over every answer that is
        // waiting, some 64 KB of them at 240 bytes a second. Sixty-four such connections are C-30.
        //
        // Only a caller that reaches the loopback port itself can do this. A proxy that reads every
        // response of its own requests, as the one the relay runs behind does, never lets a write wait.
        await using var relay = await ConformanceRelay.StartAsync();
        using var socket = await DeafWithoutTokenAsync(relay);

        await Wire.UntilAsync(() => relay.Edge.Connections.Open == 0, "the caller was dropped", ProtocolConstants.KeepAliveTimeout + Slack);
    }

    /// <summary>
    /// One connection that sends unauthenticated requests until the relay stops taking them, and reads
    /// nothing. Returns when a send has waited two seconds: the relay is no longer reading.
    /// </summary>
    private static async Task<Socket> DeafWithoutTokenAsync(ConformanceRelay relay)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ReceiveBufferSize = 1024 };
        try
        {
            await socket.ConnectAsync(System.Net.IPAddress.Loopback, relay.Port);
            var block = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(Wire.Head(relay.Port, "GET /v1/health HTTP/1.1"), 200)));
            for (var sent = 0; sent < 1000; sent++)
            {
                Task sending = socket.SendAsync(block);
                if (await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(2))) != sending)
                {
                    return socket;
                }

                await sending;
            }

            throw new Xunit.Sdk.XunitException("The relay kept reading requests whose answers nobody took.");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sixteen connections, each of which sends four answer requests one after the other and reads
    /// nothing. The answers are large. The system may take the first answer of a connection into its
    /// send buffer whole; it does not take the next one, so on every connection a write ends up waiting.
    /// </summary>
    private sealed class DeafCallers : IAsyncDisposable
    {
        private readonly List<Socket> _sockets = [];

        private DeafCallers(RelayTestHost host)
        {
            Host = host;
        }

        public RelayTestHost Host { get; }

        public LiveCue.Relay.Edge.EdgePipeline Edge => ((LiveCue.Relay.Core.RelayHost)Host.Relay).Edge!;

        /// <summary>How many requests hold the answer slot now: 0 or 1.</summary>
        public int AnswersInFlight
        {
            get
            {
                var services = (LiveCue.Relay.Core.RelayServices)typeof(LiveCue.Relay.Core.RelayHost)
                    .GetField("_services", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(Host.Relay)!;
                return services.Answers.InFlight;
            }
        }

        public static async Task<DeafCallers> StartAsync()
        {
            var host = await RelayTestHost.StartAsync(options => options.UseSystemClock = true);
            var callers = new DeafCallers(host);
            try
            {
                host.Backend.AssistResult = Bodies.ValidAssistOutput(new string('a', 900_000));
                for (var index = 0; index < ProtocolConstants.MaxRequestsInFlight; index++)
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ReceiveBufferSize = 1024 };
                    callers._sockets.Add(socket);
                    await socket.ConnectAsync(System.Net.IPAddress.Loopback, host.Port);
                    var caller = "request-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    await Task.Delay(100);
                    foreach (var part in (string[])["a", "b", "c", "d"])
                    {
                        var body = Bodies.Assist(caller + part);
                        await socket.SendAsync(Encoding.ASCII.GetBytes(
                            host.RawHead("POST /v1/assist HTTP/1.1", ("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))) + body));
                    }

                }

                // Every connection gets as far as it gets: until no further run is made for a second.
                var runs = -1;
                while (runs != host.Backend.Runs.Count)
                {
                    runs = host.Backend.Runs.Count;
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                return callers;
            }
            catch
            {
                await callers.DisposeAsync();
                throw;
            }
        }

        public void Hangup()
        {
            _sockets.ForEach(socket => socket.Dispose());
            _sockets.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            _sockets.ForEach(socket => socket.Dispose());
            await Host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Idle_connections_are_closed()
    {
        // S-24. A connection that was used, one that never sent a byte and one that sends nothing but
        // line breaks (which the server core skips without starting a request) are all closed after C-42.
        await using var relay = await ConformanceRelay.StartAsync();
        using var used = await relay.OpenRawAsync();
        using var silent = await relay.OpenRawAsync();
        using var breaks = await relay.OpenRawAsync();
        using var stop = new CancellationTokenSource();
        await used.SendAsync(relay.RawHead("GET /v1/health HTTP/1.1"));
        Assert.Equal("HTTP/1.1 200 OK", Wire.StatusLine((await Wire.ReadRawAsync(used))!));
        var started = Stopwatch.StartNew();
        var trickle = TrickleAsync(breaks, string.Concat(Enumerable.Repeat("\r\n", 100)), TimeSpan.FromSeconds(2), stop.Token);

        var closing = new[]
        {
            used.IsClosedByServerAsync(ProtocolConstants.KeepAliveTimeout + Slack),
            silent.IsClosedByServerAsync(ProtocolConstants.KeepAliveTimeout + Slack),
            breaks.IsClosedByServerAsync(ProtocolConstants.KeepAliveTimeout + Slack),
        };

        // Still open well before the limit.
        await Task.Delay(ProtocolConstants.KeepAliveTimeout - TimeSpan.FromSeconds(8));
        Assert.All(closing, task => Assert.False(task.IsCompleted));
        Assert.Equal(3, relay.Edge.Connections.Open);

        Assert.All(await Task.WhenAll(closing), Assert.True);
        Assert.InRange(started.Elapsed, ProtocolConstants.KeepAliveTimeout - TimeSpan.FromSeconds(3), ProtocolConstants.KeepAliveTimeout + Slack);
        await stop.CancelAsync();
        await trickle;
        await Wire.UntilAsync(() => relay.Edge.Connections.Open == 0, "no connection is counted");
        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>().Skip(1));
        await Hostile.AssertUnharmedAsync(relay);
    }

    [Fact]
    public async Task A_slow_connection_flood_does_not_starve_the_relay()
    {
        // S-31. Two hundred connections that each send one byte and wait. The cap C-30 holds: sixty-four
        // stay, the others are dropped at once. The sixty-four are cut off at C-40, and then a correct
        // phone is served again.
        //
        // What the test also records: while the sixty-four are held, a new connection is dropped. That
        // is C-30 as designed, and it is why the relay must only ever be reached through a proxy that
        // completes a request head before it opens a connection to it (see the coverage notes).
        await using var relay = await ConformanceRelay.StartAsync();
        var flood = new List<RawHttp>();
        var started = Stopwatch.StartNew();
        try
        {
            for (var index = 0; index < 200; index++)
            {
                var raw = await relay.OpenRawAsync();
                flood.Add(raw);
                try
                {
                    await raw.SendAsync("G");
                }
                catch (IOException)
                {
                    // Dropped before the byte was sent.
                }
            }

            await Wire.UntilAsync(() => relay.Edge.Connections.Open == ProtocolConstants.MaxConnections, "the cap is reached");
            await Task.Delay(500);
            Assert.Equal(ProtocolConstants.MaxConnections, relay.Edge.Connections.Open);

            // During the flood a correct phone gets no connection.
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => relay.ExchangeAsync(HttpMethod.Get, "/v1/health"));

            // The headers limit ends it, for all of them at once.
            await Wire.UntilAsync(
                () => relay.Edge.Connections.Open == 0, "the flood was cut off", ProtocolConstants.HeadersTimeout + Slack);
            Assert.InRange(started.Elapsed, ProtocolConstants.HeadersTimeout - TimeSpan.FromSeconds(2), ProtocolConstants.HeadersTimeout + Slack);
        }
        finally
        {
            flood.ForEach(raw => raw.Dispose());
        }

        await relay.Events.SettleAsync();
        Assert.Empty(relay.Events.OfType<PhoneSeen>());
        await Hostile.AssertUnharmedAsync(relay);
    }

    /// <summary>Sends the text one byte at a time with a pause between the bytes, until it is told to stop.</summary>
    private static async Task TrickleAsync(RawHttp raw, string text, TimeSpan pause, CancellationToken stop)
    {
        try
        {
            foreach (var character in text)
            {
                await raw.SendAsync(new[] { (byte)character });
                await Task.Delay(pause, stop);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The relay closed the connection, or the test is over.
        }
    }
}
