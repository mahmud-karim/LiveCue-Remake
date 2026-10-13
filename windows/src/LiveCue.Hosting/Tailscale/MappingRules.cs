using LiveCue.Relay;

namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// Occupied, ours, and rules TS-2, TS-4, TS-5, TS-6 and TS-8 as pure functions (W-design 4.15). Nothing
/// here reads a file, starts a process or keeps state: every answer follows from a serve snapshot, the
/// settings and the ledger alone.
///
/// The words of the design. A port is <b>occupied</b> when the serve status mentions it anywhere. A
/// handler is <b>ours</b> for (port, mount, target) when its port and mount are those, its kind is Proxy,
/// its target is exactly that text and it belongs to no foreground session and no service. A port
/// <b>holds only ours</b> when our handler is its only handler and it forwards no raw TCP.
/// </summary>
internal static class MappingRules
{
    /// <summary>The mount of the Private mapping.</summary>
    public const string RootMount = "/";

    private const string TargetPrefix = "http://127.0.0.1:";
    private const int LowestRelayPort = 1024;

    // ---- Commands: the only places in the application that ask for a MappingCommand. ----

    /// <summary>The apply command of a mode, or null when the factory refuses the values (rule TS-2).</summary>
    public static MappingCommand? ApplyCommand(ConnectionMode mode, RelaySettings settings)
    {
        try
        {
            return mode switch
            {
                ConnectionMode.Private => MappingCommand.ApplyPrivate(settings.PrivateHttpsPort, settings.RelayPort),
                ConnectionMode.Public => MappingCommand.ApplyPublic(
                    settings.PublicHttpsPort, settings.PublicPathPrefix, settings.RelayPort),
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The remove command of a ledger entry, from its own port and path; null when the factory
    /// refuses them, or when a Private entry names another path than the root.</summary>
    public static MappingCommand? RemovalCommand(OwnedMapping entry)
    {
        try
        {
            return entry.Mode switch
            {
                ConnectionMode.Private when string.Equals(entry.Path, RootMount, StringComparison.Ordinal) =>
                    MappingCommand.RemovePrivate(entry.HttpsPort),
                ConnectionMode.Public => MappingCommand.RemovePublic(entry.HttpsPort, entry.Path),
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The command built again by its own factory from its kind and numbers. Null when the argument list
    /// it carries is not, text for text, what the factory makes: a list that was altered after the
    /// factory returned never reaches a process (rules TS-2 and TS-3).
    /// </summary>
    public static MappingCommand? Canonical(MappingCommand command)
    {
        MappingCommand rebuilt;
        try
        {
            rebuilt = command.Kind switch
            {
                MappingCommandKind.ApplyPrivate => MappingCommand.ApplyPrivate(command.HttpsPort, command.RelayPort),
                MappingCommandKind.ApplyPublic => MappingCommand.ApplyPublic(command.HttpsPort, command.Path, command.RelayPort),
                MappingCommandKind.RemovePrivate => MappingCommand.RemovePrivate(command.HttpsPort),
                MappingCommandKind.RemovePublic => MappingCommand.RemovePublic(command.HttpsPort, command.Path),
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };
        }
        catch (ArgumentException)
        {
            return null;
        }

        return SameArguments(rebuilt, command) ? rebuilt : null;
    }

    public static bool SameArguments(MappingCommand left, MappingCommand right) =>
        left.Arguments is not null
        && right.Arguments is not null
        && left.Arguments.SequenceEqual(right.Arguments, StringComparer.Ordinal);

    // ---- Settings and the ledger. ----

    /// <summary>
    /// Rules TS-2 and TS-6: why these settings make no command, or None. The relay port, both HTTPS
    /// ports and the prefix must pass the settings rules as a whole, whichever mode is asked for.
    /// </summary>
    public static MappingRefusal CheckSettings(RelaySettings settings, ISettingsValidator validator)
    {
        if (settings.RelayPort == RelaySettings.ForbiddenPort)
        {
            return MappingRefusal.ForbiddenRelayPort;
        }

        return validator.ValidateStructure(settings).Any(static problem => problem.Field
            is SettingsField.RelayPort or SettingsField.PrivateHttpsPort
            or SettingsField.PublicHttpsPort or SettingsField.PublicPathPrefix)
            ? MappingRefusal.InvalidSettings
            : MappingRefusal.None;
    }

    /// <summary>Rule TS-6: the text is exactly <c>http://127.0.0.1:&lt;port&gt;</c> for a port a relay may
    /// use: 1024 to 65535 and never the old relay's port.</summary>
    public static bool IsRelayTarget(string? target)
    {
        return target is not null
            && target.StartsWith(TargetPrefix, StringComparison.Ordinal)
            && ServeStatusParser.TryReadPort(target.AsSpan(TargetPrefix.Length), out var port)
            && IsRelayPort(port);
    }

    public static bool IsRelayPort(int port) =>
        port is >= LowestRelayPort and <= 65535 && port != RelaySettings.ForbiddenPort;

    /// <summary>
    /// True when a ledger entry is one this application can have written: a remove command exists for
    /// its mode, port and path, and its target is a relay target. Any other entry (a file edited by
    /// hand) is treated as if it were not in the ledger: never shown as ours, never removed.
    /// </summary>
    public static bool IsOwnable(OwnedMapping? entry) =>
        entry is { Path: not null } && IsRelayTarget(entry.Target) && RemovalCommand(entry) is not null;

    /// <summary>The ledger entry for that mode, port, path and target, or null.</summary>
    public static OwnedMapping? FindEntry(
        IReadOnlyList<OwnedMapping>? ledger, ConnectionMode mode, int httpsPort, string path, string target) =>
        ledger?.FirstOrDefault(entry => IsOwnable(entry) && SameMapping(entry, mode, httpsPort, path, target));

    /// <summary>Two entries name the same mapping when mode, port, path and target agree; the time does not count.</summary>
    public static bool SameMapping(OwnedMapping left, OwnedMapping right) =>
        SameMapping(left, right.Mode, right.HttpsPort, right.Path, right.Target);

    // ---- The serve status. ----

    public static bool IsOccupied(ServeSnapshot snapshot, int port) =>
        snapshot.MentionedPorts.Contains(port)
        || snapshot.TcpForwardPorts.Contains(port)
        || snapshot.FunnelPorts.Contains(port)
        || snapshot.Handlers.Any(handler => handler.Port == port);

    public static bool FunnelOn(ServeSnapshot snapshot, int port) => snapshot.FunnelPorts.Contains(port);

    public static bool ForwardsTcp(ServeSnapshot snapshot, int port) => snapshot.TcpForwardPorts.Contains(port);

    public static bool IsOurs(ServeHandler handler, int port, string mount, string target) =>
        handler.Port == port
        && !handler.Foreground
        && string.Equals(handler.Mount, mount, StringComparison.Ordinal)
        && string.Equals(handler.Kind, ServeStatusParser.ProxyKind, StringComparison.Ordinal)
        && string.Equals(handler.Target, target, StringComparison.Ordinal);

    public static bool OursExists(ServeSnapshot snapshot, int port, string mount, string target) =>
        snapshot.Handlers.Any(handler => IsOurs(handler, port, mount, target));

    public static bool HoldsOnlyOurs(ServeSnapshot snapshot, int port, string mount, string target) =>
        !ForwardsTcp(snapshot, port)
        && snapshot.Handlers.Count(handler => handler.Port == port) == 1
        && OursExists(snapshot, port, mount, target);

    /// <summary>For rules TS-4, TS-5 and TS-7 a port that holds only ours counts as unoccupied.</summary>
    public static bool IsFreeOrOnlyOurs(ServeSnapshot snapshot, int port, string mount, string target) =>
        !IsOccupied(snapshot, port) || HoldsOnlyOurs(snapshot, port, mount, target);

    /// <summary>The entry's own handler is still there: ours by the entry's port, path and target.</summary>
    public static bool Matches(ServeSnapshot snapshot, OwnedMapping entry) =>
        IsOwnable(entry) && OursExists(snapshot, entry.HttpsPort, entry.Path, entry.Target);

    /// <summary>A background handler, whoever made it, is mounted at that port and path.</summary>
    public static bool AnyBackgroundHandlerAt(ServeSnapshot snapshot, int port, string mount) =>
        snapshot.Handlers.Any(handler => IsBackgroundAt(handler, port, mount));

    // ---- Inspection (the table of W-design 4.15, first match). ----

    /// <summary>
    /// The state of one mode's mapping. <paramref name="settingsRefusal"/> is what
    /// <see cref="CheckSettings"/> said: settings that make no command are Blocked with that reason, so
    /// that a handler which points at a port no relay may use is never called ours (rule TS-6).
    /// </summary>
    public static MappingInspection Inspect(
        ConnectionMode mode, ServeSnapshot snapshot, RelaySettings settings, MappingRefusal settingsRefusal)
    {
        var isPublic = mode == ConnectionMode.Public;
        var port = isPublic ? settings.PublicHttpsPort : settings.PrivateHttpsPort;
        var mount = isPublic ? settings.PublicPathPrefix : RootMount;

        MappingInspection Result(MappingState state, MappingRefusal blockedBy = MappingRefusal.None) =>
            new(mode, state, blockedBy, InLedger: state == MappingState.Present, port, mount);

        if (settingsRefusal != MappingRefusal.None)
        {
            return Result(MappingState.Blocked, settingsRefusal);
        }

        if (ApplyCommand(mode, settings) is null)
        {
            return Result(MappingState.Blocked, MappingRefusal.InvalidSettings);
        }

        var target = MappingCommand.TargetFor(settings.RelayPort);
        var present = FindEntry(settings.OwnedMappings, mode, port, mount, target) is null
            ? MappingState.PresentNotOwned
            : MappingState.Present;
        var oursExists = OursExists(snapshot, port, mount, target);

        if (!isPublic)
        {
            if (oursExists)
            {
                return Result(present);
            }

            // TS-4: anything else on the port, in any structure, blocks a private listener there.
            if (IsOccupied(snapshot, port))
            {
                return Result(
                    MappingState.Blocked,
                    FunnelOn(snapshot, port) ? MappingRefusal.PrivatePortHasFunnel : MappingRefusal.PrivatePortOccupied);
            }

            return Result(MappingState.Absent);
        }

        if (oursExists && FunnelOn(snapshot, port))
        {
            return Result(present);
        }

        // TS-5: Tailscale replaces a handler at "<prefix>/" when one is set at "<prefix>", and the reverse.
        var withSlash = mount + "/";
        if (snapshot.Handlers.Any(handler => handler.Port == port
                && (string.Equals(handler.Mount, mount, StringComparison.Ordinal)
                    || string.Equals(handler.Mount, withSlash, StringComparison.Ordinal))
                && !IsOurs(handler, port, mount, target)))
        {
            return Result(MappingState.Blocked, MappingRefusal.PublicMountOccupied);
        }

        if (ForwardsTcp(snapshot, port))
        {
            return Result(MappingState.Blocked, MappingRefusal.PublicPortHasTcpForward);
        }

        if (FunnelOn(snapshot, port) || IsFreeOrOnlyOurs(snapshot, port, mount, target))
        {
            return Result(MappingState.Absent);
        }

        // TS-5: tailnet-only handlers of others are on the port; Funnel would publish them.
        return Result(MappingState.Blocked, MappingRefusal.PublicPortWouldPublishOthers);
    }

    /// <summary>
    /// Ledger entries whose own handler is still there and which are not the selected mode's mapping
    /// for the current port, path and relay port: the other mode's mapping, or one left at an old port.
    /// </summary>
    public static IReadOnlyList<OwnedMapping> StaleOwned(ServeSnapshot snapshot, RelaySettings settings)
    {
        var selected = settings.ConnectionMode;
        var isPublic = selected == ConnectionMode.Public;
        var port = isPublic ? settings.PublicHttpsPort : settings.PrivateHttpsPort;
        var mount = isPublic ? settings.PublicPathPrefix : RootMount;
        var target = MappingCommand.TargetFor(settings.RelayPort);

        var stale = new List<OwnedMapping>();
        foreach (var entry in settings.OwnedMappings ?? [])
        {
            if (Matches(snapshot, entry)
                && !SameMapping(entry, selected, port, mount, target)
                && !stale.Exists(listed => SameMapping(listed, entry)))
            {
                stale.Add(entry);
            }
        }

        return stale.AsReadOnly();
    }

    /// <summary>
    /// The exposure report of architecture 3.9: every handler, on any port and mount and in any structure,
    /// that proxies to exactly the relay's target. It is read from what Tailscale reports and never from
    /// the ledger; the ledger only says, per row, whether the row is one of ours.
    /// </summary>
    public static IReadOnlyList<ExposedHandler> Exposure(ServeSnapshot snapshot, RelaySettings settings)
    {
        if (!IsRelayPort(settings.RelayPort))
        {
            // No relay can listen there, so nothing "forwards to this relay" (rule TS-6).
            return [];
        }

        var target = MappingCommand.TargetFor(settings.RelayPort);
        var rows = new List<ExposedHandler>();
        foreach (var handler in snapshot.Handlers)
        {
            if (!string.Equals(handler.Kind, ServeStatusParser.ProxyKind, StringComparison.Ordinal)
                || !string.Equals(handler.Target, target, StringComparison.Ordinal))
            {
                continue;
            }

            var funnelOn = FunnelOn(snapshot, handler.Port);
            var entry = handler.Foreground
                ? null
                : FindEntry(
                    settings.OwnedMappings,
                    funnelOn ? ConnectionMode.Public : ConnectionMode.Private,
                    handler.Port,
                    handler.Mount,
                    target);
            var row = new ExposedHandler(handler.Port, handler.Mount, funnelOn, handler.Foreground, entry);
            if (!rows.Contains(row))
            {
                rows.Add(row);
            }
        }

        rows.Sort(static (left, right) =>
        {
            if (left.HttpsPort != right.HttpsPort)
            {
                return left.HttpsPort.CompareTo(right.HttpsPort);
            }

            var byMount = string.CompareOrdinal(left.Mount, right.Mount);
            return byMount != 0 ? byMount : left.Foreground.CompareTo(right.Foreground);
        });
        return rows.AsReadOnly();
    }

    // ---- Removal (rule TS-8). ----

    /// <summary>
    /// None when exactly one background handler is at the entry's port and path, it is the entry's own
    /// (kind and target), and no background handler is at the path with a final slash. NotPresent when
    /// no background handler is at the path. LedgerMismatch otherwise.
    /// </summary>
    public static MappingRefusal CheckRemoval(ServeSnapshot snapshot, OwnedMapping entry)
    {
        var there = snapshot.Handlers.Where(handler => IsBackgroundAt(handler, entry.HttpsPort, entry.Path)).ToList();
        if (there.Count == 0)
        {
            return MappingRefusal.NotPresent;
        }

        if (there.Count != 1 || !IsOurs(there[0], entry.HttpsPort, entry.Path, entry.Target))
        {
            return MappingRefusal.LedgerMismatch;
        }

        // Tailscale treats "<prefix>" and "<prefix>/" as one mount when a handler is set (see Inspect).
        // Should it do the same when one is removed, a handler somebody else put at "<prefix>/" would go
        // with ours. Nobody's handler is removed on a guess: the removal is refused.
        return !string.Equals(entry.Path, RootMount, StringComparison.Ordinal)
            && AnyBackgroundHandlerAt(snapshot, entry.HttpsPort, entry.Path + "/")
            ? MappingRefusal.LedgerMismatch
            : MappingRefusal.None;
    }

    private static bool IsBackgroundAt(ServeHandler handler, int port, string mount) =>
        handler.Port == port && !handler.Foreground && string.Equals(handler.Mount, mount, StringComparison.Ordinal);

    private static bool SameMapping(OwnedMapping entry, ConnectionMode mode, int httpsPort, string path, string target) =>
        entry.Mode == mode
        && entry.HttpsPort == httpsPort
        && string.Equals(entry.Path, path, StringComparison.Ordinal)
        && string.Equals(entry.Target, target, StringComparison.Ordinal);
}
