using LiveCue.Relay;

namespace LiveCue.Hosting;

public enum MappingState
{
    /// <summary>The serve configuration could not be read.</summary>
    Unknown,

    /// <summary>No handler at the configured port and mount; it can be added.</summary>
    Absent,

    /// <summary>Our handler exists and is in the ledger.</summary>
    Present,

    /// <summary>A handler with our port, mount and target exists but is not in the ledger. Usable; never
    /// removed by this application (TS-8).</summary>
    PresentNotOwned,

    /// <summary>It cannot be added; BlockedBy says why.</summary>
    Blocked,
}

/// <summary>Why the manager will not run a command. Texts are in section 4.15 of the W-design.</summary>
public enum MappingRefusal
{
    None,
    TailscaleNotInstalled,

    /// <summary>TS-1: the status command failed or timed out.</summary>
    StatusUnavailable,

    /// <summary>TS-1: the status JSON does not have the expected shape.</summary>
    StatusUnreadable,

    /// <summary>TS-2: a port or the prefix fails the settings rules.</summary>
    InvalidSettings,

    /// <summary>TS-6.</summary>
    ForbiddenRelayPort,

    /// <summary>TS-4: the private port is occupied by something that is not our handler alone.</summary>
    PrivatePortOccupied,

    /// <summary>TS-4: the private port has Funnel on.</summary>
    PrivatePortHasFunnel,

    /// <summary>TS-5: the mount, or the mount with a trailing slash, exists and is not ours.</summary>
    PublicMountOccupied,

    /// <summary>TS-5: the public port forwards raw TCP.</summary>
    PublicPortHasTcpForward,

    /// <summary>TS-5: the public port is occupied and Funnel is off for it.</summary>
    PublicPortWouldPublishOthers,

    /// <summary>Apply: our handler is already there. Nothing is run.</summary>
    AlreadyPresent,

    /// <summary>Remove: there is no handler at that port and mount.</summary>
    NotPresent,

    /// <summary>TS-8: the handler is not in the ledger.</summary>
    NotInLedger,

    /// <summary>TS-8: the ledger entry and the live handler differ.</summary>
    LedgerMismatch,

    /// <summary>The plan no longer matches the current settings; the user must look at the command again.</summary>
    PlanOutOfDate,
}

public enum MappingAction { ApplyPrivate, ApplyPublic, RemovePrivate, RemovePublic }

/// <summary>State of one mode's mapping for the current settings.</summary>
public sealed record MappingInspection(
    ConnectionMode Mode,
    MappingState State,
    MappingRefusal BlockedBy,
    bool InLedger,
    int HttpsPort,
    string Path);

/// <summary>
/// One row of the exposure report (architecture 3.9): a live handler, on any port and mount and in any
/// structure of the serve status, whose kind is Proxy and whose target is exactly
/// MappingCommand.TargetFor(relay port). It is what Tailscale forwards to the relay, whoever created it.
/// </summary>
/// <param name="FunnelOn">Funnel is on for that port: the handler is reachable from the internet.</param>
/// <param name="Foreground">It belongs to a foreground session or a service.</param>
/// <param name="LedgerEntry">The ledger entry with the same mode, port, path and target, or null.</param>
public sealed record ExposedHandler(
    int HttpsPort,
    string Mount,
    bool FunnelOn,
    bool Foreground,
    OwnedMapping? LedgerEntry);

/// <summary>Result of one read-only inspection.</summary>
/// <param name="StaleOwned">Ledger entries that still match a live handler exactly and are not the selected
/// mode's mapping at its current port and path: the other mode's mapping, or one left at an old port.
/// Each is a standing warning with "Remove".</param>
/// <param name="StatusFailure">None when the serve status was read.</param>
/// <param name="Exposure">The exposure report, sorted by port and then mount (ordinal). Empty when the
/// serve status could not be read: StatusFailure then says that exposure is unknown, not absent.</param>
public sealed record ConnectionInspection(
    MappingInspection Private,
    MappingInspection Public,
    IReadOnlyList<OwnedMapping> StaleOwned,
    TailscaleReadFailure StatusFailure,
    DateTimeOffset At,
    IReadOnlyList<ExposedHandler> Exposure)
{
    public MappingInspection For(ConnectionMode mode) => mode == ConnectionMode.Private ? Private : Public;

    /// <summary>True when some handler that forwards to the relay port is published through Funnel. In
    /// Private mode the supervisor does not start the relay while this is true (architecture 3.11, 6a).</summary>
    public bool HasFunnelExposure => Exposure.Any(handler => handler.FunnelOn);

    /// <summary>The rows that are not in the ledger: shown with the removal command as text, never removed
    /// by the application (TS-8).</summary>
    public IEnumerable<ExposedHandler> UnownedExposure => Exposure.Where(handler => handler.LedgerEntry is null);
}

/// <summary>What a confirmation dialog shows and what will run if it is confirmed.</summary>
/// <param name="EffectText">One sentence in plain language (section 4.15 of the W-design).</param>
/// <param name="Entry">For a removal: the ledger entry to remove. Null for an apply.</param>
public sealed record MappingPlan(
    MappingAction Action,
    MappingCommand Command,
    string EffectText,
    OwnedMapping? Entry);

public enum MappingOutcome
{
    /// <summary>The command ran, the result was verified and the ledger was saved.</summary>
    Done,

    /// <summary>Nothing was run. Refusal says why.</summary>
    Refused,
    CommandFailed,

    /// <summary>TS-10: the user is told to run the shown command in a terminal.</summary>
    CommandTimedOut,

    /// <summary>The command reported success but the re-read does not show the expected state.</summary>
    NotVerified,

    /// <summary>The mapping changed but settings.json could not be saved.</summary>
    LedgerNotSaved,
}

/// <param name="OthersChanged">TS-7: an entry that is not ours differs from the snapshot taken before.
/// Reported prominently; nothing is repaired.</param>
/// <param name="Settings">The settings after the action, with the ledger as saved.</param>
public sealed record MappingResult(
    MappingOutcome Outcome,
    MappingRefusal Refusal,
    bool OthersChanged,
    RelaySettings Settings,
    ConnectionInspection? After);

/// <summary>
/// Architecture 3.9. InspectAsync and Plan never write. ExecuteAsync is the only member that can run a
/// write command; it is called only from a confirmation dialog that showed plan.Command.DisplayText
/// (rule TS-9). Never throws except for cancellation.
/// </summary>
public interface IMappingManager
{
    /// <summary>Reads the serve status once and classifies both modes for <paramref name="settings"/>.</summary>
    Task<ConnectionInspection> InspectAsync(RelaySettings settings, CancellationToken ct);

    /// <summary>
    /// Pure. The apply command of <paramref name="mode"/> and its effect text, from settings alone. Returns
    /// null and a refusal (InvalidSettings or ForbiddenRelayPort) when no valid command exists.
    /// </summary>
    MappingPlan? PlanApply(ConnectionMode mode, RelaySettings settings, string? dnsName, out MappingRefusal refusal);

    /// <summary>Pure. The remove command for one ledger entry, built from the entry's own port and path.</summary>
    MappingPlan PlanRemoval(OwnedMapping entry, string? dnsName);

    /// <summary>
    /// TS-1: reads the status. Refuses with PlanOutOfDate when an apply plan differs from the plan of the
    /// settings saved now, or a removal's entry is no longer in the ledger. Applies TS-4, TS-5 or TS-8.
    /// Runs the command. Reads the status again and verifies the result. Updates and saves the ledger.
    /// Applies TS-7.
    /// </summary>
    Task<MappingResult> ExecuteAsync(MappingPlan plan, CancellationToken ct);
}
