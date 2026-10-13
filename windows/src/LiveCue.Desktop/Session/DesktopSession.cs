using System.ComponentModel;
using System.Threading.Channels;
using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.Text;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.Session;

/// <summary>
/// The desktop's one model of the supervisor and the relay (W-design 5.4).
///
/// Threads (W-design 5.11). One background task reads the supervisor's events and posts each to the UI
/// thread, one at a time and in order. Every command runs its supervisor or relay call on the thread
/// pool and applies the outcome on the UI thread, and it does so through the same task that posts the
/// events: the outcome of a call is applied only after every event the call published has been
/// handled. Without that order a start that the supervisor refuses could be shown before its own
/// "checking" event and then be overwritten by it. So all state lives on the UI thread, nothing here
/// blocks it, and the relay never waits for the window.
///
/// Failures. Every event is handled inside one try: an exception from the session or from any
/// subscriber becomes one "Relay diagnostic: " message and the next event is processed normally
/// (WIN-10). A command never throws; a call that throws becomes "The desktop command failed. Please
/// retry.". Each command is single-flight: a second call while one runs returns at once.
/// </summary>
internal sealed class DesktopSession : IDesktopSession, IDisposable
{
    private readonly IRelaySupervisor _supervisor;
    private readonly IMappingManager _mappings;
    private readonly IEndpointResolver _endpoints;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stopReading = new();
    private readonly Channel<Action> _afterEvents = Channel.CreateUnbounded<Action>();
    private readonly RelayStatusMachine _machine = new();
    private readonly PresenceTracker _presence = new();
    private readonly ITimer _presenceTimer;

    // What the observable properties show. Everything below is touched on the UI thread only.
    private string? _stateText;
    private RelayFaultCode? _faultCode;
    private string? _advertisedEndpoint;
    private PrerequisiteReport? _prerequisites;
    private ConnectionInspection? _connection;
    private RelaySettings? _settings;
    private SettingsLoadResult? _settingsLoad;
    private Shown _shown = Shown.Initial;

    private int _startingPort;
    private bool _showQrWhenReady;
    private bool _containmentWarned;
    private (string? Version, string? CheckedVersion) _containmentWarnedFor;
    private string? _containmentWarningForThisStart;
    private Exception? _failure;
    private bool _disposed;

    // Single-flight flags, one per command.
    private bool _starting;
    private bool _stopping;
    private bool _togglingPause;
    private bool _checking;
    private bool _showingPairing;
    private bool _resettingPairing;
    private bool _executingMapping;

    public DesktopSession(
        IRelaySupervisor supervisor,
        IMappingManager mappings,
        IEndpointResolver endpoints,
        IUiDispatcher ui,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(clock);

        _supervisor = supervisor;
        _mappings = mappings;
        _endpoints = endpoints;
        _ui = ui;
        _clock = clock;
        _presenceTimer = clock.CreateTimer(
            _ => _ui.Post(OnPresenceTick), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _ = Task.Run(() => PumpAsync(supervisor.Events, _stopReading.Token));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<RelayEvent>? RelayEventReceived;

    public event EventHandler<PairingQr>? PairingQrProduced;

    public event EventHandler<SessionSignal>? Signal;

    public event EventHandler<SessionMessage>? MessageRaised;

    public RelayUiState State => _machine.State;

    public string? StateText => _stateText;

    public RelayFaultCode? FaultCode => _faultCode;

    public bool IsRelayRunning => _machine.IsRunning;

    public bool IsPaused => _machine.IsPaused;

    public PhonePresence Presence => _presence.Presence;

    public DateTimeOffset? LastPhoneSeen => _presence.LastSeen;

    public string? AdvertisedEndpoint => _advertisedEndpoint;

    public PrerequisiteReport? Prerequisites => _prerequisites;

    public ConnectionInspection? Connection => _connection;

    public bool IsConnectionReady =>
        IsRelayRunning
        && _connection?.For(Settings.ConnectionMode).State is MappingState.Present or MappingState.PresentNotOwned;

    /// <summary>
    /// The settings as last loaded or saved. Before the first check or save this is what the supervisor
    /// loads on first use; afterwards the session keeps the newest copy itself, because a mapping
    /// command saves the ledger without the supervisor seeing it.
    /// </summary>
    public RelaySettings Settings => _settings ?? _supervisor.Settings;

    public SettingsLoadResult SettingsLoad =>
        _settingsLoad ?? new SettingsLoadResult(Settings, SettingsFileState.Missing, []);

    public bool SettingsDifferFromRunning
    {
        get
        {
            if (!IsRelayRunning || _supervisor.RunningOptions is not { } running)
            {
                return false;
            }

            var settings = Settings;
            if (running.Port != settings.RelayPort || running.Mode != settings.ConnectionMode)
            {
                return true;
            }

            // The address the saved settings would advertise, when this PC's name is known.
            return _prerequisites?.DnsName is { } dnsName
                && _endpoints.Resolve(settings, dnsName) is { } expected
                && !string.Equals(expected, running.AdvertisedEndpoint, StringComparison.Ordinal);
        }
    }

    // ---- Commands ---------------------------------------------------------------------------------

    public Task StartAsync()
    {
        if (_starting || _disposed)
        {
            return Task.CompletedTask;
        }

        _starting = true;
        return CallAsync(
            () => _supervisor.StartAsync(CancellationToken.None),
            (result, failure) =>
            {
                _starting = false;
                ApplyStartResult(result, failure);
                return true;
            },
            whenBroken: true);
    }

    public Task StopAsync()
    {
        if (_stopping)
        {
            return Task.CompletedTask;
        }

        _stopping = true;
        return CallAsync(
            async () =>
            {
                await _supervisor.StopAsync(CancellationToken.None).ConfigureAwait(false);
                return true;
            },
            (_, failure) =>
            {
                _stopping = false;
                ReportCommandFailure(failure);
                return true;
            },
            whenBroken: true);
    }

    public Task TogglePauseAsync()
    {
        if (_togglingPause || !IsRelayRunning || _supervisor.Relay is not { } relay)
        {
            return Task.CompletedTask;
        }

        _togglingPause = true;
        var resume = IsPaused;
        return CallAsync(
            async () =>
            {
                await relay.SetAcceptingAsync(resume).ConfigureAwait(false);
                return true;
            },
            (_, failure) =>
            {
                // The state changes when the relay confirms with AcceptingChanged, not here.
                _togglingPause = false;
                ReportCommandFailure(failure);
                return true;
            },
            whenBroken: true);
    }

    public Task CheckAgainAsync()
    {
        if (_checking || _starting)
        {
            return Task.CompletedTask;
        }

        _checking = true;
        return CallAsync(
            async () =>
            {
                await _supervisor.CheckAsync(CancellationToken.None).ConfigureAwait(false);
                await _supervisor.InspectConnectionAsync(CancellationToken.None).ConfigureAwait(false);
                return true;
            },
            (_, failure) =>
            {
                _checking = false;
                ReportCommandFailure(failure);
                return true;
            },
            whenBroken: true);
    }

    public Task ShowPairingAsync()
    {
        if (_showingPairing || !IsConnectionReady || _supervisor.Relay is not { } relay)
        {
            return Task.CompletedTask;
        }

        _showingPairing = true;
        return CallAsync(
            () => relay.ShowPairingAsync(CancellationToken.None),
            (qr, failure) =>
            {
                _showingPairing = false;
                ReportCommandFailure(failure);
                if (qr is not null)
                {
                    Note(SafeInvoke.Each(PairingQrProduced, this, qr));
                }

                return true;
            },
            whenBroken: true);
    }

    public Task<bool> ResetPairingAsync()
    {
        if (_resettingPairing)
        {
            return Task.FromResult(false);
        }

        _resettingPairing = true;
        if (IsRelayRunning && _supervisor.Relay is { } relay)
        {
            return CallAsync(
                () => relay.ResetPairingAsync(CancellationToken.None),
                (qr, failure) =>
                {
                    _resettingPairing = false;
                    ReportCommandFailure(failure);
                    if (qr is null)
                    {
                        return false;
                    }

                    Note(SafeInvoke.Each(PairingQrProduced, this, qr));
                    return true;
                },
                whenBroken: false);
        }

        // No relay runs: the stored pairing is replaced. This is also the recovery from a pairing file
        // that cannot be read (R-17). The new QR is shown after the next start.
        return CallAsync(
            async () =>
            {
                await _supervisor.ResetStoredPairingAsync(CancellationToken.None).ConfigureAwait(false);
                return true;
            },
            (_, failure) =>
            {
                _resettingPairing = false;
                if (failure is not null)
                {
                    ReportCommandFailure(failure);
                    return false;
                }

                if (_faultCode == RelayFaultCode.PairingStoreUnreadable)
                {
                    _faultCode = null;
                    _stateText = null;
                    _machine.FaultCleared();
                }

                _showQrWhenReady = true;
                Commit();
                Raise(SessionMessageKind.Info, ShellText.PairingReset);
                return true;
            },
            whenBroken: false);
    }

    public MappingPlan? PlanSetUp(out MappingRefusal refusal) =>
        _mappings.PlanApply(Settings.ConnectionMode, Settings, _prerequisites?.DnsName, out refusal);

    public MappingPlan PlanRemoval(OwnedMapping entry) => _mappings.PlanRemoval(entry, _prerequisites?.DnsName);

    public Task<MappingResult> ExecuteMappingAsync(MappingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_executingMapping)
        {
            return Task.FromResult(NothingRun(MappingOutcome.Refused));
        }

        _executingMapping = true;
        return CallAsync(
            () => _mappings.ExecuteAsync(plan, CancellationToken.None),
            (result, failure) =>
            {
                _executingMapping = false;
                if (result is null)
                {
                    ReportCommandFailure(failure);
                    return NothingRun(MappingOutcome.CommandFailed);
                }

                // The ledger was saved by the manager, and its inspection is newer than ours. Storing it
                // can make the connection ready, which shows the first QR by itself (R-16).
                AdoptSettings(result.Settings);
                if (result.After is { } after)
                {
                    _connection = after;
                }

                Commit();
                if (ShellText.MappingOutcome(plan, result) is { } text)
                {
                    Raise(MessageKindOf(result.Outcome), text);
                }

                if (result.OthersChanged)
                {
                    Raise(SessionMessageKind.Warning, ShellText.MappingOthersChanged);
                }

                return result;
            },
            whenBroken: NothingRun(MappingOutcome.CommandFailed));
    }

    public IReadOnlyList<SettingsProblem> SaveSettings(RelaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            _supervisor.SaveSettings(settings);
        }
        catch (SettingsValidationException exception)
        {
            return exception.Problems;
        }
#pragma warning disable CA1031 // A command never throws: nothing was saved, and the activity says so.
        catch (Exception)
#pragma warning restore CA1031
        {
            Raise(SessionMessageKind.Error, ShellText.CommandFailed);
            return [];
        }

        var saved = _supervisor.Settings;
        _settings = saved;
        _settingsLoad = new SettingsLoadResult(saved, SettingsFileState.Loaded, []);
        Commit();
        ReportFailure();
        return [];
    }

    public void ClearActivity()
    {
        Note(SafeInvoke.Each(Signal, this, SessionSignal.ActivityCleared));
        ReportFailure();
    }

    // ---- For the window ---------------------------------------------------------------------------

    /// <summary>Adds one activity line of the desktop itself. UI thread.</summary>
    internal void Report(SessionMessageKind kind, string text) => Raise(kind, text);

    /// <summary>
    /// An exception reached the dispatcher (W-design 5.12): it becomes one "Relay diagnostic: " line,
    /// with the type name only, and the app continues.
    /// </summary>
    internal void ReportUnhandled(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Raise(SessionMessageKind.Error, ShellText.UnexpectedError(exception));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopReading.Cancel();
        _stopReading.Dispose();
        _presenceTimer.Dispose();
    }

    // ---- The pump: events and command outcomes, in order, to the UI thread ------------------------

    /// <summary>
    /// Reads the supervisor's events and posts each to the UI thread. An action handed to
    /// <see cref="AfterEventsSoFar"/> is posted after every event that was in the channel when it was
    /// handed over: the actions waiting are taken first, then the channel is emptied, then they are posted.
    /// </summary>
    private async Task PumpAsync(ChannelReader<HostEvent> events, CancellationToken stop)
    {
        var waiting = new List<Action>();
        Task<bool>? nextEvent = null;
        Task<bool>? nextAction = null;
        var eventsOpen = true;
        try
        {
            while (true)
            {
                nextAction ??= _afterEvents.Reader.WaitToReadAsync(stop).AsTask();
                if (eventsOpen)
                {
                    nextEvent ??= events.WaitToReadAsync(stop).AsTask();
                    await Task.WhenAny(nextEvent, nextAction).ConfigureAwait(false);
                }
                else
                {
                    await nextAction.ConfigureAwait(false);
                }

                if (nextAction.IsCompleted)
                {
                    await nextAction.ConfigureAwait(false);
                    nextAction = null;
                }

                while (_afterEvents.Reader.TryRead(out var action))
                {
                    waiting.Add(action);
                }

                if (nextEvent is { IsCompleted: true })
                {
                    // False: the supervisor was disposed and no event will follow. Commands still complete.
                    eventsOpen = await nextEvent.ConfigureAwait(false);
                    nextEvent = null;
                }

                while (events.TryRead(out var hostEvent))
                {
                    var current = hostEvent;
                    _ui.Post(() => Handle(current));
                }

                foreach (var action in waiting)
                {
                    _ui.Post(action);
                }

                waiting.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // The session was disposed.
        }
        finally
        {
            // Whatever still waits is released, so that no command stays pending for ever.
            _afterEvents.Writer.TryComplete();
            while (_afterEvents.Reader.TryRead(out var action))
            {
                waiting.Add(action);
            }

            foreach (var action in waiting)
            {
                _ui.Post(action);
            }
        }
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread after every event published so far was handled.</summary>
    private void AfterEventsSoFar(Action action)
    {
        if (!_afterEvents.Writer.TryWrite(action))
        {
            _ui.Post(action);
        }
    }

    /// <summary>
    /// One command: <paramref name="offUi"/> on the thread pool, then <paramref name="onUi"/> on the UI
    /// thread with what it returned or threw, after the events it published. The task completes with
    /// what <paramref name="onUi"/> returns, or with <paramref name="whenBroken"/> if that threw, and
    /// never faults.
    /// </summary>
    private Task<TResult> CallAsync<T, TResult>(Func<Task<T>> offUi, Func<T?, Exception?, TResult> onUi, TResult whenBroken)
    {
        var done = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            T? value = default;
            Exception? failure = null;
            try
            {
                value = await offUi().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A command never throws: the failure is handed to the UI thread.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                failure = exception;
            }

            AfterEventsSoFar(() =>
            {
                var result = whenBroken;
                Note(SafeInvoke.Run(() => result = onUi(value, failure)));
                ReportFailure();
                done.TrySetResult(result);
            });
        });
        return done.Task;
    }

    // ---- Events to state (the table of W-design 5.4) ----------------------------------------------

    private void Handle(HostEvent hostEvent)
    {
        Note(SafeInvoke.Run(() => Apply(hostEvent)));
        ReportFailure();
    }

    private void Apply(HostEvent hostEvent)
    {
        switch (hostEvent)
        {
            case CheckingStarted:
                // A fault text stays until the next start; this is the next start.
                _faultCode = null;
                _stateText = null;
                _machine.CheckingStarted();
                Commit();
                break;

            case PrerequisitesChecked checkedNow:
                Apply(checkedNow.Report);
                break;

            case RelayStarting starting:
                _startingPort = starting.Options.Port;
                _machine.RelayStarting();
                Commit();
                Note(SafeInvoke.Each(Signal, this, SessionSignal.RelayStarting));

                // The signal clears the activity log; the warning of this start belongs in the new one.
                RaiseContainmentWarningOfThisStart();
                break;

            case ConnectionInspected inspected:
                _connection = inspected.Inspection;
                Commit();
                break;

            case RelayEventForwarded forwarded:
                var ended = Apply(forwarded.Event);
                Commit();
                if (ended)
                {
                    Note(SafeInvoke.Each(Signal, this, SessionSignal.RelayEnded));
                }

                Note(SafeInvoke.Each(RelayEventReceived, this, forwarded.Event));
                break;

            default:
                break;
        }
    }

    private void Apply(PrerequisiteReport report)
    {
        var duringStart = _machine.State == RelayUiState.Checking;
        var wasFailed = _machine.State == RelayUiState.PrerequisiteFailed;

        _prerequisites = report;
        _settingsLoad = report.SettingsLoad;
        AdoptSettings(report.SettingsLoad.Settings);
        _machine.PrerequisitesChecked(report.Passed);
        if (_machine.State == RelayUiState.PrerequisiteFailed && report.FirstFailure is { } failure)
        {
            _faultCode = null;
            _stateText = PrerequisiteTexts.Failure(failure.Id);
        }
        else if (wasFailed)
        {
            _stateText = null;
        }

        Commit();

        // One warning per changed pair of versions in this run of the app (architecture 3.7).
        var versions = (report.CodexVersion, report.SettingsLoad.Settings.CodexContainmentCheckedVersion);
        if (report.ContainmentCheckIsStale && !(_containmentWarned && versions == _containmentWarnedFor))
        {
            _containmentWarned = true;
            _containmentWarnedFor = versions;
            var text = PrerequisiteTexts.ContainmentWarning(versions.CodexVersion, versions.CodexContainmentCheckedVersion);
            if (duringStart && report.Passed)
            {
                _containmentWarningForThisStart = text;
            }
            else
            {
                Raise(SessionMessageKind.Warning, text);
            }
        }
    }

    /// <summary>Returns true when the relay ended and the pages must take the stopped layout.</summary>
    private bool Apply(RelayEvent relayEvent)
    {
        switch (relayEvent)
        {
            case RelayStarted started:
                _advertisedEndpoint = started.Endpoint;
                _presence.Reset();
                _machine.RelayStarted();
                _showQrWhenReady |= started.PairingWasCreated;
                _presenceTimer.Change(ProtocolConstants.DesktopTick, ProtocolConstants.DesktopTick);
                return false;

            case AcceptingChanged changed:
                _machine.AcceptingChanged(changed.Accepting);
                return false;

            case PhoneSeen:
                if (_machine.IsRunning)
                {
                    _presence.Seen(_clock.GetUtcNow());
                }

                return false;

            case RequestStarted:
                _machine.RequestStarted();
                return false;

            case RequestCompleted:
                _machine.RequestCompleted();
                return false;

            case RequestFailed:
                _machine.RequestFailed();
                return false;

            case RelayFaulted faulted:
                _faultCode = faulted.Code;
                _stateText = faulted.Code == RelayFaultCode.Internal
                    ? StatusTexts.RelayStoppedByItself
                    : PrerequisiteTexts.StartFault(faulted.Code, _startingPort);
                _machine.Faulted();
                ForgetPhone();
                return false;

            case RelayStopped stopped:
                _machine.RelayStopped(stopped.Reason);
                if (stopped.Reason == RelayStopReason.Requested)
                {
                    _faultCode = null;
                    _stateText = null;
                }
                else if (_stateText is null)
                {
                    // A relay that ended by itself without saying why.
                    _faultCode ??= RelayFaultCode.Internal;
                    _stateText = StatusTexts.RelayStoppedByItself;
                }

                ForgetPhone();
                return true;

            default:
                return false;
        }
    }

    /// <summary>The outcome of a start, applied after the events of that start.</summary>
    private void ApplyStartResult(StartResult? result, Exception? failure)
    {
        if (result is null)
        {
            // The supervisor threw: nothing is left running, and the window must not stay "checking".
            ReportCommandFailure(failure);
            if (_machine.State is RelayUiState.Checking or RelayUiState.Starting)
            {
                _faultCode = RelayFaultCode.StartFailed;
                _stateText = PrerequisiteTexts.StartFault(RelayFaultCode.StartFailed, _startingPort);
                _machine.Faulted();
            }
        }
        else
        {
            switch (result.Outcome)
            {
                case StartOutcome.ExposedInPrivateMode:
                    // Check 6a: the Faulted layout with the supervisor's text and no fault code. The
                    // inspection that showed the exposure arrived just before and stays stored.
                    _faultCode = null;
                    _stateText = result.FailureText ?? StatusTexts.CouldNotStart;
                    _machine.Faulted();
                    break;

                case StartOutcome.RelayFault:
                    // The supervisor's text replaces what the RelayFaulted event set: this is how the
                    // second port-in-use text reaches the window.
                    _faultCode ??= result.Fault;
                    _stateText = result.FailureText ?? _stateText ?? StatusTexts.CouldNotStart;
                    if (_machine.State != RelayUiState.Faulted)
                    {
                        _machine.Faulted();
                    }

                    break;

                case StartOutcome.PrerequisiteFailed when _machine.State == RelayUiState.Checking:
                    _stateText = result.FailureText;
                    _machine.PrerequisitesChecked(passed: false);
                    break;

                default:
                    break;
            }
        }

        Commit();
        RaiseContainmentWarningOfThisStart();
    }

    private void OnPresenceTick()
    {
        if (_presence.Tick(_clock.GetUtcNow()))
        {
            Commit();
            ReportFailure();
        }
    }

    /// <summary>The relay does not run: nothing is known about a phone, and the tick stops.</summary>
    private void ForgetPhone()
    {
        _presence.Reset();
        _presenceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Takes newly loaded or saved settings, unless they are the ones already held.</summary>
    private void AdoptSettings(RelaySettings settings)
    {
        if (_settings is null || !_settings.SameValues(settings))
        {
            _settings = settings;
        }
    }

    private void RaiseContainmentWarningOfThisStart()
    {
        if (_containmentWarningForThisStart is { } text)
        {
            _containmentWarningForThisStart = null;
            Raise(SessionMessageKind.Warning, text);
        }
    }

    // ---- Telling the observers --------------------------------------------------------------------

    /// <summary>
    /// Announces what changed since the last call. All fields are already set, so an observer that
    /// reads several properties in one handler sees one consistent state. When the connection has just
    /// become ready and a first QR is owed, it is asked for here (R-16).
    /// </summary>
    private void Commit()
    {
        var before = _shown;
        var now = new Shown(
            State,
            _stateText,
            _faultCode,
            IsRelayRunning,
            IsPaused,
            Presence,
            LastPhoneSeen,
            _advertisedEndpoint,
            _prerequisites,
            _connection,
            IsRelayRunning && IsConnectionReady,
            _settings,
            _settingsLoad,
            IsRelayRunning && SettingsDifferFromRunning);
        _shown = now;

        Changed(before.State != now.State, nameof(State));
        Changed(before.StateText != now.StateText, nameof(StateText));
        Changed(before.FaultCode != now.FaultCode, nameof(FaultCode));
        Changed(before.IsRelayRunning != now.IsRelayRunning, nameof(IsRelayRunning));
        Changed(before.IsPaused != now.IsPaused, nameof(IsPaused));
        Changed(before.Presence != now.Presence, nameof(Presence));
        Changed(before.LastPhoneSeen != now.LastPhoneSeen, nameof(LastPhoneSeen));
        Changed(before.AdvertisedEndpoint != now.AdvertisedEndpoint, nameof(AdvertisedEndpoint));
        Changed(!ReferenceEquals(before.Prerequisites, now.Prerequisites), nameof(Prerequisites));
        Changed(!ReferenceEquals(before.Settings, now.Settings), nameof(Settings));
        Changed(!ReferenceEquals(before.SettingsLoad, now.SettingsLoad), nameof(SettingsLoad));
        Changed(!ReferenceEquals(before.Connection, now.Connection), nameof(Connection));
        Changed(before.IsConnectionReady != now.IsConnectionReady, nameof(IsConnectionReady));
        Changed(before.SettingsDifferFromRunning != now.SettingsDifferFromRunning, nameof(SettingsDifferFromRunning));

        if (_showQrWhenReady && now.IsConnectionReady && !_showingPairing && _supervisor.Relay is not null)
        {
            _showQrWhenReady = false;
            _ = ShowPairingAsync();
        }
    }

    private void Changed(bool changed, string propertyName)
    {
        if (!changed || PropertyChanged is not { } handlers)
        {
            return;
        }

        var args = new PropertyChangedEventArgs(propertyName);
        foreach (var handler in handlers.GetInvocationList())
        {
            Note(SafeInvoke.Run(() => ((PropertyChangedEventHandler)handler)(this, args)));
        }
    }

    private void ReportCommandFailure(Exception? failure)
    {
        if (failure is not null)
        {
            Raise(SessionMessageKind.Error, ShellText.CommandFailed);
        }
    }

    /// <summary>Remembers the first thing that went wrong while one event or outcome was shown.</summary>
    private void Note(Exception? failure) => _failure ??= failure;

    /// <summary>One diagnostic line for whatever went wrong since the last call, with the type name only.</summary>
    private void ReportFailure()
    {
        if (_failure is { } failure)
        {
            _failure = null;
            Raise(SessionMessageKind.Error, ShellText.EventNotShown(failure));
        }
    }

    /// <summary>A subscriber of the messages that throws is not told about it with another message.</summary>
    private void Raise(SessionMessageKind kind, string text) =>
        _ = SafeInvoke.Each(MessageRaised, this, new SessionMessage(_clock.GetUtcNow(), kind, text));

    private MappingResult NothingRun(MappingOutcome outcome) =>
        new(outcome, MappingRefusal.None, OthersChanged: false, Settings, After: null);

    private static SessionMessageKind MessageKindOf(MappingOutcome outcome) => outcome switch
    {
        MappingOutcome.Done => SessionMessageKind.Info,
        MappingOutcome.CommandFailed or MappingOutcome.CommandTimedOut => SessionMessageKind.Error,
        _ => SessionMessageKind.Warning,
    };

    /// <summary>What the observers were last told.</summary>
    private sealed record Shown(
        RelayUiState State,
        string? StateText,
        RelayFaultCode? FaultCode,
        bool IsRelayRunning,
        bool IsPaused,
        PhonePresence Presence,
        DateTimeOffset? LastPhoneSeen,
        string? AdvertisedEndpoint,
        PrerequisiteReport? Prerequisites,
        ConnectionInspection? Connection,
        bool IsConnectionReady,
        RelaySettings? Settings,
        SettingsLoadResult? SettingsLoad,
        bool SettingsDifferFromRunning)
    {
        public static Shown Initial { get; } = new(
            RelayUiState.Stopped, null, null, false, false, PhonePresence.NotConnected, null, null, null, null, false, null, null, false);
    }
}
