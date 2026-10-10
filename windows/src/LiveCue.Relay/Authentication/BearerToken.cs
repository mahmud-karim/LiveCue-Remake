using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Primitives;

namespace LiveCue.Relay.Authentication;

/// <summary>Reads the token out of an <c>Authorization: Bearer &lt;token&gt;</c> header.</summary>
internal static class BearerToken
{
    private const string Scheme = "Bearer";

    /// <summary>
    /// Accepts exactly one header value of the form "Bearer", one or more whitespace characters, then
    /// a non-empty token. The scheme is matched without regard to case.
    /// </summary>
    public static bool TryRead(StringValues authorization, [NotNullWhen(true)] out string? token)
    {
        token = null;
        if (authorization.Count != 1)
        {
            return false;
        }

        var value = authorization[0].AsSpan();
        if (value.Length <= Scheme.Length
            || !value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            || !char.IsWhiteSpace(value[Scheme.Length]))
        {
            return false;
        }

        var candidate = value[Scheme.Length..].TrimStart();
        if (candidate.IsEmpty)
        {
            return false;
        }

        token = candidate.ToString();
        return true;
    }
}
