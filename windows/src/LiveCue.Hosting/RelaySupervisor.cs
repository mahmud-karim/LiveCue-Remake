using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Hosting;

/// <summary>What a host may override for one run, on top of the saved settings (console host arguments, tests).</summary>
public sealed record SupervisorOverrides
{
    /// <summary>Relay port for this run. 0 asks the system for a free port and is for tests only.</summary>
    public int? Port { get; init; }

    /// <summary>Advertised endpoint for this run, already canonical. Skips endpoint resolution.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Connection mode for this run.</summary>
    public ConnectionMode? Mode { get; init; }
}

/// <summary>
/// Runs the relay for a host (architecture 3.3): composes <see cref="RelayOptions"/> from the saved
/// settings, creates and starts an <see cref="IRelay"/>, forwards its events and stops it. One relay
/// instance per start; a restarted relay is a new instance.
///
/// The desktop window and the console host both drive the relay through this class and nothing else.
/// </summary>
public sealed class RelaySupervisor : IAsyncDisposable
{
    private readonly ISettingsStore _settings;
    private readonly string _dataDirectory;
    private readonly Func<RelayOptions, IRelay> _createRelay;
    private readonly SupervisorOverrides _overrides;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IRelay? _relay;
    private Task _pump = Task.CompletedTask;
    private int _delivering;

    /// <param name="settings">Where the settings come from.</param>
    /// <param name="dataDirectory">The data folder handed to the relay.</param>
    /// <param name="createRelay">Makes the relay for one run. Defaults to the real one; the self-test passes a scripted one.</param>
    /// <param name="overrides">Per-run overrides, or null.</param>
    /// <param name="clock">The clock for diagnostics raised here.</param>
    public RelaySupervisor(
        ISettingsStore settings,
        string dataDirectory,
        Func<RelayOptions, IRelay>? createRelay = null,
        SupervisorOverrides? overrides = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        _settings = settings;
        _dataDirectory = dataDirectory;
        _createRelay = createRelay ?? (options => RelayFactory.Create(options));
        _overrides = overrides ?? new SupervisorOverrides();
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Every relay event, in order, on a thread-pool thread. A handler that throws does not stop later
    /// events: the failure is reported to the handlers as a <see cref="Diagnostic"/>.
    /// </summary>
    public event EventHandler<RelayEvent>? EventReceived;

    /// <summary>True while a relay instance exists and is running.</summary>
    public bool IsRunning => _relay?.Status == RelayStatus.Running;

    /// <summary>The loopback port the relay is listening on; 0 while it is not running.</summary>
    public int BoundPort => _relay?.BoundPort ?? 0;

    /// <summary>The options of the current or most recent run.</summary>
    public RelayOptions? Options { get; private set; }

    /// <summary>What <see cref="ISettingsStore.Load"/> returned at the most recent start.</summary>
    public SettingsLoadResult? LoadedSettings { get; private set; }

    /// <summary>
    /// Loads the settings, creates a relay and starts it. Returns after the events of the start
    /// (<see cref="RelayStarted"/>, or <see cref="RelayFaulted"/>) have been handed to the handlers.
    /// </summary>
    /// <exception cref="RelayStartException">The relay could not start.</exception>
    /// <exception cref="InvalidOperationException">A relay is already running.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_relay is not null)
            {
                throw new InvalidOperationException("The relay is already running.");
            }

            LoadedSettings = _settings.Load();
            Options = Compose(LoadedSettings.Settings);

            var relay = _createRelay(Options);
            _relay = relay;
            _pump = Task.Run(() => PumpAsync(relay), CancellationToken.None);
            try
            {
                await relay.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Disposing completes the event channel, so the pump delivers what is left (the fault) and ends.
                _relay = null;
                await relay.DisposeAsync().ConfigureAwait(false);
                await _pump.ConfigureAwait(false);
                throw;
            }

            await WhenDeliveredAsync(relay).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops the relay and returns after <see cref="RelayStopped"/> has been handed to the handlers.
    /// Does nothing when no relay is running.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var relay = _relay;
            if (relay is null)
            {
                return;
            }

            _relay = null;
            try
            {
                await relay.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await relay.DisposeAsync().ConfigureAwait(false);
                await _pump.ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Completes when every event raised so far has been handed to the handlers.</summary>
    public Task WhenIdleAsync() => _relay is { } relay ? WhenDeliveredAsync(relay) : _pump;

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private RelayOptions Compose(HostSettings settings) => new(
        Port: _overrides.Port ?? settings.RelayPort,
        AdvertisedEndpoint: _overrides.Endpoint
            ?? EndpointResolver.Resolve(
                settings with { ConnectionMode = _overrides.Mode ?? settings.ConnectionMode },
                EndpointResolver.PlaceholderDnsName),
        Mode: _overrides.Mode ?? settings.ConnectionMode,
        DataDirectory: _dataDirectory,
        CodexExecutablePath: settings.CodexExecutablePath,
        LabFolder: settings.AsrLabFolder,
        LabPort: settings.AsrLabPort,
        RelayVersion: ProtocolConstants.RelayVersion);

    private async Task PumpAsync(IRelay relay)
    {
        var events = relay.Events;
        while (await events.WaitToReadAsync().ConfigureAwait(false))
        {
            Volatile.Write(ref _delivering, 1);
            try
            {
                while (events.TryRead(out var relayEvent))
                {
                    Deliver(relayEvent);
                }
            }
            finally
            {
                Volatile.Write(ref _delivering, 0);
            }
        }
    }

    private void Deliver(RelayEvent relayEvent)
    {
        try
        {
            EventReceived?.Invoke(this, relayEvent);
        }
#pragma warning disable CA1031 // A handler's failure must never stop the events that follow it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (relayEvent is Diagnostic)
            {
                return;
            }

            // Only the type name: exception text can carry paths and other things that are never shown.
            Deliver(new Diagnostic(
                _clock.GetUtcNow(),
                "host",
                $"An event handler failed ({exception.GetType().Name})."));
        }
    }

    private async Task WhenDeliveredAsync(IRelay relay)
    {
        var events = relay.Events;
        while (!_pump.IsCompleted && ((events.CanCount && events.Count > 0) || Volatile.Read(ref _delivering) == 1))
        {
            await Task.Delay(1).ConfigureAwait(false);
        }
    }
}
