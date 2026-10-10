using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace LiveCue.Relay.Protocol;

/// <summary>
/// The advertised endpoint of protocol 4.3, parsed directly and not through a general URL parser, so
/// the relay and the phone agree exactly on what is valid:
///
/// <code>
/// endpoint = "https://" host [ ":" port ] [ path ]
/// host     = label *( "." label )            ; total length 1..253
/// label    = 1..63 of [A-Za-z0-9-], not starting or ending with "-"
/// port     = decimal 1..65535, no leading zero
/// path     = "/" [ segment *( "/" segment ) [ "/" ] ]     ; at most 4 segments
/// segment  = 1..64 of [A-Za-z0-9._~-], and not "." and not ".."
/// </code>
///
/// The canonical form is "https://", the host in lower case, ":port" when a port was given, and the
/// path with exactly one trailing "/" (an absent path becomes "/").
/// </summary>
public static class EndpointGrammar
{
    private const string Scheme = "https://";
    private const int MaxHostLength = 253;
    private const int MaxLabelLength = 63;
    private const int MaxSegments = 4;
    private const int MaxSegmentLength = 64;

    /// <summary>True when <paramref name="text"/> matches the grammar; <paramref name="canonical"/> is then its canonical form.</summary>
    public static bool TryCanonicalize([NotNullWhen(true)] string? text, [NotNullWhen(true)] out string? canonical)
    {
        canonical = null;
        if (text is null || !text.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = text.AsSpan(Scheme.Length);

        var hostEnd = rest.IndexOfAny(':', '/');
        var host = hostEnd < 0 ? rest : rest[..hostEnd];
        if (!IsHost(host))
        {
            return false;
        }

        rest = rest[host.Length..];

        var port = ReadOnlySpan<char>.Empty;
        if (rest.StartsWith(":"))
        {
            rest = rest[1..];
            var portEnd = rest.IndexOf('/');
            port = portEnd < 0 ? rest : rest[..portEnd];
            if (!IsPort(port))
            {
                return false;
            }

            rest = rest[port.Length..];
        }

        var path = "/";
        if (rest.Length > 1)
        {
            // rest starts with "/". One trailing "/" is allowed; an empty segment anywhere is not.
            var body = rest[1..];
            if (body[^1] == '/')
            {
                body = body[..^1];
            }

            var segments = 0;
            foreach (var range in body.Split('/'))
            {
                if (++segments > MaxSegments || !IsSegment(body[range]))
                {
                    return false;
                }
            }

            path = string.Concat("/", body, "/");
        }

        canonical = string.Concat(
            Scheme,
            host.ToString().ToLowerInvariant(),
            port.IsEmpty ? string.Empty : string.Concat(":", port),
            path);
        return true;
    }

    /// <summary>True when <paramref name="text"/> is already in the canonical form.</summary>
    public static bool IsCanonical([NotNullWhen(true)] string? text) =>
        TryCanonicalize(text, out var canonical) && string.Equals(canonical, text, StringComparison.Ordinal);

    private static bool IsHost(ReadOnlySpan<char> host)
    {
        if (host.Length is 0 or > MaxHostLength)
        {
            return false;
        }

        foreach (var range in host.Split('.'))
        {
            var label = host[range];
            if (label.Length is 0 or > MaxLabelLength || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (var character in label)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsPort(ReadOnlySpan<char> port)
    {
        if (port.Length is 0 or > 5 || port[0] == '0')
        {
            return false;
        }

        foreach (var character in port)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return int.Parse(port, NumberStyles.None, CultureInfo.InvariantCulture) <= 65535;
    }

    private static bool IsSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length is 0 or > MaxSegmentLength || segment is "." or "..")
        {
            return false;
        }

        foreach (var character in segment)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '~' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
