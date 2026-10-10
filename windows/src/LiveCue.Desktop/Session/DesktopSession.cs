using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveCue.Desktop.Text;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Session;

/// <summary>
/// The desktop's one model of the supervisor and the relay (W-design 5.4). It reads the supervisor's
/// events on a background task, posts each to the UI thread, updates its own state there and then
/// raises its events. A handler that throws becomes one diagnostic line and never stops later events.
///
/// First version (foundation): it forwards Start, Stop and the other commands, and follows the event
/// table for the relay state, the pause flag and the endpoint. Presence only ever becomes "connected"
/// (there is no idle timer yet), no QR is shown by itself on a first start, and commands are not yet
/// guarded against overlapping. Work package WP-W08 builds the rest.
/// </summary>
internal sealed class DesktopSession : ObservableObject, IDesktopSession, IDisposable
{
    private readonly IRelaySupervisor _supervisor;
    private readonly IMappingManager _mappings;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stopReading = new();

    private RelayUiState _state = RelayUiState.Stopped;
    private string? _stateText;
    private RelayFaultCode? _faultCode;
    private bool _isPaused;
    private PhonePresence _presence = PhonePresence.NotConnected;
    private DateTimeOffset? _lastPhoneSeen;
    private string? _advertisedEndpoint;
    private PrerequisiteReport? _prerequisites;
    private ConnectionInspection? _connection;
    private SettingsLoadResult? _settingsLoad;
    private int _startingPort;

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
        _ui = ui;
        _clock = clock;
        _ = Task.Run(() => ReadEventsAsync(supervisor.Events, _stopReading.Token));
    }

    public event EventHandler<RelayEvent>? RelayEventReceived;

    public event EventHandler<PairingQr>? PairingQrProduced;

    public event EventHandler<SessionSignal>? Signal;

    public event EventHandler<SessionMessage>? MessageRaised;

    public RelayUiState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsRelayRunning));
                OnPropertyChanged(nameof(IsConnectionReady));
                OnPropertyChanged(nameof(SettingsDifferFromRunning));
            }
        }
    }

    public string? StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    public RelayFaultCode? FaultCode
    {
        get => _faultCode;
        private set => SetProperty(ref _faultCode, value);
    }

    public bool IsRelayRunning =>
        State is RelayUiState.Ready or RelayUiState.Paused or RelayUiState.Thinking or RelayUiState.RequestFailed;

    public bool IsPaused
    {
        get => _isPaused;
        private set => SetProperty(ref _isPaused, value);
    }

    public PhonePresence Presence
    {
        get => _presence;
        private set => SetProperty(ref _presence, value);
    }

    public DateTimeOffset? LastPhoneSeen
    {
        get => _lastPhoneSeen;
        private set => SetProperty(ref _lastPhoneSeen, value);
    }

    public string? AdvertisedEndpoint
    {
        get => _advertisedEndpoint;
        private set => SetProperty(ref _advertisedEndpoint, value);
    }

    public PrerequisiteReport? Prerequisites
    {
        get => _prerequisites;
        private set => SetProperty(ref _prerequisites, value);
    }

    public ConnectionInspection? Connection
    {
        get => _connection;
        private set
        {
            if (SetProperty(ref _connection, value))
            {
                OnPropertyChanged(nameof(IsConnectionReady));
            }
        }
    }

    public bool IsConnectionReady =>
        IsRelayRunning
        && Connection?.For(Settings.ConnectionMode).State is MappingState.Present or MappingState.PresentNotOwned;

    public RelaySettings Settings => _supervisor.Settings;

    public SettingsLoadResult SettingsLoad =>
        _settingsLoad ?? new SettingsLoadResult(Settings, SettingsFileState.Missing, []);

    public bool SettingsDifferFromRunning =>
        IsRelayRunning
        && _supervisor.RunningOptions is { } running
        && (running.Port != Settings.RelayPort || running.Mode != Settings.ConnectionMode);

    public Task StartAsync() => RunAsync(() => _supervisor.StartAsync(CancellationToken.None));

    public Task StopAsync() => RunAsync(() => _supervisor.StopAsync(CancellationToken.None));

    public Task TogglePauseAsync()
    {
        var relay = _supervisor.Relay;
        var resume = IsPaused;
        return relay is null ? Task.CompletedTask : RunAsync(() => relay.SetAcceptingAsync(resume));
    }

    public Task CheckAgainAsync() => RunAsync(async () =>
    {
        await _supervisor.CheckAsync(CancellationToken.None).ConfigureAwait(false);
        await _supervisor.InspectConnectionAsync(CancellationToken.None).ConfigureAwait(false);
    });

    public async Task ShowPairingAsync()
    {
        if (!IsConnectionReady || _supervisor.Relay is not { } relay)
        {
            return;
        }

        try
        {
            var qr = await Task.Run(() => relay.ShowPairingAsync(CancellationToken.None));
            PairingQrProduced?.Invoke(this, qr);
        }
#pragma warning disable CA1031 // A command never throws: the failure becomes an activity line.
        catch (Exception)
#pragma warning restore CA1031
        {
            Raise(SessionMessageKind.Error, ShellText.CommandFailed);
        }
    }

    public async Task<bool> ResetPairingAsync()
    {
        try
        {
            if (IsRelayRunning && _supervisor.Relay is { } relay)
            {
                var qr = await Task.Run(() => relay.ResetPairingAsync(CancellationToken.None));
                PairingQrProduced?.Invoke(this, qr);
                return true;
            }

            await Task.Run(() => _supervisor.ResetStoredPairingAsync(CancellationToken.None));
            if (FaultCode == RelayFaultCode.PairingStoreUnreadable)
            {
                FaultCode = null;
                StateText = null;
                State = RelayUiState.Stopped;
            }

            Raise(SessionMessageKind.Info, ShellText.PairingReset);
            return true;
        }
#pragma warning disable CA1031 // A command never throws: the failure becomes an activity line.
        catch (Exception)
#pragma warning restore CA1031
        {
            Raise(SessionMessageKind.Error, ShellText.CommandFailed);
            return false;
        }
    }

    public MappingPlan? PlanSetUp(out MappingRefusal refusal) =>
        _mappings.PlanApply(Settings.ConnectionMode, Settings, Prerequisites?.DnsName, out refusal);

    public MappingPlan PlanRemoval(OwnedMapping entry) => _mappings.PlanRemoval(entry, Prerequisites?.DnsName);

    public async Task<MappingResult> ExecuteMappingAsync(MappingPlan plan)
    {
        try
        {
            var result = await Task.Run(() => _mappings.ExecuteAsync(plan, CancellationToken.None));
            if (result.After is { } after)
            {
                Connection = after;
            }

            if (result.Outcome == MappingOutcome.Refused)
            {
                Raise(
                    SessionMessageKind.Warning,
                    MappingTexts.Refusal(result.Refusal, plan.Command.HttpsPort, plan.Command.Path));
            }

            OnPropertyChanged(nameof(Settings));
            return result;
        }
#pragma warning disable CA1031 // A command never throws: the failure becomes an activity line.
        catch (Exception)
#pragma warning restore CA1031
        {
            Raise(SessionMessageKind.Error, ShellText.CommandFailed);
            return new MappingResult(MappingOutcome.CommandFailed, MappingRefusal.None, OthersChanged: false, Settings, After: null);
        }
    }

    public IReadOnlyList<SettingsProblem> SaveSettings(RelaySettings settings)
    {
        try
        {
            _supervisor.SaveSettings(settings);
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(SettingsDifferFromRunning));
            OnPropertyChanged(nameof(IsConnectionReady));
            return [];
        }
        catch (SettingsValidationException exception)
        {
            return exception.Problems;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            // Nothing was saved. The page keeps its unsaved changes; the activity log says why.
            Raise(SessionMessageKind.Error, ShellText.CommandFailed);
            return [];
        }
    }

    public void ClearActivity() => Signal?.Invoke(this, SessionSignal.ActivityCleared);

    public void Dispose()
    {
        _stopReading.Cancel();
        _stopReading.Dispose();
    }

    /// <summary>Runs a supervisor or relay call off the UI thread. A failure becomes an activity line.</summary>
    private async Task RunAsync(Func<Task> call)
    {
        try
        {
            await Task.Run(call);
        }
#pragma warning disable CA1031 // A command never throws: the failure becomes an activity line.
        catch (Exception)
#pragma warning restore CA1031
        {
            Raise(SessionMessageKind.Error, ShellText.CommandFailed);
        }
    }

    private async Task ReadEventsAsync(ChannelReader<HostEvent> events, CancellationToken stop)
    {
        try
        {
            while (await events.WaitToReadAsync(stop).ConfigureAwait(false))
            {
                while (events.TryRead(out var hostEvent))
                {
                    var current = hostEvent;
                    _ui.Post(() => Handle(current));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The session was disposed.
        }
    }

    private void Handle(HostEvent hostEvent)
    {
        try
        {
            Apply(hostEvent);
        }
#pragma warning disable CA1031 // WIN-10: one event that cannot be shown never stops the events after it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Raise(SessionMessageKind.Error, ShellText.EventNotShown(exception));
        }
    }

    private void Apply(HostEvent hostEvent)
    {
        switch (hostEvent)
        {
            case CheckingStarted:
                FaultCode = null;
                StateText = null;
                State = RelayUiState.Checking;
                break;

            case PrerequisitesChecked checkedNow:
                Prerequisites = checkedNow.Report;
                _settingsLoad = checkedNow.Report.SettingsLoad;
                OnPropertyChanged(nameof(SettingsLoad));
                OnPropertyChanged(nameof(Settings));
                if (checkedNow.Report.FirstFailure is { } failure)
                {
                    if (!IsRelayRunning)
                    {
                        StateText = PrerequisiteTexts.Failure(failure.Id);
                        State = RelayUiState.PrerequisiteFailed;
                    }
                }
                else if (State == RelayUiState.PrerequisiteFailed)
                {
                    StateText = null;
                    State = RelayUiState.Stopped;
                }

                break;

            case RelayStarting starting:
                _startingPort = starting.Options.Port;
                State = RelayUiState.Starting;
                Signal?.Invoke(this, SessionSignal.RelayStarting);
                break;

            case ConnectionInspected inspected:
                Connection = inspected.Inspection;
                break;

            case RelayEventForwarded forwarded:
                Apply(forwarded.Event);
                RelayEventReceived?.Invoke(this, forwarded.Event);
                break;

            default:
                break;
        }
    }

    private void Apply(RelayEvent relayEvent)
    {
        switch (relayEvent)
        {
            case RelayStarted started:
                AdvertisedEndpoint = started.Endpoint;
                IsPaused = false;
                Presence = PhonePresence.NotConnected;
                LastPhoneSeen = null;
                State = RelayUiState.Ready;
                break;

            case AcceptingChanged changed:
                IsPaused = !changed.Accepting;
                if (IsRelayRunning && State != RelayUiState.Thinking)
                {
                    State = IsPaused ? RelayUiState.Paused : RelayUiState.Ready;
                }

                break;

            case PhoneSeen:
                LastPhoneSeen = _clock.GetUtcNow();
                Presence = PhonePresence.Connected;
                break;

            case RequestStarted:
                State = RelayUiState.Thinking;
                break;

            case RequestCompleted:
                State = IsPaused ? RelayUiState.Paused : RelayUiState.Ready;
                break;

            case RequestFailed:
                State = RelayUiState.RequestFailed;
                break;

            case RelayFaulted faulted:
                FaultCode = faulted.Code;
                StateText = faulted.Code == RelayFaultCode.Internal
                    ? StatusTexts.RelayStoppedByItself
                    : PrerequisiteTexts.StartFault(faulted.Code, _startingPort);
                State = RelayUiState.Faulted;
                break;

            case RelayStopped stopped:
                IsPaused = false;
                Presence = PhonePresence.NotConnected;
                LastPhoneSeen = null;
                if (stopped.Reason == RelayStopReason.Requested)
                {
                    State = RelayUiState.Stopped;
                }

                Signal?.Invoke(this, SessionSignal.RelayEnded);
                break;

            default:
                break;
        }
    }

    private void Raise(SessionMessageKind kind, string text) =>
        MessageRaised?.Invoke(this, new SessionMessage(_clock.GetUtcNow(), kind, text));
}
