using System.ComponentModel;
using System.Windows.Input;

namespace LiveCue.Desktop;

/// <summary>The UI thread. The only way a view model gets work onto it.</summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    /// <summary>Queues the action. Returns at once. An exception in the action is routed to the session's
    /// diagnostic handler, never to the caller.</summary>
    void Post(Action action);
}

public interface IClipboard
{
    /// <summary>False when the clipboard is unavailable. Never throws.</summary>
    bool TrySetText(string text);
}

/// <summary>Opens something outside the app. Never throws.</summary>
public interface IExternalLauncher
{
    bool OpenUrl(Uri url);

    bool OpenFile(string path);
}

public interface IToastHost
{
    /// <summary>One toast at a time; a new one replaces the current one (DS 4.11).</summary>
    void Show(string text);
}

public interface INavigator
{
    AppPage Current { get; }

    void Navigate(AppPage page);
}

/// <summary>A dialog shown in the window (DS 4.10). Escape runs CancelCommand.</summary>
public interface IDialogViewModel : INotifyPropertyChanged
{
    string Title { get; }

    /// <summary>True while the confirmed action runs: the confirm button is busy, everything else and
    /// Escape are disabled.</summary>
    bool IsBusy { get; }

    ICommand CancelCommand { get; }

    /// <summary>Raised when the dialog is finished, confirmed or not.</summary>
    event EventHandler? Closed;
}

public interface IDialogHost
{
    IDialogViewModel? Current { get; }

    /// <summary>Shows the dialog over a scrim, traps focus, and restores focus to the invoking control
    /// when it closes. A second dialog while one is open is ignored.</summary>
    void Show(IDialogViewModel dialog);
}
