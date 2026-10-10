using System.Diagnostics.CodeAnalysis;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>One row of the route table: whether the route reads a body, and what serves it.</summary>
internal sealed record Route(bool ReadsBody, RequestDelegate Handler);

/// <summary>
/// The route table of protocol 6.1. A route is a method plus an exact request target, compared as
/// text: case-sensitive, no query string, no trailing slash, no percent-encoding. That is why this is
/// a dictionary and not ASP.NET Core endpoint routing, which ignores case and trailing slashes and
/// matches encoded spellings.
///
/// The spike has route 1. Routes 2 to 10 are added here as they are built.
/// </summary>
internal sealed class RouteTable
{
    public const string HealthTarget = "/v1/health";

    private readonly Dictionary<(string Method, string Target), Route> _routes = new()
    {
        [(HttpMethods.Get, HealthTarget)] = new Route(ReadsBody: false, Health),
    };

    public bool TryFind(string method, string rawTarget, [NotNullWhen(true)] out Route? route) =>
        _routes.TryGetValue((method, rawTarget), out route);

    /// <summary>Route 1, <c>GET /v1/health</c>: liveness, version check, heartbeat.</summary>
    private static Task Health(HttpContext context) =>
        EdgeResponses.WriteJsonAsync(
            context, StatusCodes.Status200OK, HealthResponse.Current, WireJsonContext.Default.HealthResponse);
}
