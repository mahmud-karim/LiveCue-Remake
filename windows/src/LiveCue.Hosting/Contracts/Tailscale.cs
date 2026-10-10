using System.Globalization;
using System.Text.RegularExpressions;

namespace LiveCue.Hosting;

/// <summary>What is read from "tailscale status --json".</summary>
/// <param name="Online">BackendState is "Running" and Self.Online is true.</param>
/// <param name="DnsName">Self.DNSName without its trailing dot, lower-cased; null when empty or when it
/// fails the host rule of protocol 4.3.</param>
public sealed record TailscaleNodeStatus(bool Online, string? DnsName);

public enum TailscaleReadFailure { None, NotInstalled, Failed, TimedOut, Unparseable }

/// <summary>Exactly one of Value and Failure is meaningful.</summary>
public sealed record TailscaleRead<T>(T? Value, TailscaleReadFailure Failure)
    where T : class;

/// <summary>One web handler of the serve configuration.</summary>
/// <param name="Port">The port after the last colon of the "host:port" key.</param>
/// <param name="Mount">The handler's mount point, for example "/" or "/livecue".</param>
/// <param name="Kind">"Proxy", "Path", "Text", "Redirect", or "Other".</param>
/// <param name="Target">The value of that member, for example "http://127.0.0.1:47841".</param>
/// <param name="Foreground">True when the handler belongs to a foreground session or a service.</param>
public sealed record ServeHandler(int Port, string Mount, string Kind, string Target, bool Foreground);

/// <summary>
/// "tailscale serve status --json", read as architecture 3.9 describes. Leaves is the whole document
/// flattened to (JSON pointer, compact JSON of the leaf), sorted by pointer; rule TS-7 compares it.
/// </summary>
public sealed record ServeSnapshot(
    IReadOnlyList<ServeHandler> Handlers,
    IReadOnlyCollection<int> MentionedPorts,
    IReadOnlyCollection<int> TcpForwardPorts,
    IReadOnlyCollection<int> FunnelPorts,
    IReadOnlyList<KeyValuePair<string, string>> Leaves);

public enum MappingCommandKind { ApplyPrivate, ApplyPublic, RemovePrivate, RemovePublic }

/// <summary>
/// One of the four write commands of architecture 3.9 and nothing else (rules TS-2, TS-3, TS-6).
/// Instances exist only through the four factories, which validate every value. Complete as written.
/// </summary>
public sealed partial class MappingCommand
{
    private MappingCommand(MappingCommandKind kind, int httpsPort, string path, int relayPort, string[] arguments)
    {
        Kind = kind;
        HttpsPort = httpsPort;
        Path = path;
        RelayPort = relayPort;
        Arguments = arguments;
    }

    public MappingCommandKind Kind { get; }

    public int HttpsPort { get; }

    /// <summary>"/" for the private forms; the prefix for the public forms.</summary>
    public string Path { get; }

    /// <summary>The relay port of the target; 0 for the remove forms.</summary>
    public int RelayPort { get; }

    /// <summary>The arguments after the executable, in order.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>"tailscale " + the arguments joined by single spaces. This exact text is what the user is
    /// shown before the command runs.</summary>
    public string DisplayText => "tailscale " + string.Join(' ', Arguments);

    public static string TargetFor(int relayPort)
        => "http://127.0.0.1:" + relayPort.ToString(CultureInfo.InvariantCulture);

    public static MappingCommand ApplyPrivate(int privateHttpsPort, int relayPort)
    {
        RequirePrivatePort(privateHttpsPort);
        RequireRelayPort(relayPort);
        return new(MappingCommandKind.ApplyPrivate, privateHttpsPort, "/", relayPort,
            ["serve", "--bg", "--https=" + Text(privateHttpsPort), TargetFor(relayPort)]);
    }

    public static MappingCommand ApplyPublic(int publicHttpsPort, string prefix, int relayPort)
    {
        RequireFunnelPort(publicHttpsPort);
        RequirePrefix(prefix);
        RequireRelayPort(relayPort);
        return new(MappingCommandKind.ApplyPublic, publicHttpsPort, prefix, relayPort,
            ["funnel", "--bg", "--https=" + Text(publicHttpsPort), "--set-path=" + prefix, TargetFor(relayPort)]);
    }

    public static MappingCommand RemovePrivate(int privateHttpsPort)
    {
        RequirePort(privateHttpsPort, 1);
        return new(MappingCommandKind.RemovePrivate, privateHttpsPort, "/", 0,
            ["serve", "--https=" + Text(privateHttpsPort), "--set-path=/", "off"]);
    }

    public static MappingCommand RemovePublic(int publicHttpsPort, string prefix)
    {
        RequireFunnelPort(publicHttpsPort);
        RequirePrefix(prefix);
        return new(MappingCommandKind.RemovePublic, publicHttpsPort, prefix, 0,
            ["funnel", "--https=" + Text(publicHttpsPort), "--set-path=" + prefix, "off"]);
    }

    // \A and \z, not ^ and $: "$" also matches before a final line feed, which would let a prefix that ends
    // in a line break reach the command line and the confirmation dialog (protocol section 1, shapes).
    [GeneratedRegex(@"\A/[a-z0-9][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void RequirePort(int port, int minimum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, minimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
    }

    // A tailnet-only listener is never created on a port on which Funnel can be switched on
    // (architecture 3.5 and TS-4). RemovePrivate keeps RequirePort: removing is always allowed.
    private static void RequirePrivatePort(int port)
    {
        RequirePort(port, 1);
        if (port is 443 or 8443 or 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
    }

    private static void RequireRelayPort(int relayPort)
    {
        RequirePort(relayPort, 1024);
        ArgumentOutOfRangeException.ThrowIfEqual(relayPort, RelaySettings.ForbiddenPort);
    }

    private static void RequireFunnelPort(int port)
    {
        if (port is not (443 or 8443 or 10000))
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
    }

    private static void RequirePrefix(string prefix)
    {
        if (prefix is null || !PrefixPattern().IsMatch(prefix))
        {
            throw new ArgumentException("The path prefix is not valid.", nameof(prefix));
        }
    }
}

public enum TailscaleWriteOutcome { Succeeded, Failed, TimedOut, NotInstalled }

/// <summary>
/// The Tailscale CLI: two read commands and the four write commands. Every call is limited as rule TS-10
/// says. Output is discarded except for the two JSON status commands. Never throws except for cancellation.
/// </summary>
public interface ITailscaleCli
{
    /// <summary>Check 4 of architecture 3.11: the resolved path, or null when Tailscale is not installed.</summary>
    string? ExecutablePath { get; }

    /// <summary>tailscale status --json</summary>
    Task<TailscaleRead<TailscaleNodeStatus>> ReadStatusAsync(CancellationToken ct);

    /// <summary>tailscale serve status --json</summary>
    Task<TailscaleRead<ServeSnapshot>> ReadServeStatusAsync(CancellationToken ct);

    /// <summary>Runs one write command. Called by the mapping manager only.</summary>
    Task<TailscaleWriteOutcome> RunAsync(MappingCommand command, CancellationToken ct);
}
