using System.ComponentModel;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// An <see cref="IDesktopSession"/> for view-model tests. The test sets its state (each change is
/// announced, as the real session does), raises its events, and reads back which commands were called.
/// Nothing here touches a supervisor, a relay or a thread.
/// </summary>
internal sealed class FakeSession : IDesktopSession
{
    private RelayUiState _state = RelayUiState.Stopped;
    private string? _stateText;
    private RelayFaultCode? _faultCode;
    private bool _isPaused;
    private PhonePresence _presence = PhonePresence.NotConnected;
    private DateTimeOffset? _lastPhoneSeen;
    private string? _advertisedEndpoint;
    private PrerequisiteReport? _prerequisites;
    private ConnectionInspection? _connection;
    private bool? _isConnectionReady;
    private RelaySettings _settings = RelaySettings.Default;
    private SettingsLoadResult _settingsLoad = new(RelaySettings.Default, SettingsFileState.Missing, []);
    private bool _settingsDifferFromRunning;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<RelayEvent>? RelayEventReceived;

    public event EventHandler<PairingQr>? PairingQrProduced;

    public event EventHandler<SessionSignal>? Signal;

    public event EventHandler<SessionMessage>? MessageRaised;

    // ---- State the test sets --------------------------------------------------------------------

    public RelayUiState State
    {
        get => _state;
        set
        {
            _state = value;
            Changed(nameof(State), nameof(IsRelayRunning), nameof(IsConnectionReady));
        }
    }

    public string? StateText
    {
        get => _stateText;
        set => Set(ref _stateText, value, nameof(StateText));
    }

    public RelayFaultCode? FaultCode
    {
        get => _faultCode;
        set => Set(ref _faultCode, value, nameof(FaultCode));
    }

    public bool IsRelayRunning =>
        State is RelayUiState.Ready or RelayUiState.Paused or RelayUiState.Thinking or RelayUiState.RequestFailed;

    public bool IsPaused
    {
        get => _isPaused;
        set => Set(ref _isPaused, value, nameof(IsPaused));
    }

    public PhonePresence Presence
    {
        get => _presence;
        set => Set(ref _presence, value, nameof(Presence));
    }

    public DateTimeOffset? LastPhoneSeen
    {
        get => _lastPhoneSeen;
        set => Set(ref _lastPhoneSeen, value, nameof(LastPhoneSeen));
    }

    public string? AdvertisedEndpoint
    {
        get => _advertisedEndpoint;
        set => Set(ref _advertisedEndpoint, value, nameof(AdvertisedEndpoint));
    }

    public PrerequisiteReport? Prerequisites
    {
        get => _prerequisites;
        set => Set(ref _prerequisites, value, nameof(Prerequisites));
    }

    public ConnectionInspection? Connection
    {
        get => _connection;
        set
        {
            _connection = value;
            Changed(nameof(Connection), nameof(IsConnectionReady));
        }
    }

    /// <summary>
    /// By default computed as the real session does: the relay runs and the selected mode's mapping is
    /// present. Set it to force a value.
    /// </summary>
    public bool IsConnectionReady
    {
        get => _isConnectionReady
            ?? (IsRelayRunning
                && Connection?.For(Settings.ConnectionMode).State is MappingState.Present or MappingState.PresentNotOwned);
        set
        {
            _isConnectionReady = value;
            Changed(nameof(IsConnectionReady));
        }
    }

    public RelaySettings Settings
    {
        get => _settings;
        set => Set(ref _settings, value, nameof(Settings));
    }

    public SettingsLoadResult SettingsLoad
    {
        get => _settingsLoad;
        set => Set(ref _settingsLoad, value, nameof(SettingsLoad));
    }

    public bool SettingsDifferFromRunning
    {
        get => _settingsDifferFromRunning;
        set => Set(ref _settingsDifferFromRunning, value, nameof(SettingsDifferFromRunning));
    }

    // ---- What the commands answer ---------------------------------------------------------------

    /// <summary>What ResetPairingAsync returns.</summary>
    public bool ResetSucceeds { get; set; } = true;

    /// <summary>The problems SaveSettings returns. Empty means saved: the settings are then adopted.</summary>
    public IReadOnlyList<SettingsProblem> SaveProblems { get; set; } = [];

    /// <summary>The refusal PlanSetUp reports. Anything but None makes it return null.</summary>
    public MappingRefusal PlanRefusal { get; set; } = MappingRefusal.None;

    /// <summary>What ExecuteMappingAsync returns. Null means "done", with the current settings.</summary>
    public MappingResult? MappingResult { get; set; }

    /// <summary>When set, the asynchronous commands wait for it, so a test can look at the busy state.</summary>
    public TaskCompletionSource? Gate { get; set; }

    // ---- What was called ------------------------------------------------------------------------

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public int TogglePauseCalls { get; private set; }

    public int CheckAgainCalls { get; private set; }

    public int ShowPairingCalls { get; private set; }

    public int ResetPairingCalls { get; private set; }

    public int ClearActivityCalls { get; private set; }

    public List<MappingPlan> ExecutedPlans { get; } = [];

    public List<RelaySettings> SavedSettings { get; } = [];

    // ---- Events the test raises -----------------------------------------------------------------

    public void RaiseRelayEvent(RelayEvent relayEvent) => RelayEventReceived?.Invoke(this, relayEvent);

    public void RaisePairingQr(PairingQr qr) => PairingQrProduced?.Invoke(this, qr);

    public void RaiseSignal(SessionSignal signal) => Signal?.Invoke(this, signal);

    public void RaiseMessage(SessionMessage message) => MessageRaised?.Invoke(this, message);

    // ---- IDesktopSession commands ---------------------------------------------------------------

    public async Task StartAsync()
    {
        StartCalls++;
        await WaitAsync();
    }

    public async Task StopAsync()
    {
        StopCalls++;
        await WaitAsync();
    }

    public async Task TogglePauseAsync()
    {
        TogglePauseCalls++;
        await WaitAsync();
    }

    public async Task CheckAgainAsync()
    {
        CheckAgainCalls++;
        await WaitAsync();
    }

    public async Task ShowPairingAsync()
    {
        ShowPairingCalls++;
        await WaitAsync();
    }

    public async Task<bool> ResetPairingAsync()
    {
        ResetPairingCalls++;
        await WaitAsync();
        return ResetSucceeds;
    }

    public MappingPlan? PlanSetUp(out MappingRefusal refusal)
    {
        refusal = PlanRefusal;
        if (refusal != MappingRefusal.None)
        {
            return null;
        }

        var isPublic = Settings.ConnectionMode == ConnectionMode.Public;
        var action = isPublic ? MappingAction.ApplyPublic : MappingAction.ApplyPrivate;
        var command = isPublic
            ? MappingCommand.ApplyPublic(Settings.PublicHttpsPort, Settings.PublicPathPrefix, Settings.RelayPort)
            : MappingCommand.ApplyPrivate(Settings.PrivateHttpsPort, Settings.RelayPort);
        return new MappingPlan(action, command, "Effect of " + action + ".", Entry: null);
    }

    public MappingPlan PlanRemoval(OwnedMapping entry)
    {
        var isPublic = entry.Mode == ConnectionMode.Public;
        var action = isPublic ? MappingAction.RemovePublic : MappingAction.RemovePrivate;
        var command = isPublic
            ? MappingCommand.RemovePublic(entry.HttpsPort, entry.Path)
            : MappingCommand.RemovePrivate(entry.HttpsPort);
        return new MappingPlan(action, command, "Effect of " + action + ".", entry);
    }

    public async Task<MappingResult> ExecuteMappingAsync(MappingPlan plan)
    {
        ExecutedPlans.Add(plan);
        await WaitAsync();
        return MappingResult
            ?? new MappingResult(MappingOutcome.Done, MappingRefusal.None, OthersChanged: false, Settings, After: null);
    }

    public IReadOnlyList<SettingsProblem> SaveSettings(RelaySettings settings)
    {
        SavedSettings.Add(settings);
        if (SaveProblems.Count == 0)
        {
            Settings = settings;
        }

        return SaveProblems;
    }

    public void ClearActivity()
    {
        ClearActivityCalls++;
        RaiseSignal(SessionSignal.ActivityCleared);
    }

    private Task WaitAsync() => Gate?.Task ?? Task.CompletedTask;

    private void Set<T>(ref T field, T value, string name)
    {
        field = value;
        Changed(name);
    }

    private void Changed(params string[] names)
    {
        foreach (var name in names)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
