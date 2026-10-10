using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCue.Desktop.Session;

namespace LiveCue.Desktop.Shell;

/// <summary>
/// The window: navigation, the sidebar status card, the dialog and toast layers (W-design 5.1, 5.2).
/// It forwards the selected page, the open dialog and the toast to the three small objects that own them.
///
/// First version (foundation): navigation, the sidebar card from the session's state, and the start on
/// the first render. Work package WP-W08 builds the rest.
/// </summary>
internal sealed class ShellViewModel : ObservableObject, IShellViewModel
{
    private readonly IDesktopSession _session;
    private readonly Navigator _navigator;
    private readonly DialogHost _dialogs;
    private readonly ToastHost _toasts;
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

        NavigateCommand = new RelayCommand<object?>(parameter =>
        {
            if (parameter is AppPage page)
            {
                Navigate(page);
            }
        });
        EscapeCommand = new RelayCommand(() =>
        {
            if (_dialogs.Current is { IsBusy: false } dialog && dialog.CancelCommand.CanExecute(null))
            {
                dialog.CancelCommand.Execute(null);
            }
        });

        _navigator.PropertyChanged += OnNavigatorChanged;
        _dialogs.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ActiveDialog));
        _toasts.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ToastText));
        _session.PropertyChanged += OnSessionChanged;
    }

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

    public StatusIndicator SidebarPhoneStatus => StatusTexts.Phone(_session.Presence);

    public IDialogViewModel? ActiveDialog => _dialogs.Current;

    public string? ToastText => _toasts.Text;

    public ICommand NavigateCommand { get; }

    public ICommand EscapeCommand { get; }

    public void Navigate(AppPage page) => _navigator.Navigate(page);

    public Task OnWindowRenderedAsync()
    {
        if (_startedOnce)
        {
            return Task.CompletedTask;
        }

        _startedOnce = true;
        return _session.StartAsync();
    }

    public Task OnWindowClosingAsync() => _session.StopAsync();

    private void OnNavigatorChanged(object? sender, PropertyChangedEventArgs change)
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(CurrentPage));
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(IDesktopSession.State) or nameof(IDesktopSession.FaultCode):
                OnPropertyChanged(nameof(SidebarRelayStatus));
                break;

            case nameof(IDesktopSession.Presence):
                OnPropertyChanged(nameof(SidebarPhoneStatus));
                break;

            default:
                break;
        }
    }
}
