namespace LiveCue.Relay;

/// <summary>How the relay is reached. Decides step E-1 of the protocol.</summary>
public enum ConnectionMode
{
    /// <summary>Tailnet only. Requests marked as arriving through Funnel are refused.</summary>
    Private,

    /// <summary>Funnel path mount. Funnel-marked requests are served.</summary>
    Public,
}

/// <summary>Life cycle of one relay instance. An instance is started once and never restarted.</summary>
public enum RelayStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted,
}

/// <summary>
/// Everything one relay instance is told. Immutable. Contains no secret.
/// Validation happens in <see cref="IRelay.StartAsync"/>, not in the constructor.
/// </summary>
/// <param name="Port">Loopback port. 0 picks a free port (tests only). The supervisor never passes 47831.</param>
/// <param name="AdvertisedEndpoint">Protocol 4.3. Canonicalised at start; a text that fails the grammar is the
/// fault <see cref="RelayFaultCode.InvalidEndpoint"/>.</param>
/// <param name="Mode">Protocol E-1.</param>
/// <param name="DataDirectory">Existing folder. Holds <c>pairing.json</c> and <c>schemas\</c>.</param>
/// <param name="CodexExecutablePath">Absolute path of <c>codex.exe</c>, or null when none was found. With null,
/// the catalogue is unavailable and every answer run fails; the relay still starts.</param>
/// <param name="LabFolder">Folder with the two launcher scripts, or null: PC speech is "not configured".</param>
/// <param name="LabPort">Host port of the lab (protocol 11.1).</param>
/// <param name="RelayVersion">Reported by health and sent to the catalogue process.</param>
/// <param name="WarmCatalogOnStart">Reads the catalogue once after start and ignores the outcome.</param>
public sealed record RelayOptions(
    int Port,
    string AdvertisedEndpoint,
    ConnectionMode Mode,
    string DataDirectory,
    string? CodexExecutablePath,
    string? LabFolder,
    int LabPort,
    string RelayVersion,
    bool WarmCatalogOnStart = true);
