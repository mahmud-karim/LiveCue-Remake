using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LiveCue.Desktop.Settings;

/// <summary>The Settings page. Its data context is an <see cref="ISettingsViewModel"/>.</summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Opening the page runs a check only if none has run yet (screens 5.2).</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            settings.OnOpened();
        }
    }

    /// <summary>
    /// "Remove…" on the warning of one stale mapping. A banner's button has no parameter of its own,
    /// so the row it belongs to is read here and handed to the page's one removal command, which
    /// opens the confirmation dialog. Nothing is removed by this click.
    /// </summary>
    private void OnStaleRemoveClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is ButtonBase { DataContext: StaleMappingRow row }
            && DataContext is ISettingsViewModel settings
            && settings.RemoveMappingCommand.CanExecute(row.Entry))
        {
            settings.RemoveMappingCommand.Execute(row.Entry);
            e.Handled = true;
        }
    }
}
