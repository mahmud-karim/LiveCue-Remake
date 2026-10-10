using System.Net.WebSockets;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Routes;
using LiveCue.Relay.Speech;
using LiveCue.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Route 10 by itself, with the edge and the bridge replaced: what it hands over, how it counts (C-32),
/// and what it does when the server core refuses an upgrade or a socket did not close.
/// </summary>
public sealed class SpeechRouteTests
{
    [Fact]
    public async Task The_route_hands_over_the_two_headers_the_upgrade_time_and_the_revocation_token()
    {
        var scene = new Scene();
        scene.Clock.Advance(TimeSpan.FromSeconds(7));
        using var revoked = new CancellationTokenSource();
        var request = scene.Request(revoked.Token, ("X-LiveCue-Protocol", ["2"]), ("X-LiveCue-Speech-Model", ["qwen3"]));

        await scene.Route.HandleAsync(request);

        var connection = Assert.Single(scene.Bridge.Connections);
        Assert.Equal("2", connection.ProtocolHeader);
        Assert.Equal("qwen3", connection.ModelHeader);
        Assert.Equal(scene.Clock.GetTimestamp(), connection.UpgradeTimestamp);
        Assert.Equal(revoked.Token, connection.PairingRevoked);
        Assert.Same(scene.Io.Sockets[0], connection.Socket);
        Assert.Empty(scene.Io.Written);
        Assert.Equal(0, scene.Route.OpenSockets);
    }

    [Fact]
    public async Task A_header_that_is_absent_or_repeated_is_handed_over_as_absent_and_the_upgrade_is_still_accepted()
    {
        var scene = new Scene();
        var request = scene.Request(default, ("X-LiveCue-Protocol", ["2", "2"]));

        await scene.Route.HandleAsync(request);

        var connection = Assert.Single(scene.Bridge.Connections);
        Assert.Null(connection.ProtocolHeader);
        Assert.Null(connection.ModelHeader);
        Assert.Single(scene.Io.Sockets);
    }

    [Fact]
    public async Task A_socket_counts_until_the_bridge_is_done_with_it_and_one_more_is_429()
    {
        var scene = new Scene();
        scene.Bridge.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Enumerable.Range(0, ProtocolConstants.MaxSpeechSockets)
            .Select(_ => scene.Route.HandleAsync(scene.Request()))
            .ToList();
        Assert.Equal(ProtocolConstants.MaxSpeechSockets, scene.Route.OpenSockets);

        // One more: the plain 429, before any upgrade, and nothing reaches the bridge.
        await scene.Route.HandleAsync(scene.Request());
        Assert.Equal([(429, """{"error":"Too many requests in progress.","code":"TOO_MANY_REQUESTS"}""")], scene.Io.Written);
        Assert.Equal(ProtocolConstants.MaxSpeechSockets, scene.Io.Sockets.Count);
        Assert.Equal(ProtocolConstants.MaxSpeechSockets, scene.Bridge.Connections.Count);
        Assert.Equal(ProtocolConstants.MaxSpeechSockets, scene.Route.OpenSockets);

        // When the bridge is done, every place is free again.
        scene.Bridge.Hold.SetResult();
        await Task.WhenAll(running);
        Assert.Equal(0, scene.Route.OpenSockets);
        await scene.Route.HandleAsync(scene.Request());
        Assert.Equal(ProtocolConstants.MaxSpeechSockets + 1, scene.Bridge.Connections.Count);
    }

    [Fact]
    public async Task An_upgrade_that_the_server_core_refuses_is_429_and_takes_no_place()
    {
        var scene = new Scene();
        scene.Io.RefusesUpgrade = true;

        await scene.Route.HandleAsync(scene.Request());

        Assert.Equal(429, Assert.Single(scene.Io.Written).Status);
        Assert.Empty(scene.Bridge.Connections);
        Assert.Equal(0, scene.Route.OpenSockets);
    }

    [Theory]
    [InlineData(WebSocketState.Closed, false)]
    [InlineData(WebSocketState.Aborted, true)]
    [InlineData(WebSocketState.CloseSent, true)]
    [InlineData(WebSocketState.Open, true)]
    public async Task A_socket_that_did_not_complete_its_close_takes_its_connection_with_it(WebSocketState left, bool aborted)
    {
        var scene = new Scene();
        scene.Bridge.LeavesSocketIn = left;
        var request = scene.Request();

        await scene.Route.HandleAsync(request);

        Assert.Equal(aborted, ((AbortRecorder)request.Http.Features.Get<IHttpRequestLifetimeFeature>()!).Aborted);
        Assert.True(scene.Io.Sockets[0].Disposed);
        Assert.Equal(0, scene.Route.OpenSockets);
    }

    /// <summary>The route with everything around it replaced.</summary>
    private sealed class Scene
    {
        public Scene()
        {
            Route = new SpeechRoute(SpeechContexts.Create(47842, Clock), Bridge);
        }

        public ManualTimeProvider Clock { get; } = new();

        public RecordingIo Io { get; } = new();

        public RecordingBridge Bridge { get; } = new();

        public SpeechRoute Route { get; }

        public RouteContext Request(CancellationToken revoked = default, params (string Name, string[] Values)[] headers)
        {
            var http = new DefaultHttpContext();
            http.Features.Set<IHttpRequestLifetimeFeature>(new AbortRecorder());
            foreach (var (name, values) in headers)
            {
                http.Request.Headers[name] = values;
            }

            return new RouteContext
            {
                Http = http,
                Route = RouteTable.Row(RouteId.Speech),
                ArrivalTimestamp = Clock.GetTimestamp(),
                Work = CancellationToken.None,
                ClientAborted = CancellationToken.None,
                PairingRevoked = revoked,
                RelayStopping = CancellationToken.None,
                Io = Io,
            };
        }
    }

    /// <summary>Notes whether the connection of a request was aborted.</summary>
    private sealed class AbortRecorder : IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }

        public CancellationToken RequestAborted { get; set; }

        public void Abort() => Aborted = true;
    }

    private sealed class RecordingIo : IRouteIo
    {
        public bool RefusesUpgrade { get; set; }

        public List<(int Status, string Body)> Written { get; } = [];

        public List<StateSocket> Sockets { get; } = [];

        public Task<BodyResult> ReadJsonBodyAsync(RouteContext context) => throw new NotSupportedException();

        public Task WriteJsonAsync(RouteContext context, int status, ReadOnlyMemory<byte> utf8Json)
        {
            Written.Add((status, System.Text.Encoding.UTF8.GetString(utf8Json.Span)));
            return Task.CompletedTask;
        }

        public Task<WebSocket> AcceptWebSocketAsync(RouteContext context)
        {
            if (RefusesUpgrade)
            {
                throw new InvalidOperationException("The server core has no place for another upgraded connection.");
            }

            var socket = new StateSocket();
            Sockets.Add(socket);
            return Task.FromResult<WebSocket>(socket);
        }
    }

    private sealed class RecordingBridge : ISpeechBridge
    {
        public List<SpeechConnection> Connections { get; } = [];

        public TaskCompletionSource? Hold { get; set; }

        public WebSocketState LeavesSocketIn { get; set; } = WebSocketState.Closed;

        public async Task RunAsync(SpeechConnection connection)
        {
            Connections.Add(connection);
            if (Hold is { } hold)
            {
                await hold.Task;
            }

            ((StateSocket)connection.Socket).Leave(LeavesSocketIn);
        }
    }

    /// <summary>A socket that is in whatever state the test says; it carries nothing.</summary>
    private sealed class StateSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public bool Disposed { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public void Leave(WebSocketState state) => _state = state;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override void Dispose() => Disposed = true;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
