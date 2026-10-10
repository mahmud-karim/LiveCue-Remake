using LiveCue.Relay.Pairing;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay;

/// <summary>How phones reach the relay, which decides one edge rule (protocol E-1).</summary>
public enum ConnectionMode
{
    /// <summary>Reachable inside the tailnet only. A request that came through Tailscale Funnel is refused.</summary>
    Private,

    /// <summary>Reachable from the internet through Tailscale Funnel, protected by the pairing token.</summary>
    Public,
}

/// <summary>
/// Everything a relay instance is started with (architecture 3.4). The record has the full shape the
/// architecture defines; the spike reads <see cref="Port"/>, <see cref="AdvertisedEndpoint"/>,
/// <see cref="Mode"/> and <see cref="RelayVersion"/>, and carries the rest for the features that
/// follow.
/// </summary>
/// <param name="Port">Loopback port. 0 picks a free port and is for tests only.</param>
/// <param name="AdvertisedEndpoint">The address phones use, in the canonical form of protocol 4.3: absolute https, ending with "/".</param>
/// <param name="Mode">Private or Public.</param>
/// <param name="DataDirectory">Where pairing.json and schemas\ live.</param>
/// <param name="CodexExecutablePath">Absolute path of codex.exe, or null when none was found.</param>
/// <param name="LabFolder">The speech lab folder; null means PC speech models are not configured.</param>
/// <param name="LabPort">Loopback port of the speech lab.</param>
/// <param name="RelayVersion">The version text reported to phones.</param>
/// <param name="WarmCatalogOnStart">Read the answer-model catalogue as soon as the relay starts.</param>
public sealed record RelayOptions(
    int Port,
    string AdvertisedEndpoint,
    ConnectionMode Mode,
    string DataDirectory,
    string? CodexExecutablePath,
    string? LabFolder,
    int LabPort,
    string RelayVersion,
    bool WarmCatalogOnStart = true)
{
    /// <summary>True when <see cref="Port"/> may be bound: 0 (tests) or an allowed relay port.</summary>
    internal bool HasUsablePort => Port == 0 || ProtocolConstants.IsAllowedRelayPort(Port);

    /// <summary>True when <see cref="AdvertisedEndpoint"/> is in the canonical form of protocol 4.3.</summary>
    internal bool HasCanonicalEndpoint => EndpointGrammar.IsCanonical(AdvertisedEndpoint);
}

/// <summary>
/// Collaborators a relay can be given in place of its own (architecture 3.2, "seams"). Production
/// code passes none; tests and the desktop self-test replace what they need.
/// </summary>
public sealed class RelayDependencies
{
    /// <summary>The clock behind every event time and every deadline.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Replaces the pairing store's verdict on a presented token. Tests only.</summary>
    internal ITokenVerifier? TokenVerifier { get; init; }
}
