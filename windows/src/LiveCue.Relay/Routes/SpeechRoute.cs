using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Speech;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 10, <c>GET /v1/speech</c> with an upgrade (protocol 10.1): accepts the socket and hands it to
/// the bridge. The request pipeline keeps running until the socket is closed.
///
/// First version (foundation): no count of open sockets. The cap C-32 (HTTP 429 before the upgrade) is
/// work package WP-W05.
/// </summary>
internal sealed class SpeechRoute : IRouteHandler
{
    private const string ProtocolHeader = "X-LiveCue-Protocol";
    private const string ModelHeader = "X-LiveCue-Speech-Model";

    private readonly RelayContext _context;
    private readonly ISpeechBridge _bridge;

    public SpeechRoute(RelayContext context, ISpeechBridge bridge)
    {
        _context = context;
        _bridge = bridge;
    }

    public async Task HandleAsync(RouteContext context)
    {
        var headers = context.Http.Request.Headers;
        var protocol = Single(headers, ProtocolHeader);
        var model = Single(headers, ModelHeader);

        using var socket = await context.AcceptWebSocketAsync().ConfigureAwait(false);
        await _bridge.RunAsync(new SpeechConnection(
            socket,
            protocol,
            model,
            _context.Clock.GetTimestamp(),
            context.PairingRevoked)).ConfigureAwait(false);
    }

    /// <summary>The header's value, or null when it is absent or repeated.</summary>
    private static string? Single(IHeaderDictionary headers, string name)
    {
        var values = headers[name];
        return values.Count == 1 ? values[0] : null;
    }
}
