using LiveCue.Relay;

namespace LiveCue.Hosting;

/// <summary>One entry of the ledger of architecture 3.9: a mapping this application created.</summary>
/// <param name="Path">"/" for Private; the path prefix for Public.</param>
/// <param name="Target">Always http://127.0.0.1:&lt;relay port at creation&gt;.</param>
public sealed record OwnedMapping(
    ConnectionMode Mode,
    int HttpsPort,
    string Path,
    string Target,
    DateTimeOffset CreatedAt);

/// <summary>
/// The content of settings.json (architecture 3.5). Immutable; "with" produces changed copies. The default
/// instance is what a missing file means. Compare two instances with <see cref="SameValues"/>, because the
/// ledger is a list.
/// </summary>
public sealed record RelaySettings
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The old relay's port. Never accepted, offered or suggested anywhere.</summary>
    public const int ForbiddenPort = 47831;

    /// <summary>The lab's port inside its container; not usable as a host port setting.</summary>
    public const int LabContainerPort = 8765;

    public static RelaySettings Default { get; } = new();

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public int RelayPort { get; init; } = 47841;

    public ConnectionMode ConnectionMode { get; init; } = ConnectionMode.Private;

    public int PrivateHttpsPort { get; init; } = 11443;

    public int PublicHttpsPort { get; init; } = 10000;

    public string PublicPathPrefix { get; init; } = "/livecue";

    public string? CodexExecutablePath { get; init; }

    public string? AsrLabFolder { get; init; }

    public int AsrLabPort { get; init; } = 47842;

    public IReadOnlyList<OwnedMapping> OwnedMappings { get; init; } = [];

    /// <summary>Member-wise comparison, the ledger compared entry by entry in order.</summary>
    public bool SameValues(RelaySettings other)
        => other is not null
            && this with { OwnedMappings = [] } == other with { OwnedMappings = [] }
            && OwnedMappings.SequenceEqual(other.OwnedMappings);
}
