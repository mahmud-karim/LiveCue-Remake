using LiveCue.Relay.Pairing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LiveCue.Relay.Edge;

/// <summary>Steps E-1 and E-2 of protocol 5.2 as pure functions.</summary>
internal static class BearerCheck
{
    public const string FunnelHeader = "Tailscale-Funnel-Request";

    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// E-1: true when the request carries Origin or Sec-Fetch-Site, or, in Private mode,
    /// Tailscale-Funnel-Request. A header counts as carried whatever its value, even empty (K-8).
    /// </summary>
    public static bool CarriesRefusedHeader(IHeaderDictionary headers, ConnectionMode mode) =>
        headers.ContainsKey("Origin")
        || headers.ContainsKey("Sec-Fetch-Site")
        || (mode == ConnectionMode.Private && headers.ContainsKey(FunnelHeader));

    /// <summary>
    /// E-2: exactly one Authorization value, the case-sensitive scheme, one space, and a token of the
    /// shape C-02. <paramref name="token"/> is the token's characters inside the header value.
    /// </summary>
    public static bool TryReadToken(StringValues authorization, out ReadOnlySpan<char> token)
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
}
