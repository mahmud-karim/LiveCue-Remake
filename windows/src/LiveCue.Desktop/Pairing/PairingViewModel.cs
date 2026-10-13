using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Text;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.Pairing;

/// <summary>
/// The Pairing page (screens section 3; W-design 5.6): the QR machine of architecture 3.12.
///
///   Empty   nothing was shown in this run
///   Shown   the session produced a matrix; it is drawn, and a countdown runs for two minutes
///   Hidden  by the button, by the timer, or because the relay ended
///
/// The page receives only the module matrix of the QR (decision A-16): it never holds, shows or
/// copies the pairing text, and when the QR hides the matrix is released, not just covered. Showing
/// the QR is asked for by the "Show QR" button only: coming back to the page asks for nothing
/// (WIN-20), and the countdown keeps running while another page is shown. Use it on the UI thread.
/// </summary>
internal sealed class PairingViewModel : ObservableObject, IPairingViewModel
{
    private readonly IDesktopSession _session;
    private readonly INavigator _navigator;
    private readonly IDialogHost _dialogs;
    private readonly IClipboard _clipboard;
    private readonly IToastHost _toasts;
    private readonly TimeProvider _clock;
    private readonly ITimer _countdownTimer;
    private readonly AsyncRelayCommand _showQr;
    private readonly RelayCommand _hideQr;
    private readonly RelayCommand _resetPairing;
    private readonly RelayCommand _copyAddress;

    private QrState _qrState = QrState.Empty;
    private QrHiddenReason _hiddenReason = QrHiddenReason.None;
    private bool[][]? _modules;
    private DateTimeOffset _hideAt;
    private QrCountdown _countdown;
    private string _hint = PairingText.InitialHint;
    private BannerModel? _verifyNotice;
    private BannerModel? _earlyNotice;
    private ResetPairingDialogViewModel? _resetDialog;
    private bool _qrArrivedDuringReset;
    private bool _nextQrFollowsReset;

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
        _navigator = navigator;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _toasts = toasts;
        _clock = clock;

        _showQr = new AsyncRelayCommand(session.ShowPairingAsync, () => CanShowQr);
        _showQr.PropertyChanged += OnShowQrChanged;
        _hideQr = new RelayCommand(() => Hide(QrHiddenReason.Button), () => CanHideQr);
        _resetPairing = new RelayCommand(OpenResetDialog, () => CanResetPairing);
        _copyAddress = new RelayCommand(CopyAddress, () => HasPcAddress);
        OpenSettingsCommand = new RelayCommand(() => navigator.Navigate(AppPage.Settings));

        // The countdown is told once a tick (W-10), and only while a QR is shown.
        _countdownTimer = clock.CreateTimer(_ => ui.Post(OnCountdownTick), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        session.PropertyChanged += OnSessionChanged;
        session.PairingQrProduced += OnPairingQrProduced;
        session.RelayEventReceived += OnRelayEvent;
        session.Signal += OnSignal;
    }

    // ---- The QR ---------------------------------------------------------------------------------

    public QrState QrState => _qrState;

    public QrHiddenReason HiddenReason => _hiddenReason;

    public QrTileFace TileFace =>
        _qrState == QrState.Shown ? QrTileFace.Code
        : IsCallRunning ? QrTileFace.Generating
        : !_session.IsRelayRunning ? QrTileFace.RelayNotRunning
        : !_session.IsConnectionReady ? QrTileFace.ConnectionNotSetUp
        : QrTileFace.Placeholder;

#pragma warning disable CA1819 // The matrix is the contract's own shape (PairingQr.Modules).
    public bool[][]? QrModules => _modules;
#pragma warning restore CA1819

    public string PlaceholderLine => TileFace switch
    {
        QrTileFace.RelayNotRunning => PairingText.TileRelayNotRunning,
        QrTileFace.ConnectionNotSetUp => PairingText.TileConnectionNotSetUp,
        _ => PairingText.TilePlaceholder,
    };

    public string CountdownText => _qrState == QrState.Shown ? _countdown.Text : string.Empty;

    public bool IsCountdownWarning => _qrState == QrState.Shown && _countdown.IsWarning;

    public string QrAutomationStatus => _qrState == QrState.Shown ? _countdown.AutomationStatus : PairingText.QrHidden;

    // ---- The panel beside it --------------------------------------------------------------------

    public string Hint => _hint;

    public bool CanShowQr => _session.IsConnectionReady;

    public string? ShowQrCaption =>
        CanShowQr ? null
        : _session.IsRelayRunning ? PairingText.CaptionConnectionNotSetUp
        : PairingText.CaptionRelayNotRunning;

    /// <summary>True while "Show QR" waits for the session: the button is busy, not just disabled.</summary>
    public bool IsShowQrBusy => _showQr.IsRunning;

    public bool CanHideQr => _qrState == QrState.Shown;

    public string PcAddress => _session.AdvertisedEndpoint ?? string.Empty;

    /// <summary>False until the relay has run once: the field then shows its placeholder and no copy button.</summary>
    public bool HasPcAddress => _session.AdvertisedEndpoint is not null;

    public BannerModel? VerifyNotice => _verifyNotice;

    public bool CanResetPairing =>
        _session.State is not (RelayUiState.Checking or RelayUiState.Starting) && !IsCallRunning;

    // ---- Commands -------------------------------------------------------------------------------

    public ICommand ShowQrCommand => _showQr;

    public ICommand HideQrCommand => _hideQr;

    public ICommand CopyAddressCommand => _copyAddress;

    public ICommand ResetPairingCommand => _resetPairing;

    public ICommand OpenSettingsCommand { get; }

    /// <summary>True while "Show QR" or a confirmed reset waits for the session.</summary>
    private bool IsCallRunning => _showQr.IsRunning || _resetDialog is { IsBusy: true };

    // ---- The QR machine -------------------------------------------------------------------------

    private void OnPairingQrProduced(object? sender, PairingQr qr)
    {
        ArgumentNullException.ThrowIfNull(qr);

        // A matrix that arrives while the confirmed reset runs, or the first one after the stored
        // pairing was reset with the relay stopped, is the new pairing's (screens 3.4).
        var followsReset = _nextQrFollowsReset || _resetDialog is { IsBusy: true };
        _nextQrFollowsReset = false;
        _qrArrivedDuringReset |= _resetDialog is { IsBusy: true };

        _modules = qr.Modules;
        _qrState = QrState.Shown;
        _hiddenReason = QrHiddenReason.None;
        _hideAt = QrCountdown.HideTime(_clock.GetUtcNow());
        _countdown = QrCountdown.At(_hideAt, _clock.GetUtcNow());
        _hint = followsReset ? PairingText.ResetHint : PairingText.ShownHint;

        // The check of this display has usually not finished yet; one that already has is kept.
        _verifyNotice = _earlyNotice;
        _earlyNotice = null;

        _countdownTimer.Change(ProtocolConstants.DesktopTick, ProtocolConstants.DesktopTick);
        QrChanged();
        _navigator.Navigate(AppPage.Pairing);
    }

    private void OnCountdownTick()
    {
        if (_qrState != QrState.Shown)
        {
            return;
        }

        var now = QrCountdown.At(_hideAt, _clock.GetUtcNow());
        if (now.IsOver)
        {
            Hide(QrHiddenReason.Timer);
            return;
        }

        if (now != _countdown)
        {
            _countdown = now;
            CountdownChanged();
        }
    }

    /// <summary>Hides a shown QR: the matrix is let go, the countdown stops, the notice leaves.</summary>
    private void Hide(QrHiddenReason reason)
    {
        if (_qrState != QrState.Shown)
        {
            return;
        }

        _modules = null;
        _qrState = QrState.Hidden;
        _hiddenReason = reason;
        _countdown = default;
        _verifyNotice = null;
        _earlyNotice = null;
        _countdownTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _hint = reason switch
        {
            QrHiddenReason.Button => PairingText.HiddenByButtonHint,
            QrHiddenReason.Timer => PairingText.HiddenByTimerHint,
            _ => PairingText.InitialHint,
        };
        QrChanged();
    }

    private void OnRelayEvent(object? sender, RelayEvent relayEvent)
    {
        if (relayEvent is not SelfCheckCompleted check)
        {
            return;
        }

        var notice = check.Result switch
        {
            SelfCheckResult.Verified => new BannerModel(BannerTone.Success, null, PairingText.Verified),
            SelfCheckResult.VerifiedThroughFunnel => new BannerModel(BannerTone.Success, null, PairingText.VerifiedThroughFunnel),
            _ => new BannerModel(BannerTone.Warning, null, PairingText.NotVerified, PairingText.OpenSettings),
        };

        // A result that overtakes its own matrix is kept for it; one that arrives after the QR was
        // hidden belongs to nothing that is on screen.
        if (IsCallRunning)
        {
            _earlyNotice = notice;
        }

        if (_qrState == QrState.Shown)
        {
            _verifyNotice = notice;
            OnPropertyChanged(nameof(VerifyNotice));
        }
    }

    private void OnSignal(object? sender, SessionSignal signal)
    {
        if (signal != SessionSignal.RelayEnded)
        {
            return;
        }

        // The stopped layout: no QR, no notice, the hint as at the start.
        Hide(QrHiddenReason.RelayEnded);
        _earlyNotice = null;
        if (_verifyNotice is not null)
        {
            _verifyNotice = null;
            OnPropertyChanged(nameof(VerifyNotice));
        }

        if (_hint != PairingText.InitialHint)
        {
            _hint = PairingText.InitialHint;
            OnPropertyChanged(nameof(Hint));
        }
    }

    // ---- Reset ----------------------------------------------------------------------------------

    private void OpenResetDialog()
    {
        if (!CanResetPairing || _resetDialog is not null)
        {
            return;
        }

        var dialog = new ResetPairingDialogViewModel(_session);
        _dialogs.Show(dialog);
        if (!ReferenceEquals(_dialogs.Current, dialog))
        {
            // Another dialog is open: this one was never shown, and lets go of the session again.
            dialog.CancelCommand.Execute(null);
            return;
        }

        _resetDialog = dialog;
        _qrArrivedDuringReset = false;
        dialog.PropertyChanged += OnResetDialogChanged;
        dialog.Closed += OnResetDialogClosed;
    }

    private void OnResetDialogChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(IDialogViewModel.IsBusy))
        {
            CallChanged();
        }
    }

    private void OnResetDialogClosed(object? sender, EventArgs e)
    {
        if (sender is not ResetPairingDialogViewModel dialog)
        {
            return;
        }

        dialog.PropertyChanged -= OnResetDialogChanged;
        dialog.Closed -= OnResetDialogClosed;
        if (ReferenceEquals(_resetDialog, dialog))
        {
            _resetDialog = null;
        }

        // Reset while no relay ran: the new QR is the first one after the next start.
        if (dialog.WasReset && !_qrArrivedDuringReset)
        {
            _nextQrFollowsReset = true;
        }

        CallChanged();
    }

    private void CopyAddress()
    {
        if (_session.AdvertisedEndpoint is { } endpoint && _clipboard.TrySetText(endpoint))
        {
            _toasts.Show(ShellText.Copied);
        }
    }

    // ---- Change notifications -------------------------------------------------------------------

    private void OnShowQrChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(AsyncRelayCommand.IsRunning))
        {
            OnPropertyChanged(nameof(IsShowQrBusy));
            CallChanged();
        }
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(IDesktopSession.State)
                or nameof(IDesktopSession.IsRelayRunning)
                or nameof(IDesktopSession.IsConnectionReady):
                GateChanged();
                break;

            case nameof(IDesktopSession.AdvertisedEndpoint):
                AddressChanged();
                break;

            case null or "":
                // "Everything may have changed."
                GateChanged();
                AddressChanged();
                break;

            default:
                break;
        }
    }

    private void QrChanged()
    {
        OnPropertyChanged(nameof(QrState));
        OnPropertyChanged(nameof(HiddenReason));
        OnPropertyChanged(nameof(QrModules));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(VerifyNotice));
        OnPropertyChanged(nameof(CanHideQr));
        _hideQr.NotifyCanExecuteChanged();
        TileChanged();
        CountdownChanged();
    }

    private void CountdownChanged()
    {
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(IsCountdownWarning));
        OnPropertyChanged(nameof(QrAutomationStatus));
    }

    private void TileChanged()
    {
        OnPropertyChanged(nameof(TileFace));
        OnPropertyChanged(nameof(PlaceholderLine));
    }

    private void GateChanged()
    {
        TileChanged();
        OnPropertyChanged(nameof(CanShowQr));
        OnPropertyChanged(nameof(ShowQrCaption));
        OnPropertyChanged(nameof(CanResetPairing));
        _showQr.NotifyCanExecuteChanged();
        _resetPairing.NotifyCanExecuteChanged();
    }

    private void CallChanged()
    {
        TileChanged();
        OnPropertyChanged(nameof(CanResetPairing));
        _resetPairing.NotifyCanExecuteChanged();
    }

    private void AddressChanged()
    {
        OnPropertyChanged(nameof(PcAddress));
        OnPropertyChanged(nameof(HasPcAddress));
        _copyAddress.NotifyCanExecuteChanged();
    }
}
