using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Session;
using LiveCue.Desktop.Text;

namespace LiveCue.Desktop.Live;

/// <summary>
/// The Live page (screens section 2; W-design 5.5).
///
/// First version (foundation): the relay controls and the two statuses follow the session; the
/// transcript and the reply show their initial texts; there is no prerequisite list, connection banner
/// or failure banner yet. Work package WP-W09 builds the page.
/// </summary>
internal sealed class LiveViewModel : ObservableObject, ILiveViewModel
{
    private readonly IDesktopSession _session;
    private readonly IClipboard _clipboard;
    private readonly IToastHost _toasts;

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
        _clipboard = clipboard;
        _toasts = toasts;

        StartRelayCommand = new AsyncRelayCommand(session.StartAsync);
        PauseResumeCommand = new AsyncRelayCommand(session.TogglePauseAsync);
        StopRelayCommand = new AsyncRelayCommand(session.StopAsync);
        CheckAgainCommand = new AsyncRelayCommand(session.CheckAgainAsync);
        OpenSettingsCommand = new RelayCommand(() => navigator.Navigate(AppPage.Settings));
        SetUpConnectionCommand = new RelayCommand(static () => { });
        CopyPcAddressCommand = new RelayCommand(CopyPcAddress);

        session.PropertyChanged += OnSessionChanged;
    }

    public StatusIndicator RelayStatus => StatusTexts.Relay(_session.State, _session.StateText, _session.FaultCode);

    public string? RelayStatusCaption => _session.State == RelayUiState.Paused ? StatusTexts.PausedCaption : null;

    public StatusIndicator PhoneStatus => StatusTexts.Phone(_session.Presence);

    public string PcAddress => _session.AdvertisedEndpoint ?? LiveText.PcAddressPlaceholder;

    public bool HasPcAddress => _session.AdvertisedEndpoint is not null;

    public BannerModel? ConnectionBanner => null;

    public bool IsStartVisible => !_session.IsRelayRunning;

    public bool IsStartBusy => _session.State is RelayUiState.Checking or RelayUiState.Starting;

    public string StartLabel => IsStartBusy ? LiveText.Checking : LiveText.StartRelay;

    public bool AreRunningControlsVisible => _session.IsRelayRunning;

    public string PauseResumeLabel => _session.IsPaused ? LiveText.ResumeRelay : LiveText.PauseRelay;

    public bool IsPaused => _session.IsPaused;

    public bool ArePrerequisitesVisible => false;

    public IReadOnlyList<PrerequisiteRow> Prerequisites { get; } = [];

    public string TranscriptHint => LiveText.TranscriptHint;

    public string TranscriptText => LiveText.TranscriptPlaceholder;

    public string TranscriptSecondaryText => string.Empty;

    public bool IsTranscriptPlaceholder => true;

    public string ReplyHint => LiveText.ReplyHint;

    public string ReplyText => LiveText.ReplyPlaceholder;

    public IReadOnlyList<string> ReplyKeyPoints { get; } = [];

    public IReadOnlyList<string> ReplyActionItems { get; } = [];

    public bool IsReplyPlaceholder => true;

    public bool IsReplyThinking => false;

    public BannerModel? ReplyFailure => null;

    public string? ReplyTiming => null;

    public ICommand StartRelayCommand { get; }

    public ICommand PauseResumeCommand { get; }

    public ICommand StopRelayCommand { get; }

    public ICommand CheckAgainCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand SetUpConnectionCommand { get; }

    public ICommand CopyPcAddressCommand { get; }

    private void CopyPcAddress()
    {
        if (_session.AdvertisedEndpoint is { } endpoint && _clipboard.TrySetText(endpoint))
        {
            _toasts.Show(ShellText.Copied);
        }
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(IDesktopSession.State) or nameof(IDesktopSession.StateText) or nameof(IDesktopSession.FaultCode):
                OnPropertyChanged(nameof(RelayStatus));
                OnPropertyChanged(nameof(RelayStatusCaption));
                OnPropertyChanged(nameof(IsStartVisible));
                OnPropertyChanged(nameof(IsStartBusy));
                OnPropertyChanged(nameof(StartLabel));
                OnPropertyChanged(nameof(AreRunningControlsVisible));
                break;

            case nameof(IDesktopSession.IsPaused):
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(PauseResumeLabel));
                break;

            case nameof(IDesktopSession.Presence):
                OnPropertyChanged(nameof(PhoneStatus));
                break;

            case nameof(IDesktopSession.AdvertisedEndpoint):
                OnPropertyChanged(nameof(PcAddress));
                OnPropertyChanged(nameof(HasPcAddress));
                break;

            default:
                break;
        }
    }
}
