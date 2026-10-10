using System.Net.WebSockets;
using System.Text;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// The I/O of one request without a listener: the body the "phone" sent, and what the handler wrote.
/// For the few tests that must act at one exact moment of a request, which no socket can time.
/// </summary>
internal sealed class RecordingRouteIo(string requestBody) : IRouteIo
{
    private readonly List<(int Status, string Body)> _responses = [];

    /// <summary>Called when the handler writes its response, before the response is recorded.</summary>
    public Action? OnWrite { get; set; }

    /// <summary>Every response written, in order. A handler writes at most one.</summary>
    public IReadOnlyList<(int Status, string Body)> Responses => _responses;

    public Task<BodyResult> ReadJsonBodyAsync(RouteContext context)
    {
        var utf8 = Encoding.UTF8.GetBytes(requestBody);
        return Task.FromResult(WireJson.TryParseObject(utf8, out var root)
            ? new BodyResult(null, utf8, root, ReadMs: 0)
            : new BodyResult(RelayErrors.InvalidJson, default, default, ReadMs: 0));
    }

    public Task WriteJsonAsync(RouteContext context, int status, ReadOnlyMemory<byte> utf8Json)
    {
        OnWrite?.Invoke();
        _responses.Add((status, Encoding.UTF8.GetString(utf8Json.Span)));
        return Task.CompletedTask;
    }

    public Task<WebSocket> AcceptWebSocketAsync(RouteContext context) =>
        throw new NotSupportedException("An answer route never upgrades.");
}

/// <summary>One request as the edge would hand it to a route, with tokens the test can fire.</summary>
internal sealed class RouteRequest : IDisposable
{
    private readonly CancellationTokenSource _work;

    public RouteRequest(AreaHarness harness, RouteId route, string body)
    {
        Io = new RecordingRouteIo(body);
        _work = CancellationTokenSource.CreateLinkedTokenSource(
            ClientAborted.Token, PairingRevoked.Token, harness.Access.Stopping);
        Context = new RouteContext
        {
            Http = new DefaultHttpContext(),
            Route = RouteTable.Row(route),
            ArrivalTimestamp = harness.Context.Clock.GetTimestamp(),
            Work = _work.Token,
            ClientAborted = ClientAborted.Token,
            PairingRevoked = PairingRevoked.Token,
            RelayStopping = harness.Access.Stopping,
            Io = Io,
        };
    }

    public RecordingRouteIo Io { get; }

    public RouteContext Context { get; }

    public CancellationTokenSource ClientAborted { get; } = new();

    public CancellationTokenSource PairingRevoked { get; } = new();

    public void Dispose()
    {
        _work.Dispose();
        ClientAborted.Dispose();
        PairingRevoked.Dispose();
    }
}
