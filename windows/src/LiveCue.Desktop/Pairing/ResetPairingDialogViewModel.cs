using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Text;

namespace LiveCue.Desktop.Pairing;

/// <summary>
/// The reset confirmation (screens 6.1; W-design 5.9). Confirm resets through the session; success
/// closes the dialog and a failure stays open with its text.
///
/// First version (foundation): one body text for both the running and the stopped relay. Work package
/// WP-W10 finishes it.
/// </summary>
internal sealed class ResetPairingDialogViewModel : ObservableObject, IResetPairingDialogViewModel
{
    private readonly IDesktopSession _session;
    private readonly AsyncRelayCommand _confirm;
    private readonly RelayCommand _cancel;
    private bool _isBusy;
    private BannerModel? _failure;

    public ResetPairingDialogViewModel(IDesktopSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _confirm = new AsyncRelayCommand(ConfirmAsync, () => !IsBusy);
        _cancel = new RelayCommand(() => Closed?.Invoke(this, EventArgs.Empty), () => !IsBusy);
    }

    public event EventHandler? Closed;

    public string Title => PairingText.ResetTitle;

    public string Body => PairingText.ResetBody;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                _confirm.NotifyCanExecuteChanged();
                _cancel.NotifyCanExecuteChanged();
            }
        }
    }

    public BannerModel? Failure
    {
        get => _failure;
        private set => SetProperty(ref _failure, value);
    }

    public ICommand ConfirmCommand => _confirm;

    public ICommand CancelCommand => _cancel;

    private async Task ConfirmAsync()
    {
        IsBusy = true;
        Failure = null;
        var done = await _session.ResetPairingAsync();
        IsBusy = false;
        if (done)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Failure = new BannerModel(BannerTone.Danger, null, PairingText.ResetFailed);
        }
    }
}
