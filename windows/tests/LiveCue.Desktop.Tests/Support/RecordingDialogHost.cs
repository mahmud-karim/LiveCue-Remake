namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// A dialog host without a window: it remembers every dialog it was asked to show and behaves as the
/// real one does (a second dialog while one is open is ignored; a dialog leaves when it raises Closed).
/// </summary>
internal sealed class RecordingDialogHost : IDialogHost
{
    /// <summary>Every dialog that was shown, in order.</summary>
    public List<IDialogViewModel> Shown { get; } = [];

    /// <summary>Dialogs that were asked for while another was open, and ignored.</summary>
    public List<IDialogViewModel> Ignored { get; } = [];

    public IDialogViewModel? Current { get; private set; }

    public void Show(IDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (Current is not null)
        {
            Ignored.Add(dialog);
            return;
        }

        Shown.Add(dialog);
        Current = dialog;
        dialog.Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is IDialogViewModel dialog)
        {
            dialog.Closed -= OnClosed;
            if (ReferenceEquals(Current, dialog))
            {
                Current = null;
            }
        }
    }
}

/// <summary>A navigator without a window, for page view models that open another page.</summary>
internal sealed class RecordingNavigator : INavigator
{
    public List<AppPage> Visited { get; } = [];

    public AppPage Current { get; private set; } = AppPage.Live;

    public void Navigate(AppPage page)
    {
        Visited.Add(page);
        Current = page;
    }
}

/// <summary>A toast host without a window: it remembers every toast.</summary>
internal sealed class RecordingToastHost : IToastHost
{
    public List<string> Texts { get; } = [];

    public void Show(string text) => Texts.Add(text);
}
