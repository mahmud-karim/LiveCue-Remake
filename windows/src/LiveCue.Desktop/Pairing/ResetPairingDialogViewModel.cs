using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Text;

namespace LiveCue.Desktop.Pairing;

/// <summary>
/// The reset confirmation (screens 6.1; W-design 5.9). Reset is the one destructive action of the
/// app, so nothing happens before "Reset pairing" is chosen in this dialog: Cancel has the initial
/// focus and Enter does not confirm. Confirm resets through the session; success closes the dialog,
/// and a failure stays open with its text so that it can be retried.
/// </summary>
internal sealed class ResetPairingDialogViewModel : ObservableObject, IResetPairingDialogViewModel
{
    private readonly IDesktopSession _session;
    private readonly AsyncRelayCommand _confirm;
    private readonly RelayCommand _cancel;
    private bool _isBusy;
    private bool _isClosed;
    private BannerModel? _failure;

    public ResetPairingDialogViewModel(IDesktopSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _confirm = new AsyncRelayCommand(ConfirmAsync, () => !IsBusy);
        _cancel = new RelayCommand(Close, () => !IsBusy);
        session.PropertyChanged += OnSessionChanged;
    }

    public event EventHandler? Closed;

    public string Title => PairingText.ResetTitle;

    /// <summary>With the relay stopped, "The relay keeps running." is left out.</summary>
    public string Body => _session.IsRelayRunning ? PairingText.ResetBodyRunning : PairingText.ResetBody;

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

    /// <summary>True once the pairing was replaced. The page that opened the dialog reads it when the dialog closes.</summary>
    internal bool WasReset { get; private set; }

    private async Task ConfirmAsync()
    {
        if (IsBusy || _isClosed)
        {
            return;
        }

        IsBusy = true;
        Failure = null;
        var done = await _session.ResetPairingAsync();
        WasReset |= done;
        IsBusy = false;
        if (done)
        {
            Close();
        }
        else
        {
            Failure = new BannerModel(BannerTone.Danger, null, PairingText.ResetFailed);
        }
    }

    private void Close()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        _session.PropertyChanged -= OnSessionChanged;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(IDesktopSession.IsRelayRunning) or nameof(IDesktopSession.State) or null or "")
        {
            OnPropertyChanged(nameof(Body));
        }
    }
}
