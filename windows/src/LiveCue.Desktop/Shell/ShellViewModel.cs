using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Session;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop.Shell;

/// <summary>
/// The window: navigation, the sidebar status card, the dialog and toast layers (W-design 5.1, 5.2).
/// It forwards the selected page, the open dialog and the toast to the three small objects that own
/// them, so a page view model reaches them through <see cref="INavigator"/>, <see cref="IDialogHost"/>
/// and <see cref="IToastHost"/> and never through the window.
/// </summary>
internal sealed class ShellViewModel : ObservableObject, IShellViewModel
{
    private readonly IDesktopSession _session;
    private readonly Navigator _navigator;
    private readonly DialogHost _dialogs;
    private readonly ToastHost _toasts;
    private readonly RelayCommand<object?> _navigate;
    private bool _startedOnce;

    public ShellViewModel(
        IDesktopSession session,
        ILiveViewModel live,
        IPairingViewModel pairing,
        IActivityViewModel activity,
        ISettingsViewModel settings,
        Navigator navigator,
        DialogHost dialogs,
        ToastHost toasts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(pairing);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(toasts);

        _session = session;
        Live = live;
        Pairing = pairing;
        Activity = activity;
        Settings = settings;
        _navigator = navigator;
        _dialogs = dialogs;
        _toasts = toasts;

        // The keys and the sidebar do not reach behind an open dialog. A view model still navigates
        // through INavigator while one is open (the reset dialog's new QR opens the Pairing view).
        _navigate = new RelayCommand<object?>(
            parameter =>
            {
                if (parameter is AppPage page)
                {
                    Navigate(page);
                }
            },
            _ => _dialogs.Current is null);
        EscapeCommand = new RelayCommand(() =>
        {
            if (_dialogs.Current is { IsBusy: false } dialog && dialog.CancelCommand.CanExecute(null))
            {
                dialog.CancelCommand.Execute(null);
            }
        });

        _navigator.PropertyChanged += OnNavigatorChanged;
        _dialogs.PropertyChanged += OnDialogChanged;
        _toasts.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ToastText));
        _session.PropertyChanged += OnSessionChanged;
        Live.PropertyChanged += OnLiveChanged;
    }

    /// <summary>How long closing the window waits for the relay to stop (W-11).</summary>
    internal TimeSpan CloseWait { get; init; } = ProtocolConstants.WindowCloseWait;

    public ILiveViewModel Live { get; }

    public IPairingViewModel Pairing { get; }

    public IActivityViewModel Activity { get; }

    public ISettingsViewModel Settings { get; }

    public AppPage Current => _navigator.Current;

    /// <summary>
    /// One page is always selected. A list box can clear its selection; a value that is not a page is
    /// refused, and the list is told afterwards to show the real selection again.
    /// </summary>
    public AppPage SelectedPage
    {
        get => _navigator.Current;
        set
        {
            if (Enum.IsDefined(value))
            {
                _navigator.Navigate(value);
            }
            else
            {
                SynchronizationContext.Current?.Post(_ => OnPropertyChanged(nameof(SelectedPage)), null);
            }
        }
    }

    public object CurrentPage => _navigator.Current switch
    {
        AppPage.Pairing => Pairing,
        AppPage.Activity => Activity,
        AppPage.Settings => Settings,
        _ => Live,
    };

    public StatusIndicator SidebarRelayStatus => StatusTexts.Sidebar(_session.State, _session.FaultCode);

    /// <summary>The same indicator as the Live page, tooltip included.</summary>
    public StatusIndicator SidebarPhoneStatus => Live.PhoneStatus;

    public IDialogViewModel? ActiveDialog => _dialogs.Current;

    public string? ToastText => _toasts.Text;

    public ICommand NavigateCommand => _navigate;

    public ICommand EscapeCommand { get; }

    public void Navigate(AppPage page) => _navigator.Navigate(page);

    /// <summary>The pointer rests on the toast: it stays (design system 4.11).</summary>
    internal void HoldToast() => _toasts.Hold();

    /// <summary>The pointer left the toast: its time starts again.</summary>
    internal void ReleaseToast() => _toasts.Release();

    public Task OnWindowRenderedAsync()
    {
        if (_startedOnce)
        {
            return Task.CompletedTask;
        }

        _startedOnce = true;
        return _session.StartAsync();
    }

    /// <summary>
    /// Stops the relay and waits for it for at most W-11: a stop that hangs must not keep the window
    /// open. Never throws.
    /// </summary>
    public async Task OnWindowClosingAsync()
    {
        try
        {
            await _session.StopAsync().WaitAsync(CloseWait);
        }
        catch (TimeoutException)
        {
            // The window closes anyway; the process ends with it.
        }
    }

    private void OnNavigatorChanged(object? sender, PropertyChangedEventArgs change)
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(CurrentPage));
    }

    private void OnDialogChanged(object? sender, PropertyChangedEventArgs change)
    {
        OnPropertyChanged(nameof(ActiveDialog));
        _navigate.NotifyCanExecuteChanged();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(IDesktopSession.State) or nameof(IDesktopSession.FaultCode):
                OnPropertyChanged(nameof(SidebarRelayStatus));
                break;

            case nameof(IDesktopSession.Presence) or nameof(IDesktopSession.LastPhoneSeen):
                OnPropertyChanged(nameof(SidebarPhoneStatus));
                break;

            default:
                break;
        }
    }

    private void OnLiveChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(ILiveViewModel.PhoneStatus) or null or "")
        {
            OnPropertyChanged(nameof(SidebarPhoneStatus));
        }
    }
}
