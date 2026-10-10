using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveCue.Desktop.ViewModels;

/// <summary>The four views of the sidebar, in order.</summary>
public enum PageId
{
    Live,
    Pairing,
    Activity,
    Settings,
}

/// <summary>One sidebar row: its label, the resource key of its icon, its automation id and the page it opens.</summary>
public sealed record NavigationItem(PageId Id, string Title, string IconKey, string AutomationId, PageViewModel Page)
{
    /// <summary>The title: what a screen reader announces for the row.</summary>
    public override string ToString() => Title;
}

/// <summary>Base of the page view models. The view for each is chosen by type in Views/Pages.xaml.</summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>The view's name, shown in the page header.</summary>
    public abstract string Title { get; }
}

/// <summary>The window: the sidebar's pages, the selected one, and the relay state of the status card.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(RelayViewModel relay, ActivityLog activity, SettingsPageViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(settings);

        Relay = relay;
        Pages =
        [
            new NavigationItem(PageId.Live, "Live", "Icon.Wave", "NavLive", new LivePageViewModel(relay)),
            new NavigationItem(PageId.Pairing, "Pairing", "Icon.Qr", "NavPairing", new PairingPageViewModel(relay)),
            new NavigationItem(PageId.Activity, "Activity", "Icon.Activity", "NavActivity", new ActivityPageViewModel(activity)),
            new NavigationItem(PageId.Settings, "Settings", "Icon.Settings", "NavSettings", settings),
        ];
        _selectedPage = Pages[0];
    }

    private NavigationItem _selectedPage;

    public RelayViewModel Relay { get; }

    public IReadOnlyList<NavigationItem> Pages { get; }

    /// <summary>
    /// One view is always selected. A list box can clear its selection (Ctrl+click on the selected
    /// row); that null is refused, and the list is told afterwards to show the real selection again.
    /// </summary>
    public NavigationItem SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedPage, value);
            }
            else
            {
                SynchronizationContext.Current?.Post(_ => OnPropertyChanged(nameof(SelectedPage)), null);
            }
        }
    }

    public void Navigate(PageId page) => SelectedPage = Pages.First(item => item.Id == page);
}
