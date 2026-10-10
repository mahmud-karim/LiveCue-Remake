using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LiveCue.Relay.Edge;

/// <summary>What step E-1 found. The order of the members is the order in which a request is counted.</summary>
internal enum RefusedHeader
{
    /// <summary>The request carries none of the refused headers.</summary>
    None,

    /// <summary>Private mode, and the request carries <c>Tailscale-Funnel-Request</c>.</summary>
    FunnelInPrivateMode,

    /// <summary>The request carries <c>Origin</c> or <c>Sec-Fetch-Site</c>.</summary>
    Browser,
}

/// <summary>
/// Steps E-1 and E-2 of protocol 5.2 as pure functions. Neither allocates, reads a clock or touches
/// anything but its arguments.
/// </summary>
internal static class BearerCheck
{
    public const string FunnelHeader = "Tailscale-Funnel-Request";

    public const string OriginHeader = "Origin";

    public const string FetchSiteHeader = "Sec-Fetch-Site";

    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// E-1. A header counts as carried whatever its value, even an empty one (W-design K-8), so this
    /// asks for the key and never for a value. The Funnel header is looked at first: a Funnel-marked
    /// request in Private mode is counted as that even when it also carries a browser header
    /// (protocol 5.5).
    /// </summary>
    public static RefusedHeader FindRefusedHeader(IHeaderDictionary headers, ConnectionMode mode)
    {
        if (mode == ConnectionMode.Private && headers.ContainsKey(FunnelHeader))
        {
            return RefusedHeader.FunnelInPrivateMode;
        }

        return headers.ContainsKey(OriginHeader) || headers.ContainsKey(FetchSiteHeader)
            ? RefusedHeader.Browser
            : RefusedHeader.None;
    }

    /// <summary>
    /// E-2: exactly one Authorization value, whose length is 7 plus C-02, which starts with the
    /// case-sensitive scheme and one space, and whose rest is in the token alphabet. The shape is matched
    /// against the whole value by a character loop: a trailing line break or any other extra character
    /// fails. <paramref name="token"/> is the token's characters inside the header value.
    /// </summary>
    public static bool TryReadToken(StringValues authorization, out ReadOnlySpan<char> token)
    {
        token = default;
        if (authorization.Count != 1)
        {
            return false;
        }

        var value = authorization[0].AsSpan();
        if (value.Length != BearerPrefix.Length + ProtocolConstants.TokenLength
            || !value.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = value[BearerPrefix.Length..];
        foreach (var character in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
            {
                return false;
            }
        }

        token = candidate;
        return true;
    }
}
