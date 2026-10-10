using System.Globalization;
using System.Net.WebSockets;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace LiveCue.Relay.Edge;

/// <summary>
/// The checks every request passes, in the order protocol 5.2 fixes (W-design 4.4, steps P-1 to P-12).
/// The first failing step answers.
///
/// The relay is its own internet edge, and this class is where that is true:
/// <list type="bullet">
/// <item>The three 401 answers of E-1 to E-3 come from one pre-built byte array through one code path
/// (<see cref="RefuseUnauthorizedAsync"/>), so a caller without the token cannot tell which step refused
/// it and learns nothing about routes.</item>
/// <item>No work happens before E-3 passes: no body is read, no file is touched, no process is started
/// and no event is raised. The only trace of a refused request is one of three counters.</item>
/// <item>Every answer written while the request's body is still unread carries <c>Connection: close</c>
/// (W-design K-4), because the proxy in front may reuse one loopback connection for different callers.</item>
/// <item>Nothing a request does escapes to the server core, which would answer with a bare 500 of its
/// own.</item>
/// </list>
/// </summary>
internal sealed class EdgePipeline : IRouteIo
{
    /// <summary>The <see cref="Diagnostic.Source"/> of every diagnostic this class publishes.</summary>
    public const string Source = "edge";

    private const string ProtocolHeader = "X-LiveCue-Protocol";
    private const string SelfCheckHeader = "X-LiveCue-Self-Check";
    private const string TransferEncodingHeader = "Transfer-Encoding";

    private static readonly string ExpectedProtocol =
        ProtocolConstants.ProtocolVersion.ToString(CultureInfo.InvariantCulture);

    /// <summary>The one body of every 401, whatever refused the request.</summary>
    private static readonly ReadOnlyMemory<byte> UnauthorizedBody = RelayErrors.Body(RelayErrors.Unauthorized);

    private readonly RelayContext _context;
    private readonly RelayServices _services;
    private readonly ConnectionMode _mode;
    private readonly Lock _statisticsGate = new();

    // The rejected-request counters of protocol 5.5. Each is changed only with Interlocked.Increment, on
    // the 401 path and nowhere else.
    private long _malformed;
    private long _tokenNotAccepted;
    private long _funnelInPrivateMode;

    // The totals of the last EdgeStatistics that was taken. Guarded by _statisticsGate.
    private long _takenMalformed;
    private long _takenTokenNotAccepted;
    private long _takenFunnelInPrivateMode;

    // Requests inside InvokeAsync, speech sockets included, and who waits for them to end.
    private int _active;
    private TaskCompletionSource? _idle;

    public EdgePipeline(RelayContext context, RelayServices services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(services);
        _context = context;
        _services = services;
        _mode = context.Options.Mode;
    }

    /// <summary>Step P-0: every open connection, upgraded or not (C-30).</summary>
    public ConnectionGate Connections { get; } = new(ProtocolConstants.MaxConnections);

    /// <summary>Step P-10: the authenticated HTTP requests in flight (C-31).</summary>
    public RequestGauge Gauge { get; } = new(ProtocolConstants.MaxRequestsInFlight);

    /// <summary>Requests that are inside the pipeline now: HTTP requests and open speech sockets.</summary>
    public int Active => Volatile.Read(ref _active);

    /// <summary>Refused at E-1 for Origin or Sec-Fetch-Site, or at E-2. A total since the relay started.</summary>
    public long Malformed => Interlocked.Read(ref _malformed);

    /// <summary>Refused at E-3: a well-formed token that is not the accepted one.</summary>
    public long TokenNotAccepted => Interlocked.Read(ref _tokenNotAccepted);

    /// <summary>Refused at E-1 in Private mode for Tailscale-Funnel-Request.</summary>
    public long FunnelInPrivateMode => Interlocked.Read(ref _funnelInPrivateMode);

    /// <summary>The terminal delegate of the application: one request, from its first step to its last.</summary>
    public async Task InvokeAsync(HttpContext http)
    {
        Interlocked.Increment(ref _active);
        try
        {
            await ProcessAsync(http).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Nothing may escape to the server core: it would answer with a bare 500.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // An answer of the edge itself could not be written. That is the caller going away, or (never
            // seen, but then it must be visible) a defect; either way this connection ends here.
            if (exception is not (OperationCanceledException or IOException))
            {
                _context.Events.Diagnostic(Source, "A request could not be answered.", exception);
            }

            http.Abort();
        }
        finally
        {
            if (Interlocked.Decrement(ref _active) == 0)
            {
                Volatile.Read(ref _idle)?.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Completes when no request and no speech socket is inside the pipeline. Stop waits on it, bounded
    /// by C-58 (W-design 4.2). A request that arrives later does not undo a completed wait.
    /// </summary>
    public Task WhenIdleAsync()
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Exchange is a full fence: either the last request sees this waiter, or the count read here is
        // already zero.
        Interlocked.Exchange(ref _idle, idle);
        if (Volatile.Read(ref _active) == 0)
        {
            idle.TrySetResult();
        }

        return idle.Task;
    }

    /// <summary>
    /// The one statistics event of protocol 5.5: the three totals, when at least one changed since the
    /// event that was taken last; otherwise null. The relay's timer calls it once per W-13 and StopAsync
    /// calls it once more, so no event is ever raised per request.
    /// </summary>
    public EdgeStatistics? TakeStatistics(DateTimeOffset at)
    {
        lock (_statisticsGate)
        {
            var malformed = Malformed;
            var tokenNotAccepted = TokenNotAccepted;
            var funnelInPrivateMode = FunnelInPrivateMode;
            if (malformed == _takenMalformed
                && tokenNotAccepted == _takenTokenNotAccepted
                && funnelInPrivateMode == _takenFunnelInPrivateMode)
            {
                return null;
            }

            _takenMalformed = malformed;
            _takenTokenNotAccepted = tokenNotAccepted;
            _takenFunnelInPrivateMode = funnelInPrivateMode;
            return new EdgeStatistics(at, malformed, tokenNotAccepted, funnelInPrivateMode);
        }
    }

    public Task<BodyResult> ReadJsonBodyAsync(RouteContext context) => BodyReader.ReadAsync(context, _context.Clock);

    public Task WriteJsonAsync(RouteContext context, int status, ReadOnlyMemory<byte> utf8Json) =>
        ResponseWriter.WriteJsonAsync(context.Http, status, utf8Json, MustCloseConnection(context.Http));

    public Task<WebSocket> AcceptWebSocketAsync(RouteContext context) =>
        context.Http.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
        {
            SubProtocol = null,
            DangerousEnableCompression = false,
        });

    private async Task ProcessAsync(HttpContext http)
    {
        // P-1. The deadlines of the request count from here.
        var arrival = _context.Clock.GetTimestamp();
        var request = http.Request;
        var headers = request.Headers;
        var declaresBody = request.ContentLength is > 0 || headers.ContainsKey(TransferEncodingHeader);

        // P-2 (E-1), P-3 (E-2), P-4 (E-3).
        if (!TryAuthenticate(headers, out var revoked))
        {
            await RefuseUnauthorizedAsync(http, declaresBody).ConfigureAwait(false);
            return;
        }

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

        var state = new EdgeRequest(declaresBody);
        http.Features.Set(state);
        var stopping = _context.Access.Stopping;

        // P-7. The speech route has no deadline, checks its protocol header after the upgrade and counts
        // its own sockets (E-7, C-32) before it.
        if (route.Id == RouteId.Speech)
        {
            using var open = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, stopping, revoked);
            await RunAsync(NewContext(http, route, requestId, arrival, open.Token, revoked), state).ConfigureAwait(false);
            return;
        }

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

        // P-10 (E-7).
        if (!Gauge.TryEnter())
        {
            await RefuseAsync(http, RelayErrors.TooManyRequests, declaresBody).ConfigureAwait(false);
            return;
        }

        try
        {
            // P-11 (E-8). The route deadline counts from P-1.
            var left = route.Deadline - _context.Clock.GetElapsedTime(arrival);
            using var deadline = new CancellationTokenSource(left > TimeSpan.Zero ? left : TimeSpan.Zero, _context.Clock);
            using var work = CancellationTokenSource.CreateLinkedTokenSource(
                http.RequestAborted, stopping, revoked, deadline.Token);
            await RunAsync(NewContext(http, route, requestId, arrival, work.Token, revoked), state).ConfigureAwait(false);
        }
        finally
        {
            // P-12.
            Gauge.Leave();
        }
    }

    /// <summary>
    /// Steps P-2 to P-4 and their counters. A refused request is counted once: as "through Funnel in
    /// Private mode" when that applies, otherwise by the step that refused it. Nothing else happens here:
    /// no allocation, no event, no clock read.
    /// </summary>
    private bool TryAuthenticate(IHeaderDictionary headers, out CancellationToken revoked)
    {
        revoked = CancellationToken.None;

        // P-2 (E-1).
        switch (BearerCheck.FindRefusedHeader(headers, _mode))
        {
            case RefusedHeader.FunnelInPrivateMode:
                Interlocked.Increment(ref _funnelInPrivateMode);
                return false;

            case RefusedHeader.Browser:
                Interlocked.Increment(ref _malformed);
                return false;
        }

        // P-3 (E-2).
        if (!BearerCheck.TryReadToken(headers.Authorization, out var token))
        {
            Interlocked.Increment(ref _malformed);
            return false;
        }

        // P-4 (E-3). One call gives the result and the revocation token of exactly the pairing that was
        // verified; nothing reads a "current revocation" apart from it.
        if (!_services.Pairing.TryVerify(token, out revoked))
        {
            Interlocked.Increment(ref _tokenNotAccepted);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The 401 of E-1, E-2 and E-3: one byte array, one code path. Nothing is recorded for a request that
    /// was not authenticated, not even when its caller has gone before the answer.
    /// </summary>
    private static async Task RefuseUnauthorizedAsync(HttpContext http, bool declaresBody)
    {
        try
        {
            await ResponseWriter.WriteJsonAsync(
                http, RelayErrors.Unauthorized.Status, UnauthorizedBody, closeConnection: declaresBody).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever went wrong, an unauthenticated caller gets no second answer and leaves no trace.
        catch (Exception)
#pragma warning restore CA1031
        {
            http.Abort();
        }
    }

    /// <summary>An answer of the edge itself to an authenticated request, before any body was read.</summary>
    private static Task RefuseAsync(HttpContext http, RelayError error, bool declaresBody) =>
        ResponseWriter.WriteJsonAsync(http, error.Status, RelayErrors.Body(error), closeConnection: declaresBody);

    private RouteContext NewContext(
        HttpContext http,
        RouteEntry route,
        string? requestId,
        long arrival,
        CancellationToken work,
        CancellationToken revoked) => new()
        {
            Http = http,
            Route = route,
            RequestId = requestId,
            ArrivalTimestamp = arrival,
            Work = work,
            ClientAborted = http.RequestAborted,
            PairingRevoked = revoked,
            RelayStopping = _context.Access.Stopping,
            Io = this,
        };

    /// <summary>Awaits the handler and answers for it when it did not answer itself (W-design 4.4).</summary>
    private async Task RunAsync(RouteContext context, EdgeRequest state)
    {
        var http = context.Http;
        try
        {
            await _services.Routes[context.Route.Id].HandleAsync(context).ConfigureAwait(false);
            if (!http.Response.HasStarted)
            {
                // A handler that returns without an answer would leave the server core to send an empty 200.
                _context.Events.Diagnostic(Source, "A route ended without an answer.");
                await AnswerAsync(http, state, RelayErrors.InternalError).ConfigureAwait(false);
            }
        }
        catch (RelayException exception)
        {
            await AnswerAsync(http, state, exception.Error).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            switch (context.Cancellation)
            {
                case RouteCancellation.ClientAborted:
                    // The connection is gone: there is nobody to answer.
                    break;

                case RouteCancellation.RelayStopping:
                    // In-flight requests are not answered at a stop; the phone sees a transport failure.
                    http.Abort();
                    break;

                case RouteCancellation.PairingRevoked:
                    await AnswerAsync(http, state, RelayErrors.Unauthorized).ConfigureAwait(false);
                    break;

                case RouteCancellation.DeadlineExceeded:
                    await AnswerAsync(
                        http,
                        state,
                        context.Route.Id is RouteId.Assist or RouteId.SessionSummary
                            ? RelayErrors.AssistantTimeout
                            : RelayErrors.DeadlineExceeded).ConfigureAwait(false);
                    break;

                default:
                    // A cancellation that is not one of the request's own. When the connection ended under
                    // the request there is still nobody to answer; anything else is an unexpected failure.
                    if (!state.ConnectionLost && exception is not ConnectionAbortedException)
                    {
                        await FailAsync(http, state, exception).ConfigureAwait(false);
                    }

                    break;
            }
        }
        catch (ConnectionResetException)
        {
            // The caller's connection ended under a read or a write of this request.
        }
#pragma warning disable CA1031 // The last-resort handler: the caller gets the fixed 500 and nothing of the exception.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            await FailAsync(http, state, exception).ConfigureAwait(false);
        }
    }

    /// <summary>The last resort: a diagnostic with the exception's type name only, and the fixed 500.</summary>
    private async Task FailAsync(HttpContext http, EdgeRequest state, Exception exception)
    {
        _context.Events.Diagnostic(Source, "A request failed unexpectedly.", exception);
        await AnswerAsync(http, state, RelayErrors.InternalError).ConfigureAwait(false);
    }

    /// <summary>
    /// The answer for a handler that ended without writing one. Nothing is written twice: when a response
    /// has started (or the socket was upgraded) the connection is aborted instead.
    /// </summary>
    private static async Task AnswerAsync(HttpContext http, EdgeRequest state, RelayError error)
    {
        if (http.Response.HasStarted)
        {
            http.Abort();
            return;
        }

        try
        {
            await ResponseWriter.WriteJsonAsync(
                http, error.Status, RelayErrors.Body(error), state.MustCloseConnection).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // The caller went away while the answer was written.
        }
    }

    private static bool MustCloseConnection(HttpContext http) =>
        http.Features.Get<EdgeRequest>() is { MustCloseConnection: true };
}
