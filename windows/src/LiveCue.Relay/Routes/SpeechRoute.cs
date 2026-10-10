using System.Net.WebSockets;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Speech;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 10, <c>GET /v1/speech</c> with an upgrade (protocol 10.1). It applies the cap C-32, accepts the
/// socket and hands it to the bridge; the request pipeline keeps running until the socket is closed.
///
/// C-32 counts every open speech socket, admitted or being refused: a socket counts from before the
/// upgrade until the bridge is done with it. One more is answered <c>429 TOO_MANY_REQUESTS</c> as plain
/// HTTP, before any upgrade. Past that step the upgrade is always accepted, whatever the two headers
/// say and whatever extension or subprotocol was offered, and every later refusal is a typed frame.
/// </summary>
internal sealed class SpeechRoute : IRouteHandler
{
    private const string ProtocolHeader = "X-LiveCue-Protocol";
    private const string ModelHeader = "X-LiveCue-Speech-Model";

    private readonly RelayContext _context;
    private readonly ISpeechBridge _bridge;
    private int _open;

    public SpeechRoute(RelayContext context, ISpeechBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bridge);
        _context = context;
        _bridge = bridge;
    }

    /// <summary>The speech sockets that are open now (C-32).</summary>
    internal int OpenSockets => Volatile.Read(ref _open);

    public async Task HandleAsync(RouteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Interlocked.Increment(ref _open) > ProtocolConstants.MaxSpeechSockets)
        {
            Interlocked.Decrement(ref _open);
            await context.WriteErrorAsync(RelayErrors.TooManyRequests).ConfigureAwait(false);
            return;
        }

        try
        {
            var headers = context.Http.Request.Headers;
            var protocol = Single(headers, ProtocolHeader);
            var model = Single(headers, ModelHeader);

            WebSocket socket;
            try
            {
                socket = await context.AcceptWebSocketAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException) when (!context.Http.Response.HasStarted)
            {
                // The server core keeps its own count of upgraded connections, with the same cap, and gives
                // a place back a moment after this route does. In that moment it refuses the upgrade.
                await context.WriteErrorAsync(RelayErrors.TooManyRequests).ConfigureAwait(false);
                return;
            }

            using (socket)
            {
                await _bridge.RunAsync(new SpeechConnection(
                    socket,
                    protocol,
                    model,
                    _context.Clock.GetTimestamp(),
                    context.PairingRevoked)).ConfigureAwait(false);

                if (socket.State != WebSocketState.Closed)
                {
                    // No completed close handshake: the stream aborted the socket. The connection is aborted
                    // here as well, so that dropping it never depends on how the server core ends an
                    // upgraded request whose output a phone that does not read has left unsent.
                    context.Http.Abort();
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }

    /// <summary>The header's value, or null when it is absent or repeated.</summary>
    private static string? Single(IHeaderDictionary headers, string name)
    {
        var values = headers[name];
        return values.Count == 1 ? values[0] : null;
    }
}
