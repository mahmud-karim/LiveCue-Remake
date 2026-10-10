using System.Globalization;
using LiveCue.Relay;
using LiveCue.Relay.Pairing;

namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// The mapping manager of architecture 3.9 (W-design 4.15): the only code that asks the Tailscale CLI to
/// change anything, and only from <see cref="ExecuteAsync"/>, which a confirmation dialog calls after it
/// showed the exact command (rule TS-9). <see cref="InspectAsync"/> reads the serve status;
/// <see cref="PlanApply"/> and <see cref="PlanRemoval"/> read nothing at all.
///
/// What <see cref="ExecuteAsync"/> runs is never the command object of the plan it was handed. It builds
/// the command again from the settings saved now, or from the ledger entry, and runs that; the plan only
/// has to be equal to it. So no value reaches the command line except through the four factories of
/// <see cref="MappingCommand"/>, whatever a plan, the settings file, the ledger or Tailscale's own
/// status contain (rules TS-2, TS-3 and TS-6).
/// </summary>
#pragma warning disable CA1001 // The semaphore is only awaited, so it never creates a wait handle; the manager lives as long as the application.
internal sealed class MappingManager : IMappingManager
#pragma warning restore CA1001
{
    private readonly ITailscaleCli _tailscale;
    private readonly ISettingsStore _settings;
    private readonly ISettingsValidator _validator;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _oneWrite = new(1, 1);

    public MappingManager(ITailscaleCli tailscale, ISettingsStore settings, ISettingsValidator validator, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(tailscale);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(clock);
        _tailscale = tailscale;
        _settings = settings;
        _validator = validator;
        _clock = clock;
    }

    public async Task<ConnectionInspection> InspectAsync(RelaySettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();

        if (_tailscale.ExecutablePath is null)
        {
            return Unknown(settings, TailscaleReadFailure.NotInstalled);
        }

        var read = await _tailscale.ReadServeStatusAsync(ct).ConfigureAwait(false);
        return read is { Failure: TailscaleReadFailure.None, Value: { } snapshot }
            ? Inspect(snapshot, settings)
            : Unknown(settings, FailureOf(read));
    }

    public MappingPlan? PlanApply(ConnectionMode mode, RelaySettings settings, string? dnsName, out MappingRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(settings);

        refusal = MappingRules.CheckSettings(settings, _validator);
        if (refusal != MappingRefusal.None)
        {
            return null;
        }

        // The factory has the last word: it also refuses a private listener on a port Funnel can use.
        var command = MappingRules.ApplyCommand(mode, settings);
        if (command is null)
        {
            refusal = MappingRefusal.InvalidSettings;
            return null;
        }

        var action = command.Kind == MappingCommandKind.ApplyPublic ? MappingAction.ApplyPublic : MappingAction.ApplyPrivate;
        return new MappingPlan(
            action,
            command,
            MappingTexts.Effect(action, Address(dnsName, command.HttpsPort, command.Path), settings.RelayPort),
            Entry: null);
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> for an entry no remove command exists for (a Public entry on
    /// a port Funnel cannot use, a Private entry with a path, a prefix outside the settings rule). No such
    /// entry is ever handed out by an inspection: StaleOwned and the exposure rows carry removable entries only.
    /// </summary>
    public MappingPlan PlanRemoval(OwnedMapping entry, string? dnsName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var command = MappingRules.RemovalCommand(entry)
            ?? throw new ArgumentException("No remove command exists for this ledger entry.", nameof(entry));
        var action = command.Kind == MappingCommandKind.RemovePublic ? MappingAction.RemovePublic : MappingAction.RemovePrivate;
        return new MappingPlan(
            action,
            command,
            MappingTexts.Effect(action, Address(dnsName, command.HttpsPort, command.Path), relayPort: 0),
            entry);
    }

    public async Task<MappingResult> ExecuteAsync(MappingPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();

        // One write at a time: the second waits and then reads the status the first one left.
        await _oneWrite.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExecuteCoreAsync(plan, ct).ConfigureAwait(false);
        }
        finally
        {
            _oneWrite.Release();
        }
    }

    private async Task<MappingResult> ExecuteCoreAsync(MappingPlan plan, CancellationToken ct)
    {
        // Step 1.
        if (_tailscale.ExecutablePath is null)
        {
            return Refused(MappingRefusal.TailscaleNotInstalled);
        }

        // Step 2 (TS-1). Without a status that was read and understood, nothing is written.
        var beforeRead = await _tailscale.ReadServeStatusAsync(ct).ConfigureAwait(false);
        if (beforeRead is not { Failure: TailscaleReadFailure.None, Value: { } before })
        {
            return Refused(RefusalOf(FailureOf(beforeRead)));
        }

        // Step 3. The command is built again from what is saved now; the plan must be equal to it.
        var saved = _settings.Load().Settings;
        var removal = plan.Action is MappingAction.RemovePrivate or MappingAction.RemovePublic;
        var prepared = removal ? PrepareRemoval(plan, saved, out var refusal) : PrepareApply(plan, saved, out refusal);
        if (prepared is not { } step)
        {
            return Refused(refusal, saved, Inspect(before, saved));
        }

        // Step 4 (TS-4, TS-5, TS-8).
        if (removal)
        {
            refusal = MappingRules.CheckRemoval(before, step.Entry);
            if (refusal == MappingRefusal.NotPresent)
            {
                // The handler is gone already: nothing to run, and the stale entry leaves the ledger.
                var cleaned = TrySaveLedger(ledger => ledger.Where(entry => MappingRules.Matches(before, entry)), out _);
                return Refused(refusal, cleaned, Inspect(before, cleaned));
            }
        }
        else
        {
            var inspection = MappingRules.Inspect(step.Entry.Mode, before, saved, MappingRefusal.None);
            refusal = inspection.State switch
            {
                MappingState.Absent => MappingRefusal.None,
                MappingState.Blocked => inspection.BlockedBy,
                _ => MappingRefusal.AlreadyPresent,
            };

            // A handler that could not be recorded afterwards would be one LiveCue made and then
            // disowns. Settings that a save would reject are refused before anything runs.
            if (refusal == MappingRefusal.None && _validator.Validate(saved).Count > 0)
            {
                refusal = MappingRefusal.InvalidSettings;
            }
        }

        if (refusal != MappingRefusal.None)
        {
            return Refused(refusal, saved, Inspect(before, saved));
        }

        var portWasOursAlone = MappingRules.IsFreeOrOnlyOurs(before, step.Entry.HttpsPort, step.Entry.Path, step.Entry.Target);

        // Step 5 (TS-10). A cancellation that arrives while the command runs ends the command and is
        // thrown. Once the command has returned, the caller's cancellation is no longer followed: the
        // status is read again and the ledger is brought in line before anything is reported.
        var written = await _tailscale.RunAsync(step.Command, ct).ConfigureAwait(false);
        var succeeded = written == TailscaleWriteOutcome.Succeeded;
        var outcome = written switch
        {
            TailscaleWriteOutcome.Succeeded => MappingOutcome.Done,
            TailscaleWriteOutcome.TimedOut => MappingOutcome.CommandTimedOut,
            _ => MappingOutcome.CommandFailed,
        };

        // Step 6.
        var afterRead = await _tailscale.ReadServeStatusAsync(CancellationToken.None).ConfigureAwait(false);
        if (afterRead is not { Failure: TailscaleReadFailure.None, Value: { } after })
        {
            var unverified = _settings.Load().Settings;
            return new MappingResult(
                succeeded ? MappingOutcome.NotVerified : outcome,
                MappingRefusal.None,
                OthersChanged: false,
                unverified,
                Unknown(unverified, FailureOf(afterRead)));
        }

        // Step 7.
        var verified = removal
            ? !MappingRules.AnyBackgroundHandlerAt(after, step.Entry.HttpsPort, step.Entry.Path)
            : MappingRules.OursExists(after, step.Entry.HttpsPort, step.Entry.Path, step.Entry.Target)
                && (step.Entry.Mode == ConnectionMode.Private || MappingRules.FunnelOn(after, step.Entry.HttpsPort));
        if (succeeded && !verified)
        {
            outcome = MappingOutcome.NotVerified;
        }

        // Step 8. An entry is added only for a command that reported success and was then seen in the
        // status: the ledger never claims a handler this application did not see itself create.
        var claim = succeeded && verified && !removal;
        var final = TrySaveLedger(
            ledger =>
            {
                var kept = ledger.Where(entry => !MappingRules.SameMapping(entry, step.Entry) || (!claim && !(removal && verified)));
                return (claim ? kept.Append(step.Entry) : kept).Where(entry => MappingRules.Matches(after, entry));
            },
            out var ledgerSaved);
        if (!ledgerSaved && outcome == MappingOutcome.Done)
        {
            outcome = MappingOutcome.LedgerNotSaved;
        }

        // Step 9 (TS-7). Reported, never repaired.
        var othersChanged = SnapshotDiff.OthersChanged(before, after, step.Entry.HttpsPort, step.Entry.Path, portWasOursAlone);

        // Step 10.
        return new MappingResult(outcome, MappingRefusal.None, othersChanged, final, Inspect(after, final));
    }

    /// <summary>The apply command of the plan's mode from the saved settings, and the entry it would add.</summary>
    private (MappingCommand Command, OwnedMapping Entry)? PrepareApply(MappingPlan plan, RelaySettings saved, out MappingRefusal refusal)
    {
        var mode = plan.Action == MappingAction.ApplyPublic ? ConnectionMode.Public : ConnectionMode.Private;
        var fresh = PlanApply(mode, saved, dnsName: null, out refusal);
        if (fresh is null)
        {
            return null;
        }

        if (plan.Action != fresh.Action
            || plan.Entry is not null
            || plan.Command is null
            || !MappingRules.SameArguments(fresh.Command, plan.Command))
        {
            refusal = MappingRefusal.PlanOutOfDate;
            return null;
        }

        // The ledger keeps the time to the second, as settings.json writes it.
        var command = fresh.Command;
        var now = _clock.GetUtcNow().UtcTicks;
        return (command, new OwnedMapping(
            mode,
            command.HttpsPort,
            command.Path,
            MappingCommand.TargetFor(command.RelayPort),
            new DateTimeOffset(now - (now % TimeSpan.TicksPerSecond), TimeSpan.Zero)));
    }

    /// <summary>The remove command of the plan's entry, built from the entry as the ledger holds it now.</summary>
    private static (MappingCommand Command, OwnedMapping Entry)? PrepareRemoval(MappingPlan plan, RelaySettings saved, out MappingRefusal refusal)
    {
        var entry = plan.Entry is null
            ? null
            : (saved.OwnedMappings ?? []).FirstOrDefault(listed => listed is not null && MappingRules.SameMapping(listed, plan.Entry));
        if (entry is null)
        {
            refusal = MappingRefusal.PlanOutOfDate;
            return null;
        }

        // TS-8 and TS-6: an entry this application cannot have written is not a ledger entry.
        if (!MappingRules.IsOwnable(entry))
        {
            refusal = MappingRefusal.NotInLedger;
            return null;
        }

        var command = MappingRules.RemovalCommand(entry)!;
        var expected = command.Kind == MappingCommandKind.RemovePublic ? MappingAction.RemovePublic : MappingAction.RemovePrivate;
        if (plan.Action != expected || plan.Command is null || !MappingRules.SameArguments(command, plan.Command))
        {
            refusal = MappingRefusal.PlanOutOfDate;
            return null;
        }

        refusal = MappingRefusal.None;
        return (command, entry);
    }

    /// <summary>
    /// Applies <paramref name="edit"/> to the ledger of the settings as they are saved at this moment and
    /// saves when that changed anything. Returns the settings as saved. <paramref name="saved"/> is false
    /// when a save was needed and failed; the settings on disk are then the old ones.
    /// </summary>
    private RelaySettings TrySaveLedger(Func<IEnumerable<OwnedMapping>, IEnumerable<OwnedMapping>> edit, out bool saved)
    {
        saved = true;
        var current = _settings.Load().Settings;
        IReadOnlyList<OwnedMapping> ledger = current.OwnedMappings ?? [];
        OwnedMapping[] edited = [.. edit(ledger.Where(entry => entry is not null))];
        if (edited.SequenceEqual(ledger))
        {
            return current;
        }

        try
        {
            _settings.Save(current with { OwnedMappings = Array.AsReadOnly(edited) });
        }
        catch (Exception exception) when (exception
            is IOException or SettingsValidationException or UnauthorizedAccessException or NotSupportedException
            or System.Security.SecurityException or InvalidOperationException or ArgumentException)
        {
            saved = false;
        }

        return _settings.Load().Settings;
    }

    private ConnectionInspection Inspect(ServeSnapshot snapshot, RelaySettings settings)
    {
        var refusal = MappingRules.CheckSettings(settings, _validator);
        return new ConnectionInspection(
            MappingRules.Inspect(ConnectionMode.Private, snapshot, settings, refusal),
            MappingRules.Inspect(ConnectionMode.Public, snapshot, settings, refusal),
            MappingRules.StaleOwned(snapshot, settings),
            TailscaleReadFailure.None,
            _clock.GetUtcNow(),
            MappingRules.Exposure(snapshot, settings));
    }

    /// <summary>Both modes Unknown, and an empty exposure report that means "unknown", not "nothing".</summary>
    private ConnectionInspection Unknown(RelaySettings settings, TailscaleReadFailure failure)
    {
        var refusal = RefusalOf(failure);
        return new ConnectionInspection(
            new MappingInspection(ConnectionMode.Private, MappingState.Unknown, refusal, InLedger: false, settings.PrivateHttpsPort, MappingRules.RootMount),
            new MappingInspection(ConnectionMode.Public, MappingState.Unknown, refusal, InLedger: false, settings.PublicHttpsPort, settings.PublicPathPrefix),
            StaleOwned: [],
            failure,
            _clock.GetUtcNow(),
            Exposure: []);
    }

    private MappingResult Refused(MappingRefusal refusal) =>
        Refused(refusal, _settings.Load().Settings, after: null);

    private static MappingResult Refused(MappingRefusal refusal, RelaySettings settings, ConnectionInspection? after) =>
        new(MappingOutcome.Refused, refusal, OthersChanged: false, settings, after);

    /// <summary>A read that claims success without a value is a failed read.</summary>
    private static TailscaleReadFailure FailureOf(TailscaleRead<ServeSnapshot>? read) =>
        read is null || read.Failure == TailscaleReadFailure.None ? TailscaleReadFailure.Failed : read.Failure;

    private static MappingRefusal RefusalOf(TailscaleReadFailure failure) => failure switch
    {
        TailscaleReadFailure.NotInstalled => MappingRefusal.TailscaleNotInstalled,
        TailscaleReadFailure.Unparseable => MappingRefusal.StatusUnreadable,
        _ => MappingRefusal.StatusUnavailable,
    };

    /// <summary>The advertised endpoint of a mapping, or null when the name is unknown or makes no valid endpoint.</summary>
    private static string? Address(string? dnsName, int httpsPort, string path)
    {
        if (string.IsNullOrWhiteSpace(dnsName))
        {
            return null;
        }

        var candidate = string.Create(
            CultureInfo.InvariantCulture,
            $"https://{dnsName.Trim().TrimEnd('.')}:{httpsPort}{(path == "/" ? string.Empty : path)}/");
        return AdvertisedEndpoint.TryCanonicalize(candidate, out var canonical) ? canonical : null;
    }
}
