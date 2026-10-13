using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// Reads <c>tailscale status --json</c>: whether this PC is online, and its DNS name.
///
/// Only three members are read: <c>BackendState</c>, <c>Self.Online</c> and <c>Self.DNSName</c>. The
/// rest of the document (peers, users, addresses) is never kept.
/// </summary>
internal static class TailscaleStatusParser
{
    private const string Running = "Running";
    private const int MaxHostLength = 253;
    private const int MaxLabelLength = 63;

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    public static bool TryParse(string json, [NotNullWhen(true)] out TailscaleNodeStatus? status)
    {
        ArgumentNullException.ThrowIfNull(json);
        return TryParse(System.Text.Encoding.UTF8.GetBytes(json), out status);
    }

    /// <summary>False when the bytes are not the expected document; nothing is thrown.</summary>
    public static bool TryParse(ReadOnlyMemory<byte> utf8, [NotNullWhen(true)] out TailscaleNodeStatus? status)
    {
        status = null;
        if (utf8.Span.StartsWith(ByteOrderMark))
        {
            utf8 = utf8[ByteOrderMark.Length..];
        }

        try
        {
            using var document = JsonDocument.Parse(utf8);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("BackendState", out var backendState)
                || backendState.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var online = false;
            string? dnsName = null;

            // A PC that is signed out has no "Self"; that is a readable document of a PC that is offline.
            if (root.TryGetProperty("Self", out var self) && self.ValueKind != JsonValueKind.Null)
            {
                if (self.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (self.TryGetProperty("Online", out var selfOnline))
                {
                    if (selfOnline.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        return false;
                    }

                    online = selfOnline.ValueKind == JsonValueKind.True;
                }

                if (self.TryGetProperty("DNSName", out var name) && name.ValueKind != JsonValueKind.Null)
                {
                    if (name.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    dnsName = NormalizeDnsName(name.GetString());
                }
            }

            status = new TailscaleNodeStatus(
                online && string.Equals(backendState.GetString(), Running, StringComparison.Ordinal),
                dnsName);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The name without its trailing dot, in lower case; null when it is empty or is not a host of
    /// protocol 4.3 (labels of 1 to 63 letters, digits and hyphens, no hyphen at either end, 253 in all).
    /// </summary>
    internal static string? NormalizeDnsName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var host = name.EndsWith('.') ? name[..^1] : name;
        return IsHost(host) ? host.ToLowerInvariant() : null;
    }

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
}
