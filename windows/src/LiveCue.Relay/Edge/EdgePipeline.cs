using System.Globalization;
using System.Net.WebSockets;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace LiveCue.Relay.Edge;

/// <summary>
/// The checks every request passes, in the order protocol 5.2 fixes (W-design 4.4, steps P-1 to P-12).
/// The first failing step answers. The three 401 answers come from one pre-built byte array through one
/// code path, so a caller without the token cannot tell which step refused it. No work happens before
/// E-3 passes: no body is read, no file is touched, no event reaches the desktop.
///
/// First version (foundation): P-1 to P-9, P-11 and P-12 without the route deadline. The connection gate
/// (P-0), the request gauge (P-10, C-31) and the deadlines of protocol 5.5 are work package WP-W01.
/// </summary>
internal sealed class EdgePipeline : IRouteIo
{
    private const string ProtocolHeader = "X-LiveCue-Protocol";
    private const string SelfCheckHeader = "X-LiveCue-Self-Check";

    private static readonly string ExpectedProtocol =
        ProtocolConstants.ProtocolVersion.ToString(CultureInfo.InvariantCulture);

    private readonly RelayContext _context;
    private readonly RelayServices _services;

    public EdgePipeline(RelayContext context, RelayServices services)
    {
        _context = context;
        _services = services;
    }

    public async Task InvokeAsync(HttpContext http)
    {
        // P-1.
        var arrival = _context.Clock.GetTimestamp();
        var request = http.Request;
        var headers = request.Headers;
        var declaresBody = request.ContentLength is > 0 || headers.ContainsKey("Transfer-Encoding");

        // P-2 (E-1), P-3 (E-2), P-4 (E-3).
        if (BearerCheck.CarriesRefusedHeader(headers, _context.Options.Mode)
            || !BearerCheck.TryReadToken(headers.Authorization, out var token)
            || !_services.Pairing.Verify(token))
        {
            await RefuseAsync(http, RelayErrors.Unauthorized, declaresBody).ConfigureAwait(false);
            return;
        }

        var revoked = _services.Pairing.CurrentRevocation;

        // P-5. The self-check request carrying the current nonce is the one request that is not a phone.
        var selfCheck = headers[SelfCheckHeader];
        if (selfCheck.Count == 1 && _services.Pairing.IsSelfCheckNonce(selfCheck[0]))
        {
            _services.Pairing.NoteSelfCheckArrival(headers.ContainsKey(BearerCheck.FunnelHeader));
        }
        else
        {
            _context.Events.Publish(new PhoneSeen(_context.Events.Now));
        }

        // P-6 (E-4). The raw target is the text on the request line, before any decoding (K-7).
        var target = http.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        var route = RouteTable.Match(request.Method, target, out var requestId);
        if (route is null || (route.Id == RouteId.Speech && !http.WebSockets.IsWebSocketRequest))
        {
            await RefuseAsync(http, RelayErrors.NotFound, declaresBody).ConfigureAwait(false);
            return;
        }

        if (route.Id != RouteId.Speech)
        {
            // P-8 (E-5).
            var protocol = headers[ProtocolHeader];
            if (protocol.Count != 1 || !string.Equals(protocol[0], ExpectedProtocol, StringComparison.Ordinal))
            {
                await RefuseAsync(http, RelayErrors.ProtocolMismatch, declaresBody).ConfigureAwait(false);
                return;
            }

            // P-9 (E-6).
            if (!route.ReadsBody && declaresBody)
            {
                await RefuseAsync(http, RelayErrors.InvalidRequest, declaresBody).ConfigureAwait(false);
                return;
            }
        }

        // P-7 and P-11.
        using var work = CancellationTokenSource.CreateLinkedTokenSource(
            http.RequestAborted, _context.Access.Stopping, revoked);
        var routeContext = new RouteContext
        {
            Http = http,
            Route = route,
            RequestId = requestId,
            ArrivalTimestamp = arrival,
            Work = work.Token,
            ClientAborted = http.RequestAborted,
            PairingRevoked = revoked,
            RelayStopping = _context.Access.Stopping,
            Io = this,
        };

        try
        {
            await _services.Routes[route.Id].HandleAsync(routeContext).ConfigureAwait(false);
        }
        catch (RelayException exception)
        {
            await AnswerAsync(http, exception.Error).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            switch (routeContext.Cancellation)
            {
                case RouteCancellation.PairingRevoked:
                    await AnswerAsync(http, RelayErrors.Unauthorized).ConfigureAwait(false);
                    break;

                case RouteCancellation.DeadlineExceeded:
                    await AnswerAsync(
                        http,
                        route.Id is RouteId.Assist or RouteId.SessionSummary
                            ? RelayErrors.AssistantTimeout
                            : RelayErrors.DeadlineExceeded).ConfigureAwait(false);
                    break;

                case RouteCancellation.RelayStopping:
                    http.Abort();
                    break;

                default:
                    // The client went away: there is nobody to answer.
                    break;
            }
        }
#pragma warning disable CA1031 // The last-resort handler: nothing may escape to the server core.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _context.Events.Diagnostic("edge", "A request failed unexpectedly.", exception);
            if (http.Response.HasStarted)
            {
                http.Abort();
            }
            else
            {
                await AnswerAsync(http, RelayErrors.InternalError).ConfigureAwait(false);
            }
        }
    }

    public Task<BodyResult> ReadJsonBodyAsync(RouteContext context) => BodyReader.ReadAsync(context, _context.Clock);

    public Task WriteJsonAsync(RouteContext context, int status, ReadOnlyMemory<byte> utf8Json) =>
        ResponseWriter.WriteJsonAsync(context.Http, status, utf8Json);

    public Task<WebSocket> AcceptWebSocketAsync(RouteContext context) =>
        context.Http.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
        {
            SubProtocol = null,
            DangerousEnableCompression = false,
        });

    /// <summary>An answer of the edge itself, before any body was read.</summary>
    private static Task RefuseAsync(HttpContext http, RelayError error, bool declaresBody) =>
        ResponseWriter.WriteJsonAsync(http, error.Status, RelayErrors.Body(error), closeConnection: declaresBody);

    /// <summary>The answer for a handler that ended without writing one. Nothing is written twice.</summary>
    private static async Task AnswerAsync(HttpContext http, RelayError error)
    {
        if (http.Response.HasStarted)
        {
            http.Abort();
            return;
        }

        try
        {
            await ResponseWriter.WriteJsonAsync(http, error.Status, RelayErrors.Body(error)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The client went away while the answer was written.
        }
    }
}
