using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Composition;
using LiveCue.Desktop.Text;
using LiveCue.Hosting;
using LiveCue.Relay;

namespace LiveCue.Desktop.Settings;

/// <summary>
/// The Settings page (screens section 5; W-design 5.8).
///
/// First version (foundation): the fields show the saved settings and can be typed into, but nothing
/// is validated or saved; the status rows show fixed texts; "Set up connection…" and "Remove…" open the
/// mapping dialog with the session's plan. Work package WP-W10 builds the page.
/// </summary>
internal sealed class SettingsViewModel : ObservableObject, ISettingsViewModel
{
    private static readonly StatusIndicator NotReported =
        new(StatusMark.Ring, StatusTone.Offline, SettingsText.NotReportedYet);

    private static readonly StatusIndicator VersionUnknown =
        new(StatusMark.Ring, StatusTone.Offline, SettingsText.CodexVersionUnknown);

    private readonly IDesktopSession _session;
    private bool _hideFromScreenCapture;
    private ConnectionMode _mode;
    private string _relayPortText;
    private string _privateHttpsPortText;
    private int _publicHttpsPort;
    private string _publicPathPrefix;
    private string _codexPathText;
    private string _labFolderText;

    public SettingsViewModel(
        IDesktopSession session,
        IDialogHost dialogs,
        IClipboard clipboard,
        IToastHost toasts,
        IExternalLauncher launcher,
        HostingServices hosting,
        IUiDispatcher ui)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(hosting);
        ArgumentNullException.ThrowIfNull(ui);

        _session = session;
        var saved = session.Settings;
        _mode = saved.ConnectionMode;
        _relayPortText = saved.RelayPort.ToString(CultureInfo.InvariantCulture);
        _privateHttpsPortText = saved.PrivateHttpsPort.ToString(CultureInfo.InvariantCulture);
        _publicHttpsPort = saved.PublicHttpsPort;
        _publicPathPrefix = saved.PublicPathPrefix;
        _codexPathText = saved.CodexExecutablePath ?? string.Empty;
        _labFolderText = saved.AsrLabFolder ?? string.Empty;
        _hideFromScreenCapture = saved.HideFromScreenCapture;

        SaveCommand = new RelayCommand(static () => { }, static () => false);
        DiscardCommand = new RelayCommand(static () => { }, static () => false);
        SetUpMappingCommand = new RelayCommand(() =>
        {
            if (session.PlanSetUp(out _) is { } plan)
            {
                dialogs.Show(new MappingDialogViewModel(session, plan));
            }
        });
        RemoveMappingCommand = new RelayCommand<object?>(parameter =>
        {
            if (parameter is OwnedMapping entry)
            {
                dialogs.Show(new MappingDialogViewModel(session, session.PlanRemoval(entry)));
            }
        });
        CheckMappingCommand = new AsyncRelayCommand(session.CheckAgainAsync);
        CheckAssistantCommand = new AsyncRelayCommand(session.CheckAgainAsync);
        MarkContainmentCheckedCommand = new RelayCommand(static () => { }, static () => false);
        CopyAddressCommand = new RelayCommand(() =>
        {
            if (session.AdvertisedEndpoint is { } endpoint && clipboard.TrySetText(endpoint))
            {
                toasts.Show(ShellText.Copied);
            }
        });
        // The source link is chosen with the About block (work package WP-W10); until then this does nothing.
        OpenSourceCommand = new RelayCommand(static () => { });
        OpenNoticesCommand = new RelayCommand(() => launcher.OpenFile(AppEnvironment.ThirdPartyNoticesPath));
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged
    {
        add { }
        remove { }
    }

    public bool HasErrors => false;

    public ConnectionMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(ModeExplanation));
            }
        }
    }

    public string ModeExplanation =>
        Mode == ConnectionMode.Public ? SettingsText.PublicExplanation : SettingsText.PrivateExplanation;

    public string RelayPortText
    {
        get => _relayPortText;
        set => SetProperty(ref _relayPortText, value ?? string.Empty);
    }

    public string PrivateHttpsPortText
    {
        get => _privateHttpsPortText;
        set => SetProperty(ref _privateHttpsPortText, value ?? string.Empty);
    }

    public int PublicHttpsPort
    {
        get => _publicHttpsPort;
        set => SetProperty(ref _publicHttpsPort, value);
    }

    public string PublicPathPrefix
    {
        get => _publicPathPrefix;
        set => SetProperty(ref _publicPathPrefix, value ?? string.Empty);
    }

    public string PcAddress => _session.AdvertisedEndpoint ?? SettingsText.AddressUnknown;

    public StatusIndicator MappingStatus => NotReported;

    public string? MappingCaption => null;

    public string MappingCommandText => _session.PlanSetUp(out _)?.Command.DisplayText ?? string.Empty;

    public string MappingEffectText => _session.PlanSetUp(out _)?.EffectText ?? string.Empty;

    public bool CanSetUpMapping => false;

    public OwnedMapping? CurrentOwnedMapping => null;

    public IReadOnlyList<StaleMappingRow> StaleMappings { get; } = [];

    public IReadOnlyList<UnownedExposureRow> UnownedExposures { get; } = [];

    public string? ExposureUnknownText => null;

    public BannerModel? RestartNotice => null;

    public BannerModel? SettingsFileNotice => null;

    public string CodexPathText
    {
        get => _codexPathText;
        set => SetProperty(ref _codexPathText, value ?? string.Empty);
    }

    public StatusIndicator CodexPathStatus => NotReported;

    public string? CodexPathDetail => null;

    public StatusIndicator CodexLoginStatus => NotReported;

    public StatusIndicator CodexVersionStatus => VersionUnknown;

    public string? CodexVersionDetail => null;

    public bool CanMarkContainmentChecked => false;

    public bool IsCheckingAssistant => false;

    public string LabFolderText
    {
        get => _labFolderText;
        set => SetProperty(ref _labFolderText, value ?? string.Empty);
    }

    public StatusIndicator LabFolderStatus => NotReported;

    public string? LabFolderDetail => null;

    public StatusIndicator LoadedModelStatus => NotReported;

    public string SpeechCaption => string.Empty;

    public bool HideFromScreenCapture
    {
        get => _hideFromScreenCapture;
        set => SetProperty(ref _hideFromScreenCapture, value);
    }

    public string Version => AppEnvironment.Version;

    public bool HasChanges => false;

    public ICommand SaveCommand { get; }

    public ICommand DiscardCommand { get; }

    public ICommand SetUpMappingCommand { get; }

    public ICommand RemoveMappingCommand { get; }

    public ICommand CheckMappingCommand { get; }

    public ICommand CheckAssistantCommand { get; }

    public ICommand MarkContainmentCheckedCommand { get; }

    public ICommand CopyAddressCommand { get; }

    public ICommand OpenSourceCommand { get; }

    public ICommand OpenNoticesCommand { get; }

    public IEnumerable GetErrors(string? propertyName) => Array.Empty<string>();
}
