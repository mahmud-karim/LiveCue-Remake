using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveCue.Desktop.Shell;

/// <summary>Which of the four pages is shown. View models navigate through <see cref="INavigator"/> only.</summary>
internal sealed class Navigator : ObservableObject, INavigator
{
    private AppPage _current = AppPage.Live;

    public AppPage Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public void Navigate(AppPage page)
    {
        if (Enum.IsDefined(page))
        {
            Current = page;
        }
    }
}
