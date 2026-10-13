using LiveCue.Relay;

namespace LiveCue.Hosting.Tests.Supervisor;

/// <summary>
/// The prerequisite checks as a test scripts them: which check fails, what was found, what the settings
/// file said. Every call is recorded with its options. Nothing is looked at.
/// </summary>
internal sealed class ScriptedPrerequisites : IPrerequisiteChecker
{
    public const string CodexPath = @"C:\LiveCue-tests\codex\codex.exe";
    public const string DnsName = "pc.example.test";
    public const string Version = "codex-cli 1.2.3";

    private readonly Lock _gate = new();
    private readonly List<PrerequisiteOptions> _calls = [];

    /// <summary>What the settings file holds. A start adopts it.</summary>
    public RelaySettings Settings { get; set; } = RelaySettings.Default with { CodexContainmentCheckedVersion = Version };

    public SettingsFileState FileState { get; set; } = SettingsFileState.Loaded;

    /// <summary>The check that fails, or null when every check passes.</summary>
    public PrerequisiteId? Fails { get; set; }

    /// <summary>The version text that was read from the Codex that was found.</summary>
    public string? CodexVersion { get; set; } = Version;

    /// <summary>When set, a check waits for it. Cancelling the check ends the wait.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public IReadOnlyList<PrerequisiteOptions> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public async Task<PrerequisiteReport> CheckAsync(PrerequisiteOptions options, CancellationToken ct)
    {
        lock (_gate)
        {
            _calls.Add(options);
        }

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();

        // The dependencies of the contract: every check needs 1; 3 needs 2; 5 needs 4; 6 needs 5.
        PrerequisiteState State(PrerequisiteId id)
        {
            if (options.SkipTailscale && id >= PrerequisiteId.TailscaleInstalled)
            {
                return PrerequisiteState.NotChecked;
            }

            if (Fails == id)
            {
                return PrerequisiteState.Failed;
            }

            var blocked = Fails switch
            {
                PrerequisiteId.DataFolder => true,
                PrerequisiteId.CodexFound => id == PrerequisiteId.CodexSignedIn,
                PrerequisiteId.TailscaleInstalled => id is PrerequisiteId.TailscaleOnline or PrerequisiteId.TailscaleDnsName,
                PrerequisiteId.TailscaleOnline => id == PrerequisiteId.TailscaleDnsName,
                _ => false,
            };
            return blocked ? PrerequisiteState.NotChecked : PrerequisiteState.Passed;
        }

        var codexFound = State(PrerequisiteId.CodexFound) == PrerequisiteState.Passed;
        var named = State(PrerequisiteId.TailscaleDnsName) == PrerequisiteState.Passed;
        PrerequisiteResult[] results =
        [
            new(PrerequisiteId.DataFolder, State(PrerequisiteId.DataFolder), null),
            new(PrerequisiteId.CodexFound, State(PrerequisiteId.CodexFound), codexFound ? options.CodexPathOverride ?? CodexPath : null),
            new(PrerequisiteId.CodexSignedIn, State(PrerequisiteId.CodexSignedIn), null),
            new(PrerequisiteId.TailscaleInstalled, State(PrerequisiteId.TailscaleInstalled), null),
            new(PrerequisiteId.TailscaleOnline, State(PrerequisiteId.TailscaleOnline), null),
            new(PrerequisiteId.TailscaleDnsName, State(PrerequisiteId.TailscaleDnsName), named ? DnsName : null),
        ];

        return new PrerequisiteReport(
            results,
            new SettingsLoadResult(Settings, FileState, []),
            codexFound ? options.CodexPathOverride ?? CodexPath : null,
            named ? DnsName : null,
            codexFound ? CodexVersion : null);
    }
}

/// <summary>
/// The mapping manager as a test scripts it: what an inspection shows, and a record of what was asked.
/// It reads no Tailscale configuration, and a write that is asked for is counted and refused.
/// </summary>
internal sealed class ScriptedMappings : IMappingManager
{
    public static readonly DateTimeOffset Time = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<RelaySettings> _inspections = [];
    private int _executions;

    /// <summary>The exposure report of every inspection: the handlers that forward to the relay port.</summary>
    public IReadOnlyList<ExposedHandler> Exposure { get; set; } = [];

    /// <summary>Other than None: the serve status could not be read, and the exposure is unknown.</summary>
    public TailscaleReadFailure StatusFailure { get; set; } = TailscaleReadFailure.None;

    /// <summary>When set, every inspection throws it.</summary>
    public Exception? Throws { get; set; }

    /// <summary>The settings of every inspection, in order.</summary>
    public IReadOnlyList<RelaySettings> Inspections
    {
        get
        {
            lock (_gate)
            {
                return [.. _inspections];
            }
        }
    }

    /// <summary>How often a write was asked for. Rule TS-9 wants zero.</summary>
    public int Executions => Volatile.Read(ref _executions);

    public Task<ConnectionInspection> InspectAsync(RelaySettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _inspections.Add(settings);
        }

        if (Throws is { } exception)
        {
            throw exception;
        }

        var unread = StatusFailure != TailscaleReadFailure.None;
        var state = unread ? MappingState.Unknown : MappingState.Absent;
        var refusal = unread ? MappingRefusal.StatusUnavailable : MappingRefusal.None;
        return Task.FromResult(new ConnectionInspection(
            new MappingInspection(ConnectionMode.Private, state, refusal, InLedger: false, settings.PrivateHttpsPort, "/"),
            new MappingInspection(ConnectionMode.Public, state, refusal, InLedger: false, settings.PublicHttpsPort, settings.PublicPathPrefix),
            StaleOwned: [],
            StatusFailure,
            Time,
            unread ? [] : Exposure));
    }

    public MappingPlan? PlanApply(ConnectionMode mode, RelaySettings settings, string? dnsName, out MappingRefusal refusal)
    {
        refusal = MappingRefusal.InvalidSettings;
        return null;
    }

    public MappingPlan PlanRemoval(OwnedMapping entry, string? dnsName) =>
        throw new NotSupportedException("No test of the supervisor plans a removal.");

    public Task<MappingResult> ExecuteAsync(MappingPlan plan, CancellationToken ct)
    {
        Interlocked.Increment(ref _executions);
        return Task.FromResult(new MappingResult(
            MappingOutcome.Refused, MappingRefusal.StatusUnavailable, OthersChanged: false, RelaySettings.Default, After: null));
    }
}
