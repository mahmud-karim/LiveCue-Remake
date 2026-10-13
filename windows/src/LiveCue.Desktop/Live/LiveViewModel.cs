using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Session;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Text;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.Live;

/// <summary>
/// The Live page (screens section 2; W-design 5.5): the relay controls, the relay and phone status, the
/// PC address, the two warnings about the connection, the prerequisite list of a start that failed, and
/// the mirror of the last request and of the answer that went back.
///
/// Everything it shows is derived from the session: its state for the controls and the statuses, its
/// events for the transcript and the reply. It starts nothing by itself and never changes anything in
/// Tailscale: "Set up connection…" only opens the dialog that shows the command (rule TS-9).
/// Use it on the UI thread.
/// </summary>
internal sealed class LiveViewModel : ObservableObject, ILiveViewModel
{
    private readonly IDesktopSession _session;
    private readonly IDialogHost _dialogs;
    private readonly IClipboard _clipboard;
    private readonly IToastHost _toasts;
    private readonly TimeProvider _clock;
    private readonly IUiDispatcher _ui;
    private readonly ITimer _ageTimer;
    private readonly RelayCommand _setUpConnection;
    private readonly RelayCommand _copyPcAddress;
    private readonly TranscriptModel _transcript = new();

    private bool _ageTimerRuns;
    private IReadOnlyList<PrerequisiteRow> _prerequisites;
    private string _transcriptHint = LiveText.TranscriptHint;
    private string _replyHint = LiveText.ReplyHint;
    private ReplyModel _reply = ReplyModel.Empty;
    private bool _isReplyThinking;
    private BannerModel? _replyFailure;
    private string? _replyTiming;

    public LiveViewModel(
        IDesktopSession session,
        INavigator navigator,
        IDialogHost dialogs,
        IClipboard clipboard,
        IToastHost toasts,
        TimeProvider clock,
        IUiDispatcher ui)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ui);

        _session = session;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _toasts = toasts;
        _clock = clock;
        _ui = ui;
        _prerequisites = PrerequisiteRows.From(session.Prerequisites);

        StartRelayCommand = new AsyncRelayCommand(session.StartAsync);
        PauseResumeCommand = new AsyncRelayCommand(session.TogglePauseAsync);
        StopRelayCommand = new AsyncRelayCommand(session.StopAsync);
        CheckAgainCommand = new AsyncRelayCommand(session.CheckAgainAsync);
        OpenSettingsCommand = new RelayCommand(() => navigator.Navigate(AppPage.Settings));
        _setUpConnection = new RelayCommand(SetUpConnection, () => CanSetUpConnection);
        _copyPcAddress = new RelayCommand(CopyPcAddress, () => HasPcAddress);

        // The phone's tooltip says how long ago it was last heard: it is told again once a tick (W-10)
        // for as long as there is a phone to speak of.
        _ageTimer = clock.CreateTimer(_ => _ui.Post(OnAgeTick), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        FollowPhoneAge();

        session.PropertyChanged += OnSessionChanged;
        session.RelayEventReceived += OnRelayEvent;
        session.Signal += OnSignal;
    }

    // ---- Status strip ---------------------------------------------------------------------------

    public StatusIndicator RelayStatus
    {
        get
        {
            var status = StatusTexts.Relay(_session.State, _session.StateText, _session.FaultCode);

            // A failed check or a fault is a sentence that may wrap: its tooltip shows it whole (screens 2.3).
            return _session.State is RelayUiState.PrerequisiteFailed or RelayUiState.Faulted
                ? status with { ToolTip = status.Text }
                : status;
        }
    }

    public string? RelayStatusCaption => _session.State == RelayUiState.Paused ? StatusTexts.PausedCaption : null;

    public StatusIndicator PhoneStatus =>
        StatusTexts.Phone(_session.Presence, _session.LastPhoneSeen, _clock.GetUtcNow());

    public string PcAddress => _session.AdvertisedEndpoint ?? LiveText.PcAddressPlaceholder;

    public bool HasPcAddress => _session.AdvertisedEndpoint is not null;

    public BannerModel? ConnectionBanner
    {
        get
        {
            if (!_session.IsRelayRunning || SelectedMapping is not { } mapping)
            {
                return null;
            }

            return mapping.State switch
            {
                MappingState.Absent => new BannerModel(
                    BannerTone.Warning, null, LiveText.ConnectionNotSetUp, LiveText.SetUpConnection),

                // It cannot be added from here: the reason follows, and the button stays disabled.
                MappingState.Blocked or MappingState.Unknown => new BannerModel(
                    BannerTone.Warning,
                    null,
                    WithReason(LiveText.ConnectionNotSetUp, MappingTexts.Refusal(mapping.BlockedBy, mapping.HttpsPort, mapping.Path)),
                    LiveText.SetUpConnection),

                _ => null,
            };
        }
    }

    public BannerModel? ExposureBanner
    {
        get
        {
            if (_session.Connection is not { } connection)
            {
                return null;
            }

            if (_session.Settings.ConnectionMode == ConnectionMode.Private && connection.HasFunnelExposure)
            {
                return new BannerModel(
                    BannerTone.Danger, LiveText.ExposureTitle, PrerequisiteTexts.ExposedInPrivateMode, LiveText.OpenSettings);
            }

            var unmanaged = connection.UnownedExposure.Count();
            if (unmanaged > 0)
            {
                return new BannerModel(BannerTone.Warning, null, LiveText.UnmanagedMappings(unmanaged), LiveText.OpenSettings);
            }

            // Without Tailscale there is nothing that could expose the relay, and the check list says so.
            return connection.StatusFailure is TailscaleReadFailure.None or TailscaleReadFailure.NotInstalled
                ? null
                : new BannerModel(BannerTone.Warning, null, MappingTexts.ExposureUnknown, LiveText.OpenSettings);
        }
    }

    // ---- Relay controls -------------------------------------------------------------------------

    public bool IsStartVisible => !_session.IsRelayRunning;

    public bool IsStartBusy => _session.State is RelayUiState.Checking or RelayUiState.Starting;

    public string StartLabel => IsStartBusy ? LiveText.Checking : LiveText.StartRelay;

    public bool AreRunningControlsVisible => _session.IsRelayRunning;

    public string PauseResumeLabel => _session.IsPaused ? LiveText.ResumeRelay : LiveText.PauseRelay;

    public bool IsPaused => _session.IsPaused;

    // ---- Prerequisite list ----------------------------------------------------------------------

    public bool ArePrerequisitesVisible => _session.State == RelayUiState.PrerequisiteFailed;

    public IReadOnlyList<PrerequisiteRow> Prerequisites => _prerequisites;

    // ---- Incoming transcript --------------------------------------------------------------------

    public string TranscriptHint => _transcriptHint;

    public string TranscriptText => _transcript.IsEmpty ? LiveText.TranscriptPlaceholder : _transcript.Text;

    public string TranscriptSecondaryText => _transcript.Secondary;

    public bool IsTranscriptPlaceholder => _transcript.IsEmpty;

    // ---- Assistant reply ------------------------------------------------------------------------

    public string ReplyHint => _replyHint;

    public string ReplyText => IsReplyPlaceholder ? LiveText.ReplyPlaceholder : _reply.Text;

    public IReadOnlyList<string> ReplyKeyPoints => _reply.KeyPoints;

    public IReadOnlyList<string> ReplyActionItems => _reply.ActionItems;

    public bool IsReplyPlaceholder => !_isReplyThinking && _replyFailure is null && ReferenceEquals(_reply, ReplyModel.Empty);

    public bool IsReplyThinking => _isReplyThinking;

    public BannerModel? ReplyFailure => _replyFailure;

    public string? ReplyTiming => _replyTiming;

    // ---- Commands -------------------------------------------------------------------------------

    public ICommand StartRelayCommand { get; }

    public ICommand PauseResumeCommand { get; }

    public ICommand StopRelayCommand { get; }

    public ICommand CheckAgainCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand SetUpConnectionCommand => _setUpConnection;

    public ICommand CopyPcAddressCommand => _copyPcAddress;

    /// <summary>The mapping of the mode the settings select, as last inspected; null before the first inspection.</summary>
    private MappingInspection? SelectedMapping => _session.Connection?.For(_session.Settings.ConnectionMode);

    /// <summary>Only a mapping that is simply missing can be set up from here (screens 2.10).</summary>
    private bool CanSetUpConnection => _session.IsRelayRunning && SelectedMapping?.State == MappingState.Absent;

    private static string WithReason(string text, string reason) => reason.Length == 0 ? text : text + " " + reason;

    private void SetUpConnection()
    {
        if (CanSetUpConnection && _session.PlanSetUp(out _) is { } plan)
        {
            _dialogs.Show(new MappingDialogViewModel(_session, plan));
        }
    }

    private void CopyPcAddress()
    {
        if (_session.AdvertisedEndpoint is { } endpoint && _clipboard.TrySetText(endpoint))
        {
            _toasts.Show(ShellText.Copied);
        }
    }

    // ---- The session's events -------------------------------------------------------------------

    private void OnRelayEvent(object? sender, RelayEvent relayEvent)
    {
        switch (relayEvent)
        {
            case RequestStarted request:
                _transcript.Show(request);
                TranscriptChanged(LiveText.RequestHint(request.Kind));
                ShowReply(LiveText.ReplyProcessing, ReplyModel.Empty, isThinking: true, failure: null, timing: null);
                break;

            case RequestCompleted reply:
                ShowReply(
                    LiveText.ReplySent(reply.Model, reply.ReasoningEffort, reply.CodexMs),
                    ReplyModel.FromPayload(reply.PayloadJson),
                    isThinking: false,
                    failure: null,
                    timing: LiveText.ReplyTiming(reply.DurationMs));
                break;

            case RequestFailed failed:
                ShowReply(
                    LiveText.ReplyNotSent,
                    ReplyModel.Empty,
                    isThinking: false,
                    failure: new BannerModel(BannerTone.Danger, LiveText.FailureTitle, LiveText.Failure(failed.Code)),
                    timing: null);
                break;

            case AssistantNotice assistant:
                SetTranscriptHint(assistant.Message);
                break;

            case LocalModelChanged model:
                SetTranscriptHint(model.Message);
                break;

            case SpeechStreamChanged speech:
                SetTranscriptHint(speech.Message);
                break;

            case SpeechText speech:
                _transcript.Append(speech.Text);
                TranscriptChanged(_transcriptHint);
                break;

            default:
                break;
        }
    }

    private void OnSignal(object? sender, SessionSignal signal)
    {
        switch (signal)
        {
            case SessionSignal.ActivityCleared:
                _transcript.Clear();
                TranscriptChanged(LiveText.TranscriptHint);
                ShowReply(LiveText.ReplyCleared, ReplyModel.Empty, isThinking: false, failure: null, timing: null);
                break;

            case SessionSignal.RelayEnded:
                // The stopped layout (screens 2.2): both hints as at the start and no timing. What was
                // said and answered stays; a request that was still running has no answer to wait for.
                SetTranscriptHint(LiveText.TranscriptHint);
                ShowReply(LiveText.ReplyHint, _reply, isThinking: false, _replyFailure, timing: null);
                break;

            default:
                break;
        }
    }

    private void SetTranscriptHint(string hint)
    {
        if (!string.IsNullOrEmpty(hint))
        {
            SetProperty(ref _transcriptHint, hint, nameof(TranscriptHint));
        }
    }

    private void TranscriptChanged(string hint)
    {
        SetTranscriptHint(hint);
        OnPropertyChanged(nameof(TranscriptText));
        OnPropertyChanged(nameof(TranscriptSecondaryText));
        OnPropertyChanged(nameof(IsTranscriptPlaceholder));
    }

    private void ShowReply(string hint, ReplyModel reply, bool isThinking, BannerModel? failure, string? timing)
    {
        _reply = reply;
        _isReplyThinking = isThinking;
        _replyFailure = failure;
        _replyTiming = timing;
        _replyHint = hint;

        OnPropertyChanged(nameof(ReplyHint));
        OnPropertyChanged(nameof(IsReplyPlaceholder));
        OnPropertyChanged(nameof(IsReplyThinking));
        OnPropertyChanged(nameof(ReplyFailure));
        OnPropertyChanged(nameof(ReplyText));
        OnPropertyChanged(nameof(ReplyKeyPoints));
        OnPropertyChanged(nameof(ReplyActionItems));
        OnPropertyChanged(nameof(ReplyTiming));
    }

    // ---- The session's state --------------------------------------------------------------------

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(IDesktopSession.State) or nameof(IDesktopSession.IsRelayRunning):
                StatusChanged();
                ControlsChanged();
                ConnectionChanged();
                break;

            case nameof(IDesktopSession.StateText) or nameof(IDesktopSession.FaultCode):
                StatusChanged();
                break;

            case nameof(IDesktopSession.IsPaused):
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(PauseResumeLabel));
                break;

            case nameof(IDesktopSession.Presence) or nameof(IDesktopSession.LastPhoneSeen):
                FollowPhoneAge();
                OnPropertyChanged(nameof(PhoneStatus));
                break;

            case nameof(IDesktopSession.AdvertisedEndpoint):
                AddressChanged();
                break;

            case nameof(IDesktopSession.Prerequisites):
                PrerequisitesChanged();
                break;

            case nameof(IDesktopSession.Connection) or nameof(IDesktopSession.Settings) or nameof(IDesktopSession.IsConnectionReady):
                ConnectionChanged();
                break;

            case null or "":
                // "Everything may have changed."
                StatusChanged();
                ControlsChanged();
                ConnectionChanged();
                AddressChanged();
                PrerequisitesChanged();
                FollowPhoneAge();
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(PauseResumeLabel));
                OnPropertyChanged(nameof(PhoneStatus));
                break;

            default:
                break;
        }
    }

    private void StatusChanged()
    {
        OnPropertyChanged(nameof(RelayStatus));
        OnPropertyChanged(nameof(RelayStatusCaption));
    }

    private void ControlsChanged()
    {
        OnPropertyChanged(nameof(IsStartVisible));
        OnPropertyChanged(nameof(IsStartBusy));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(AreRunningControlsVisible));
        OnPropertyChanged(nameof(ArePrerequisitesVisible));
    }

    private void ConnectionChanged()
    {
        OnPropertyChanged(nameof(ConnectionBanner));
        OnPropertyChanged(nameof(ExposureBanner));
        _setUpConnection.NotifyCanExecuteChanged();
    }

    private void AddressChanged()
    {
        OnPropertyChanged(nameof(PcAddress));
        OnPropertyChanged(nameof(HasPcAddress));
        _copyPcAddress.NotifyCanExecuteChanged();
    }

    private void PrerequisitesChanged()
    {
        _prerequisites = PrerequisiteRows.From(_session.Prerequisites);
        OnPropertyChanged(nameof(Prerequisites));
    }

    // ---- The age in the phone's tooltip ---------------------------------------------------------

    private void FollowPhoneAge()
    {
        var needed = _session.LastPhoneSeen is not null;
        if (needed == _ageTimerRuns)
        {
            return;
        }

        _ageTimerRuns = needed;
        var period = needed ? ProtocolConstants.DesktopTick : Timeout.InfiniteTimeSpan;
        _ageTimer.Change(period, period);
    }

    private void OnAgeTick()
    {
        if (_session.LastPhoneSeen is not null)
        {
            OnPropertyChanged(nameof(PhoneStatus));
        }
    }
}
