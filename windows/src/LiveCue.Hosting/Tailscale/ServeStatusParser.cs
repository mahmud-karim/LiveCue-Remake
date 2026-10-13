using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// Reads <c>tailscale serve status --json</c> into a <see cref="ServeSnapshot"/> (W-design 4.15).
///
/// The reader fails closed: a document that is not exactly the shape architecture 3.9 describes is
/// "unparseable", and rule TS-1 then writes nothing. Nothing in the document is trusted: a mount, a
/// host or a target is only ever compared with a value this application built itself, and none of them
/// can reach a command line.
/// </summary>
internal static class ServeStatusParser
{
    public const string ProxyKind = "Proxy";
    public const string OtherKind = "Other";

    private const string HandlersMember = "Handlers";
    private const string TcpForwardMember = "TCPForward";

    /// <summary>The kinds of a web handler, in the order in which the first one present names the handler.</summary>
    private static readonly string[] HandlerKinds = [ProxyKind, "Path", "Text", "Redirect"];

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    public static bool TryParse(string json, [NotNullWhen(true)] out ServeSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(json);
        return TryParse(System.Text.Encoding.UTF8.GetBytes(json), out snapshot);
    }

    /// <summary>False when the bytes are not the expected document; nothing is thrown.</summary>
    public static bool TryParse(ReadOnlyMemory<byte> utf8, [NotNullWhen(true)] out ServeSnapshot? snapshot)
    {
        snapshot = null;
        if (utf8.Span.StartsWith(ByteOrderMark))
        {
            utf8 = utf8[ByteOrderMark.Length..];
        }

        try
        {
            using var document = JsonDocument.Parse(utf8, Options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var leaves = new List<KeyValuePair<string, string>>();
            var content = new Content();
            if (!Flatten(root, string.Empty, leaves) || !content.Read(root, topLevel: true))
            {
                return false;
            }

            leaves.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
            content.Handlers.Sort(CompareHandlers);

            snapshot = new ServeSnapshot(
                content.Handlers.AsReadOnly(),
                Array.AsReadOnly([.. content.Mentioned]),
                Array.AsReadOnly([.. content.TcpForward]),
                Array.AsReadOnly([.. content.Funnel]),
                leaves.AsReadOnly());
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            // Not JSON, too deep, or text that is not valid UTF-16 once unescaped.
            return false;
        }
    }

    /// <summary>The port of a "host:port" key: the text after the last colon.</summary>
    internal static bool TryReadHostPort(string key, out int port)
    {
        port = 0;
        var colon = key.LastIndexOf(':');
        return colon > 0 && TryReadPort(key.AsSpan(colon + 1), out port);
    }

    /// <summary>A port as Tailscale writes one: 1 to 65535 in decimal digits, with no sign, space or leading zero.</summary>
    internal static bool TryReadPort(ReadOnlySpan<char> text, out int port)
    {
        port = 0;
        if (text.Length is 0 or > 5 || text[0] == '0')
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        port = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return port <= 65535;
    }

    /// <summary>By port, then mount (ordinal), then background before foreground.</summary>
    private static int CompareHandlers(ServeHandler left, ServeHandler right)
    {
        if (left.Port != right.Port)
        {
            return left.Port.CompareTo(right.Port);
        }

        var byMount = string.CompareOrdinal(left.Mount, right.Mount);
        return byMount != 0 ? byMount : left.Foreground.CompareTo(right.Foreground);
    }

    /// <summary>One reference token of a JSON pointer (RFC 6901).</summary>
    internal static string EscapeToken(string token) =>
        token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>
    /// The whole document as (pointer, compact JSON of the leaf). An empty object or array is a leaf.
    /// False for an object that names a member twice: no encoder writes that, and two readers could
    /// disagree about which of the two counts.
    /// </summary>
    private static bool Flatten(JsonElement element, string pointer, List<KeyValuePair<string, string>> leaves)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in element.EnumerateObject())
                {
                    if (!names.Add(member.Name) || !Flatten(member.Value, pointer + "/" + EscapeToken(member.Name), leaves))
                    {
                        return false;
                    }
                }

                if (names.Count == 0)
                {
                    leaves.Add(new(pointer, "{}"));
                }

                return true;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (!Flatten(item, pointer + "/" + index.ToString(CultureInfo.InvariantCulture), leaves))
                    {
                        return false;
                    }

                    index++;
                }

                if (index == 0)
                {
                    leaves.Add(new(pointer, "[]"));
                }

                return true;

            case JsonValueKind.String:
                // One spelling per text, whatever escapes the document used.
                leaves.Add(new(pointer, "\"" + JsonEncodedText.Encode(element.GetString()!, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\""));
                return true;

            default:
                leaves.Add(new(pointer, element.GetRawText()));
                return true;
        }
    }

    /// <summary>What the rules need from the document: the handlers and the three sets of ports.</summary>
    private sealed class Content
    {
        public List<ServeHandler> Handlers { get; } = [];

        public SortedSet<int> Mentioned { get; } = [];

        public SortedSet<int> TcpForward { get; } = [];

        public SortedSet<int> Funnel { get; } = [];

        /// <summary>
        /// Reads TCP, Web and AllowFunnel of one configuration; at the top level also every foreground
        /// session and every service, each with the same three shapes. False for an unexpected shape.
        /// </summary>
        public bool Read(JsonElement config, bool topLevel)
        {
            var foreground = !topLevel;
            foreach (var member in config.EnumerateObject())
            {
                var read = member.Name switch
                {
                    "TCP" => ReadTcp(member.Value),
                    "Web" => ReadWeb(member.Value, foreground),
                    "AllowFunnel" => ReadAllowFunnel(member.Value),
                    "Foreground" or "Services" when topLevel => ReadNested(member.Value),
                    _ => true,
                };
                if (!read)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ReadNested(JsonElement configs)
        {
            if (configs.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var config in configs.EnumerateObject())
            {
                if (config.Value.ValueKind != JsonValueKind.Object || !Read(config.Value, topLevel: false))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ReadTcp(JsonElement ports)
        {
            if (ports.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var entry in ports.EnumerateObject())
            {
                if (!TryReadPort(entry.Name, out var port) || entry.Value.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                Mentioned.Add(port);
                if (entry.Value.TryGetProperty(TcpForwardMember, out _))
                {
                    TcpForward.Add(port);
                }
            }

            return true;
        }

        private bool ReadWeb(JsonElement servers, bool foreground)
        {
            if (servers.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var server in servers.EnumerateObject())
            {
                if (!TryReadHostPort(server.Name, out var port) || server.Value.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                Mentioned.Add(port);
                if (!server.Value.TryGetProperty(HandlersMember, out var handlers) || handlers.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (handlers.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (var handler in handlers.EnumerateObject())
                {
                    if (!TryReadHandler(handler.Value, out var kind, out var target))
                    {
                        return false;
                    }

                    Handlers.Add(new ServeHandler(port, handler.Name, kind, target, foreground));
                }
            }

            return true;
        }

        private bool ReadAllowFunnel(JsonElement entries)
        {
            if (entries.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var entry in entries.EnumerateObject())
            {
                if (!TryReadHostPort(entry.Name, out var port)
                    || entry.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return false;
                }

                Mentioned.Add(port);
                if (entry.Value.ValueKind == JsonValueKind.True)
                {
                    Funnel.Add(port);
                }
            }

            return true;
        }

        private static bool TryReadHandler(JsonElement handler, out string kind, out string target)
        {
            kind = OtherKind;
            target = string.Empty;
            if (handler.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (handler.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var candidate in HandlerKinds)
            {
                if (!handler.TryGetProperty(candidate, out var value))
                {
                    continue;
                }

                if (value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                kind = candidate;
                target = value.GetString()!;
                return true;
            }

            return true;
        }
    }
}
