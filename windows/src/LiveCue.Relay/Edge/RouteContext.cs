using System.Net.WebSockets;
using System.Text.Json;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Edge;

/// <summary>Why a route's work token was cancelled. Evaluated in this order.</summary>
internal enum RouteCancellation
{
    None,
    ClientAborted,
    RelayStopping,
    PairingRevoked,
    DeadlineExceeded,
}

/// <summary>Result of protocol 5.4. Exactly one of Error and Root is set.</summary>
/// <param name="Utf8">The body bytes as received.</param>
/// <param name="Root">The parsed top-level object, detached from its document.</param>
/// <param name="ReadMs">Milliseconds from header arrival to the parsed body (requestReadMs).</param>
internal readonly record struct BodyResult(RelayError? Error, ReadOnlyMemory<byte> Utf8, JsonElement Root, double ReadMs);

/// <summary>The I/O a handler may do. Implemented by the edge; faked in handler unit tests.</summary>
internal interface IRouteIo
{
    Task<BodyResult> ReadJsonBodyAsync(RouteContext context);

    /// <summary>Writes one JSON document with the three fixed headers. A second write on the same request
    /// throws <see cref="InvalidOperationException"/>.</summary>
    Task WriteJsonAsync(RouteContext context, int status, ReadOnlyMemory<byte> utf8Json);

    /// <summary>Accepts the WebSocket upgrade with no subprotocol and no compression.</summary>
    Task<WebSocket> AcceptWebSocketAsync(RouteContext context);
}

/// <summary>
/// One authenticated request that passed steps E-1 to E-7. Handlers do the route's own checks (E-8),
/// then either write exactly one response, or throw <see cref="Core.RelayException"/>, or let an
/// <see cref="OperationCanceledException"/> of <see cref="Work"/> escape. The edge answers the last two.
/// </summary>
internal sealed class RouteContext
{
    public required HttpContext Http { get; init; }

    public required RouteEntry Route { get; init; }

    /// <summary>Route 9 only: the id from the target.</summary>
    public string? RequestId { get; init; }

    /// <summary>Clock timestamp taken when the pipeline first saw the request.</summary>
    public required long ArrivalTimestamp { get; init; }

    /// <summary>Cancelled at the route deadline, on client abort, on relay stop and on pairing reset.</summary>
    public required CancellationToken Work { get; init; }

    public required CancellationToken ClientAborted { get; init; }

    public required CancellationToken PairingRevoked { get; init; }

    public required CancellationToken RelayStopping { get; init; }

    public required IRouteIo Io { get; init; }

    /// <summary>The first cause that applies, in the order of <see cref="RouteCancellation"/>.</summary>
    public RouteCancellation Cancellation =>
        ClientAborted.IsCancellationRequested ? RouteCancellation.ClientAborted
        : RelayStopping.IsCancellationRequested ? RouteCancellation.RelayStopping
        : PairingRevoked.IsCancellationRequested ? RouteCancellation.PairingRevoked
        : Work.IsCancellationRequested ? RouteCancellation.DeadlineExceeded
        : RouteCancellation.None;

    public Task<BodyResult> ReadJsonBodyAsync() => Io.ReadJsonBodyAsync(this);

    public Task WriteJsonAsync(int status, ReadOnlyMemory<byte> utf8Json) => Io.WriteJsonAsync(this, status, utf8Json);

    public Task WriteErrorAsync(RelayError error) => Io.WriteJsonAsync(this, error.Status, RelayErrors.Body(error));

    public Task<WebSocket> AcceptWebSocketAsync() => Io.AcceptWebSocketAsync(this);
}
