using System.Threading.Channels;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Hosting;

/// <summary>
/// One relay instance per start (architecture 3.3 and 3.11), with the step order of W-design 4.16.
/// Thread-safe; Start, Stop and ResetStoredPairing are serialised. No member ever runs a Tailscale write
/// command: the only mapping call made here is the read-only inspection.
/// </summary>
internal sealed class RelaySupervisor : IRelaySupervisor
{
    private readonly SupervisorOptions _options;
    private readonly RelayOverrides _overrides;
    private readonly ISettingsStore _store;
    private readonly IPrerequisiteChecker _prerequisites;
    private readonly IMappingManager _mappings;
    private readonly IEndpointResolver _endpoints;
    private readonly IRelayFactory _relays;
    private readonly IHostFileSystem _files;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<HostEvent> _events = Channel.CreateBounded<HostEvent>(
        new BoundedChannelOptions(ProtocolConstants.EventChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private volatile SupervisorState _state = SupervisorState.Stopped;
    private volatile IRelay? _relay;
    private volatile RelayOptions? _runningOptions;
    private volatile RelaySettings? _settings;
    private Task _pump = Task.CompletedTask;
    private bool _disposed;

    public RelaySupervisor(
        SupervisorOptions options,
        ISettingsStore settings,
        IPrerequisiteChecker prerequisites,
        IMappingManager mappings,
        IEndpointResolver endpoints,
        IRelayFactory relays,
        IHostFileSystem files,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(prerequisites);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(clock);

        _options = options;
        _overrides = options.Overrides ?? new RelayOverrides();
        _store = settings;
        _prerequisites = prerequisites;
        _mappings = mappings;
        _endpoints = endpoints;
        _relays = relays;
        _files = files;
        _clock = clock;
    }

    public SupervisorState State => _state;

    public IRelay? Relay => _relay;

    /// <summary>Loaded on first use, so that composing the hosting library does no I/O.</summary>
    public RelaySettings Settings => _settings ??= _store.Load().Settings;

    public RelayOptions? RunningOptions => _runningOptions;

    public ChannelReader<HostEvent> Events => _events.Reader;

    public async Task<StartResult> StartAsync(CancellationToken ct)
    {
        if (_state != SupervisorState.Stopped || !await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return new StartResult(StartOutcome.AlreadyRunning, null, null, null);
        }

        try
        {
            if (_state != SupervisorState.Stopped)
            {
                return new StartResult(StartOutcome.AlreadyRunning, null, null, null);
            }

            try
            {
                return await StartCoreAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Cancellation, or a relay that threw something unexpected: nothing is left running.
                await DropRelayAsync().ConfigureAwait(false);
                _state = SupervisorState.Stopped;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var relay = _relay;
            if (relay is null)
            {
                return;
            }

            _state = SupervisorState.Stopping;
            try
            {
                await relay.StopAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                await DropRelayAsync().ConfigureAwait(false);
                _state = SupervisorState.Stopped;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PrerequisiteReport> CheckAsync(CancellationToken ct)
    {
        var report = await _prerequisites.CheckAsync(CheckOptions(), ct).ConfigureAwait(false);
        Publish(new PrerequisitesChecked(_clock.GetUtcNow(), report));
        return report;
    }

    public async Task<ConnectionInspection> InspectConnectionAsync(CancellationToken ct)
    {
        var inspection = await _mappings.InspectAsync(Settings, ct).ConfigureAwait(false);
        Publish(new ConnectionInspected(_clock.GetUtcNow(), inspection));
        return inspection;
    }

    public void SaveSettings(RelaySettings settings)
    {
        _store.Save(settings);
        _settings = _store.Load().Settings;
    }

    public async Task ResetStoredPairingAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_state != SupervisorState.Stopped)
            {
                throw new InvalidOperationException("The stored pairing can be reset only while the relay is stopped.");
            }

            await PairingMaintenance.ResetStoredPairingAsync(_options.DataDirectory, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _events.Writer.TryComplete();
    }

    private PrerequisiteOptions CheckOptions() => new(
        _options.DataDirectory,
        _overrides.CodexPath,
        SkipTailscale: _overrides.Endpoint is not null);

    private async Task<StartResult> StartCoreAsync(CancellationToken ct)
    {
        // Step 1.
        _state = SupervisorState.Checking;
        Publish(new CheckingStarted(_clock.GetUtcNow()));

        // Step 2.
        var report = await _prerequisites.CheckAsync(CheckOptions(), ct).ConfigureAwait(false);
        var settings = report.SettingsLoad.Settings;
        _settings = settings;
        Publish(new PrerequisitesChecked(_clock.GetUtcNow(), report));

        // Step 3.
        if (report.FirstFailure is { } failure)
        {
            _state = SupervisorState.Stopped;
            return new StartResult(StartOutcome.PrerequisiteFailed, report, null, PrerequisiteTexts.Failure(failure.Id));
        }

        // Step 4.
        var port = _overrides.Port ?? settings.RelayPort;
        var mode = _overrides.Mode ?? settings.ConnectionMode;
        var endpoint = _overrides.Endpoint
            ?? (report.DnsName is { } dnsName
                ? _endpoints.Resolve(settings with { ConnectionMode = mode }, dnsName)
                : null);
        if (endpoint is null)
        {
            return FaultWithoutRelay(report, RelayFaultCode.InvalidEndpoint, port);
        }

        if (port == RelaySettings.ForbiddenPort)
        {
            // The old relay's port never reaches a relay, whatever asked for it.
            return FaultWithoutRelay(report, RelayFaultCode.StartFailed, port);
        }

        var options = new RelayOptions(
            Port: port,
            AdvertisedEndpoint: endpoint,
            Mode: mode,
            DataDirectory: _options.DataDirectory,
            CodexExecutablePath: report.CodexExecutablePath,
            LabFolder: _overrides.LabFolder ?? LabFolderResolver.Resolve(settings, _files),
            LabPort: settings.AsrLabPort,
            RelayVersion: _options.RelayVersion);

        // Step 5. The events a relay publishes while it starts are forwarded as soon as its start
        // returns, in order and before anything else; from then on the pump forwards them.
        _state = SupervisorState.Starting;
        var relay = _relays.Create(options);
        _relay = relay;
        Publish(new RelayStarting(_clock.GetUtcNow(), options));

        // Step 6.
        try
        {
            await relay.StartAsync(ct).ConfigureAwait(false);
        }
        catch (RelayStartException exception)
        {
            ForwardWhatIsQueued(relay);
            await DropRelayAsync().ConfigureAwait(false);
            return Fault(report, exception.Code, port);
        }

        ForwardWhatIsQueued(relay);
        _pump = Task.Run(() => PumpAsync(relay), CancellationToken.None);

        // Step 7.
        _runningOptions = options;
        _state = SupervisorState.Running;

        // Step 8. Read-only; skipped when the endpoint was given, because no mapping is involved then.
        if (_overrides.Endpoint is null)
        {
            var inspection = await _mappings.InspectAsync(settings, ct).ConfigureAwait(false);
            Publish(new ConnectionInspected(_clock.GetUtcNow(), inspection));
        }

        return new StartResult(StartOutcome.Started, report, null, null);
    }

    /// <summary>
    /// A start that fails before any relay exists. Hosts follow events, so the fault is published in the
    /// shape a relay's own start fault has.
    /// </summary>
    private StartResult FaultWithoutRelay(PrerequisiteReport report, RelayFaultCode code, int port)
    {
        var now = _clock.GetUtcNow();
        Publish(new RelayEventForwarded(now, new RelayFaulted(now, code, PrerequisiteTexts.StartFault(code, port))));
        return Fault(report, code, port);
    }

    private StartResult Fault(PrerequisiteReport report, RelayFaultCode code, int port)
    {
        _state = SupervisorState.Stopped;
        return new StartResult(StartOutcome.RelayFault, report, code, PrerequisiteTexts.StartFault(code, port));
    }

    /// <summary>Disposes the current instance, if any, after its remaining events have been forwarded.</summary>
    private async Task DropRelayAsync()
    {
        var relay = _relay;
        _relay = null;
        _runningOptions = null;
        if (relay is null)
        {
            return;
        }

        await relay.DisposeAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _pump = Task.CompletedTask;
        ForwardWhatIsQueued(relay);
    }

    private void ForwardWhatIsQueued(IRelay relay)
    {
        while (relay.Events.TryRead(out var relayEvent))
        {
            Forward(relay, relayEvent);
        }
    }

    private async Task PumpAsync(IRelay relay)
    {
        var events = relay.Events;
        while (await events.WaitToReadAsync().ConfigureAwait(false))
        {
            while (events.TryRead(out var relayEvent))
            {
                Forward(relay, relayEvent);
            }
        }
    }

    private void Forward(IRelay relay, RelayEvent relayEvent)
    {
        Publish(new RelayEventForwarded(relayEvent.At, relayEvent));

        // A relay that stopped by itself is disposed here; nothing restarts it.
        if (relayEvent is RelayStopped { Reason: RelayStopReason.Fault })
        {
            _ = Task.Run(() => RetireFaultedRelayAsync(relay), CancellationToken.None);
        }
    }

    private async Task RetireFaultedRelayAsync(IRelay relay)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_relay, relay))
            {
                await DropRelayAsync().ConfigureAwait(false);
                _state = SupervisorState.Stopped;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Publish(HostEvent hostEvent) => _events.Writer.TryWrite(hostEvent);
}
