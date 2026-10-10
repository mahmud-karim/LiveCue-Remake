using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Text;

namespace LiveCue.Desktop.Pairing;

/// <summary>
/// The Pairing page (screens section 3; W-design 5.6).
///
/// First version (foundation): the page at rest. The tile shows its placeholder for the session's
/// state, "Show QR" asks the session for a pairing when the connection is ready, and "Reset pairing…"
/// opens the confirmation. A produced matrix is not kept or drawn yet: the QR machine, the countdown
/// and the verification notice are work package WP-W10.
/// </summary>
internal sealed class PairingViewModel : ObservableObject, IPairingViewModel
{
    private readonly IDesktopSession _session;
    private readonly IClipboard _clipboard;
    private readonly IToastHost _toasts;
    private readonly AsyncRelayCommand _showQr;
    private readonly RelayCommand _resetPairing;

    public PairingViewModel(
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

        _showQr = new AsyncRelayCommand(session.ShowPairingAsync, () => CanShowQr);
        _resetPairing = new RelayCommand(
            () => dialogs.Show(new ResetPairingDialogViewModel(session)),
            () => CanResetPairing);
        HideQrCommand = new RelayCommand(static () => { }, () => CanHideQr);
        CopyAddressCommand = new RelayCommand(CopyAddress);
        OpenSettingsCommand = new RelayCommand(() => navigator.Navigate(AppPage.Settings));

        session.PropertyChanged += OnSessionChanged;
    }

    public QrState QrState => QrState.Empty;

    public QrHiddenReason HiddenReason => QrHiddenReason.None;

    public QrTileFace TileFace =>
        !_session.IsRelayRunning ? QrTileFace.RelayNotRunning
        : !_session.IsConnectionReady ? QrTileFace.ConnectionNotSetUp
        : QrTileFace.Placeholder;

#pragma warning disable CA1819 // The matrix is the contract's own shape (PairingQr.Modules).
    public bool[][]? QrModules => null;
#pragma warning restore CA1819

    public string PlaceholderLine => TileFace switch
    {
        QrTileFace.RelayNotRunning => PairingText.TileRelayNotRunning,
        QrTileFace.ConnectionNotSetUp => PairingText.TileConnectionNotSetUp,
        _ => PairingText.TilePlaceholder,
    };

    public string CountdownText => string.Empty;

    public bool IsCountdownWarning => false;

    public string QrAutomationStatus => PairingText.QrHidden;

    public string Hint => PairingText.InitialHint;

    public bool CanShowQr => _session.IsConnectionReady;

    public string? ShowQrCaption =>
        CanShowQr ? null
        : _session.IsRelayRunning ? PairingText.CaptionConnectionNotSetUp
        : PairingText.CaptionRelayNotRunning;

    public bool CanHideQr => false;

    public string PcAddress => _session.AdvertisedEndpoint ?? string.Empty;

    public BannerModel? VerifyNotice => null;

    public bool CanResetPairing => _session.State is not (RelayUiState.Checking or RelayUiState.Starting);

    public ICommand ShowQrCommand => _showQr;

    public ICommand HideQrCommand { get; }

    public ICommand CopyAddressCommand { get; }

    public ICommand ResetPairingCommand => _resetPairing;

    public ICommand OpenSettingsCommand { get; }

    private void CopyAddress()
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
            case nameof(IDesktopSession.State) or nameof(IDesktopSession.IsConnectionReady):
                OnPropertyChanged(nameof(TileFace));
                OnPropertyChanged(nameof(PlaceholderLine));
                OnPropertyChanged(nameof(CanShowQr));
                OnPropertyChanged(nameof(ShowQrCaption));
                OnPropertyChanged(nameof(CanResetPairing));
                _showQr.NotifyCanExecuteChanged();
                _resetPairing.NotifyCanExecuteChanged();
                break;

            case nameof(IDesktopSession.AdvertisedEndpoint):
                OnPropertyChanged(nameof(PcAddress));
                break;

            default:
                break;
        }
    }
}
