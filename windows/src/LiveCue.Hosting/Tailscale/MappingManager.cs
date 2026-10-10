using System.Globalization;
using LiveCue.Relay;
using LiveCue.Relay.Pairing;

namespace LiveCue.Hosting.Tailscale;

/// <summary>
/// The mapping manager of architecture 3.9 (W-design 4.15).
///
/// First version (foundation): planning is real, because a plan is made from settings alone. Inspection
/// always reports both modes as Unknown, and <see cref="ExecuteAsync"/> refuses every plan without
/// running anything: no Tailscale command, read or write, leaves this class. Work package WP-W07 builds
/// the inspection and the execution with rules TS-1 to TS-10.
/// </summary>
internal sealed class MappingManager : IMappingManager
{
    private readonly ITailscaleCli _tailscale;
    private readonly ISettingsStore _settings;
    private readonly ISettingsValidator _validator;
    private readonly TimeProvider _clock;

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

    public Task<ConnectionInspection> InspectAsync(RelaySettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();

        var refusal = Unavailable;
        return Task.FromResult(new ConnectionInspection(
            new MappingInspection(ConnectionMode.Private, MappingState.Unknown, refusal, InLedger: false, settings.PrivateHttpsPort, "/"),
            new MappingInspection(ConnectionMode.Public, MappingState.Unknown, refusal, InLedger: false, settings.PublicHttpsPort, settings.PublicPathPrefix),
            StaleOwned: [],
            _tailscale.ExecutablePath is null ? TailscaleReadFailure.NotInstalled : TailscaleReadFailure.Failed,
            _clock.GetUtcNow()));
    }

    public MappingPlan? PlanApply(ConnectionMode mode, RelaySettings settings, string? dnsName, out MappingRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.RelayPort == RelaySettings.ForbiddenPort)
        {
            refusal = MappingRefusal.ForbiddenRelayPort;
            return null;
        }

        if (_validator.ValidateStructure(settings).Any(problem => problem.Field
                is SettingsField.RelayPort or SettingsField.PrivateHttpsPort
                or SettingsField.PublicHttpsPort or SettingsField.PublicPathPrefix))
        {
            refusal = MappingRefusal.InvalidSettings;
            return null;
        }

        refusal = MappingRefusal.None;
        var action = mode == ConnectionMode.Public ? MappingAction.ApplyPublic : MappingAction.ApplyPrivate;
        var command = mode == ConnectionMode.Public
            ? MappingCommand.ApplyPublic(settings.PublicHttpsPort, settings.PublicPathPrefix, settings.RelayPort)
            : MappingCommand.ApplyPrivate(settings.PrivateHttpsPort, settings.RelayPort);
        return new MappingPlan(
            action,
            command,
            MappingTexts.Effect(action, Address(dnsName, command.HttpsPort, command.Path), settings.RelayPort),
            Entry: null);
    }

    public MappingPlan PlanRemoval(OwnedMapping entry, string? dnsName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var action = entry.Mode == ConnectionMode.Public ? MappingAction.RemovePublic : MappingAction.RemovePrivate;
        var command = entry.Mode == ConnectionMode.Public
            ? MappingCommand.RemovePublic(entry.HttpsPort, entry.Path)
            : MappingCommand.RemovePrivate(entry.HttpsPort);
        return new MappingPlan(
            action,
            command,
            MappingTexts.Effect(action, Address(dnsName, entry.HttpsPort, entry.Path), relayPort: 0),
            entry);
    }

    public Task<MappingResult> ExecuteAsync(MappingPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new MappingResult(
            MappingOutcome.Refused,
            Unavailable,
            OthersChanged: false,
            _settings.Load().Settings,
            After: null));
    }

    private MappingRefusal Unavailable => _tailscale.ExecutablePath is null
        ? MappingRefusal.TailscaleNotInstalled
        : MappingRefusal.StatusUnavailable;

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
