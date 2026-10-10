using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveCue.Desktop.Shell;

/// <summary>
/// The one dialog the window can show (W-design 5.9). A second dialog while one is open is ignored; the
/// dialog leaves when it raises <see cref="IDialogViewModel.Closed"/>.
///
/// First version (foundation): which dialog is open. Moving the focus into the dialog, trapping it and
/// restoring it afterwards are work package WP-W08.
/// </summary>
internal sealed class DialogHost : ObservableObject, IDialogHost
{
    private IDialogViewModel? _current;

    public IDialogViewModel? Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public void Show(IDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (Current is not null)
        {
            return;
        }

        dialog.Closed += OnClosed;
        Current = dialog;
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
