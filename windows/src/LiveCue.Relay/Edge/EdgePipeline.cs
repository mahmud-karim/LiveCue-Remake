using LiveCue.Relay.Pairing;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Routes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace LiveCue.Relay.Edge;

/// <summary>
/// The checks every request passes, in the order protocol 5.2 fixes. The first failing step answers.
///
/// <list type="table">
/// <item><term>E-1</term><description>no Origin, no Sec-Fetch-Site; in Private mode no Tailscale-Funnel-Request: 401</description></item>
/// <item><term>E-2</term><description>exactly one Authorization header, "Bearer " plus 43 base64url characters: 401</description></item>
/// <item><term>E-3</term><description>the token's hash is the accepted hash: 401. Then the presence event fires.</description></item>
/// <item><term>E-4</term><description>method and raw request target equal a route exactly: 404</description></item>
/// <item><term>E-5</term><description>X-LiveCue-Protocol is exactly "2": 400</description></item>
/// <item><term>E-6</term><description>GET and DELETE declare no body: 400</description></item>
/// </list>
///
/// The three 401 answers are byte-identical, so a caller without the token cannot tell which step
/// refused it and learns nothing about routes. No work happens before E-3 passes.
///
/// Not built yet: E-7 (the cap on requests in flight), body reading (5.4) and route deadlines (5.5).
/// </summary>
internal sealed class EdgePipeline(
    ConnectionMode mode,
    ITokenVerifier tokens,
    RouteTable routes,
    Action onAuthenticated)
{
    private const string BearerPrefix = "Bearer ";
    private const string ProtocolHeader = "X-LiveCue-Protocol";
    private const string FunnelHeader = "Tailscale-Funnel-Request";
    private const string SecFetchSiteHeader = "Sec-Fetch-Site";

    private static readonly string ExpectedProtocol =
        ProtocolConstants.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var headers = request.Headers;

        // E-1. A header counts as carried whatever its value, even empty.
        if (headers.ContainsKey("Origin")
            || headers.ContainsKey(SecFetchSiteHeader)
            || (mode == ConnectionMode.Private && headers.ContainsKey(FunnelHeader)))
        {
            return EdgeResponses.WriteErrorAsync(context, WireError.Unauthorized);
        }

        // E-2 and E-3.
        if (!TryReadToken(headers.Authorization, out var token) || !tokens.IsAccepted(token))
        {
            return EdgeResponses.WriteErrorAsync(context, WireError.Unauthorized);
        }

        // Presence: every authenticated request, whatever its route and outcome.
        onAuthenticated();

        // E-4. The raw target is the text on the request line, before any decoding.
        var target = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        if (!routes.TryFind(request.Method, target, out var route))
        {
            return EdgeResponses.WriteErrorAsync(context, WireError.NotFound);
        }

        // E-5.
        var protocol = headers[ProtocolHeader];
        if (protocol.Count != 1 || !string.Equals(protocol[0], ExpectedProtocol, StringComparison.Ordinal))
        {
            return EdgeResponses.WriteErrorAsync(context, WireError.ProtocolMismatch);
        }

        // E-6.
        if (!route.ReadsBody && DeclaresBody(request))
        {
            return EdgeResponses.WriteErrorAsync(context, WireError.InvalidRequest);
        }

        return route.Handler(context);
    }

    /// <summary>E-2: exactly one header, case-sensitive scheme, one space, a token of the right shape.</summary>
    private static bool TryReadToken(StringValues authorization, out ReadOnlySpan<char> token)
    {
        token = default;
        if (authorization.Count != 1)
        {
            return false;
        }

        var value = authorization[0].AsSpan();
        if (!value.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = value[BearerPrefix.Length..];
        if (!PairingToken.HasValidShape(candidate))
        {
            return false;
        }

        token = candidate;
        return true;
    }

    private static bool DeclaresBody(HttpRequest request) =>
        request.Headers.ContainsKey("Transfer-Encoding") || request.ContentLength is > 0;
}
