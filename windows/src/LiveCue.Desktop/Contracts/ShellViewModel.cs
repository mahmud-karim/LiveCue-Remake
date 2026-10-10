using System.ComponentModel;
using System.Windows.Input;

namespace LiveCue.Desktop;

/// <summary>The window: navigation, the sidebar status card, the dialog and toast layers.</summary>
public interface IShellViewModel : INotifyPropertyChanged, INavigator
{
    /// <summary>Two-way: the selected sidebar row.</summary>
    AppPage SelectedPage { get; set; }

    ILiveViewModel Live { get; }

    IPairingViewModel Pairing { get; }

    IActivityViewModel Activity { get; }

    ISettingsViewModel Settings { get; }

    /// <summary>The view model of SelectedPage; the page host's content.</summary>
    object CurrentPage { get; }

    /// <summary>Sidebar card, "Relay" line: the short labels of screens 1.3.</summary>
    StatusIndicator SidebarRelayStatus { get; }

    /// <summary>Sidebar card, "iPhone" line: the same indicator as the Live page.</summary>
    StatusIndicator SidebarPhoneStatus { get; }

    IDialogViewModel? ActiveDialog { get; }

    string? ToastText { get; }

    /// <summary>Parameter: an <see cref="AppPage"/>. Bound to Ctrl+1 to Ctrl+4 and the sidebar rows.</summary>
    ICommand NavigateCommand { get; }

    /// <summary>Escape: cancels the open dialog, if any and if it is not busy.</summary>
    ICommand EscapeCommand { get; }

    /// <summary>Called once by the window on its first ContentRendered: starts the relay (WIN-2).</summary>
    Task OnWindowRenderedAsync();

    /// <summary>Called by the window's Closing handler: stops the relay, bounded (section 5.12).</summary>
    Task OnWindowClosingAsync();
}
