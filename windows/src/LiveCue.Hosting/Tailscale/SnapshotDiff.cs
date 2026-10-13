namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// Rule TS-7: what changed between two serve snapshots besides our own handler.
///
/// The two documents are compared leaf by leaf. Ignored are the leaves under
/// <c>/Web/&lt;host&gt;:&lt;port&gt;/Handlers/&lt;mount&gt;</c> for our port and mount and, only when the port
/// was unoccupied or held only our handler before the command, the leaves under <c>/TCP/&lt;port&gt;</c>,
/// <c>/AllowFunnel/&lt;host&gt;:&lt;port&gt;</c> and <c>/Web/&lt;host&gt;:&lt;port&gt;</c>, which Tailscale adds and
/// removes together with the handler. Every other difference is somebody else's entry. This class only
/// reports; nothing is ever repaired.
/// </summary>
internal static class SnapshotDiff
{
    /// <summary>True when an entry that is not ours differs between the two snapshots.</summary>
    /// <param name="portWasOursAlone">The port was unoccupied, or held only our handler, in <paramref name="before"/>.</param>
    public static bool OthersChanged(ServeSnapshot before, ServeSnapshot after, int port, string mount, bool portWasOursAlone) =>
        ChangedPointers(before, after, port, mount, portWasOursAlone).Count > 0
        || StructureChanged(before, after, port, mount, portWasOursAlone);

    /// <summary>The pointers of the leaves that differ and are not ours, sorted.</summary>
    public static IReadOnlyList<string> ChangedPointers(
        ServeSnapshot before, ServeSnapshot after, int port, string mount, bool portWasOursAlone)
    {
        var old = Others(before, port, mount, portWasOursAlone);
        var now = Others(after, port, mount, portWasOursAlone);

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (pointer, value) in old)
        {
            if (!now.TryGetValue(pointer, out var current) || !string.Equals(current, value, StringComparison.Ordinal))
            {
                changed.Add(pointer);
            }
        }

        foreach (var pointer in now.Keys)
        {
            if (!old.ContainsKey(pointer))
            {
                changed.Add(pointer);
            }
        }

        return [.. changed];
    }

    /// <summary>True for a leaf that belongs to our handler, or to the port our handler has to itself.</summary>
    internal static bool IsOurs(string pointer, int port, string mount, bool portWasOursAlone)
    {
        var tokens = Tokens(pointer);
        if (tokens.Length < 2)
        {
            return false;
        }

        switch (tokens[0])
        {
            case "Web" when IsHostPort(tokens[1], port):
                return portWasOursAlone
                    || (tokens.Length >= 4
                        && string.Equals(tokens[2], "Handlers", StringComparison.Ordinal)
                        && string.Equals(tokens[3], mount, StringComparison.Ordinal));
            case "TCP":
                return portWasOursAlone && ServeStatusParser.TryReadPort(tokens[1], out var tcpPort) && tcpPort == port;
            case "AllowFunnel":
                return portWasOursAlone && IsHostPort(tokens[1], port);
            default:
                return false;
        }
    }

    private static bool IsHostPort(string key, int port) =>
        ServeStatusParser.TryReadHostPort(key, out var keyPort) && keyPort == port;

    /// <summary>The reference tokens of a JSON pointer (RFC 6901), unescaped. The root has none.</summary>
    private static string[] Tokens(string pointer)
    {
        if (pointer.Length == 0 || pointer[0] != '/')
        {
            return [];
        }

        var tokens = pointer[1..].Split('/');
        for (var index = 0; index < tokens.Length; index++)
        {
            tokens[index] = tokens[index].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        }

        return tokens;
    }

    /// <summary>
    /// The leaves that are not ours. An empty object or array holds no entry of anybody, so it is the
    /// same as its absence: Tailscale drops a map when its last entry goes.
    /// </summary>
    private static Dictionary<string, string> Others(ServeSnapshot snapshot, int port, string mount, bool portWasOursAlone)
    {
        var others = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (pointer, value) in snapshot.Leaves)
        {
            if (value is "{}" or "[]" || IsOurs(pointer, port, mount, portWasOursAlone))
            {
                continue;
            }

            // A pointer that appears twice cannot come from the parser; it then counts with both values.
            others[pointer] = others.TryGetValue(pointer, out var earlier) ? earlier + "\n" + value : value;
        }

        return others;
    }

    /// <summary>
    /// The same question asked of the parsed view, so that a snapshot without leaves (one that did not
    /// come from the parser) cannot hide a change: the handlers that are not at our port and mount, and
    /// the three sets of ports without our port when we had it to ourselves.
    /// </summary>
    private static bool StructureChanged(ServeSnapshot before, ServeSnapshot after, int port, string mount, bool portWasOursAlone)
    {
        bool Ours(ServeHandler handler) =>
            handler.Port == port
            && !handler.Foreground
            && (portWasOursAlone || string.Equals(handler.Mount, mount, StringComparison.Ordinal));

        HashSet<ServeHandler> Handlers(ServeSnapshot snapshot) => [.. snapshot.Handlers.Where(handler => !Ours(handler))];

        HashSet<int> Ports(IReadOnlyCollection<int> ports) => [.. ports.Where(value => !(portWasOursAlone && value == port))];

        return !Handlers(before).SetEquals(Handlers(after))
            || !Ports(before.MentionedPorts).SetEquals(Ports(after.MentionedPorts))
            || !Ports(before.TcpForwardPorts).SetEquals(Ports(after.TcpForwardPorts))
            || !Ports(before.FunnelPorts).SetEquals(Ports(after.FunnelPorts));
    }
}
