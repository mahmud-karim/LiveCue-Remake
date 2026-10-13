using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LiveCue.Desktop.Activity;
using LiveCue.Desktop.Live;
using LiveCue.Desktop.Pairing;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Shell;
using LiveCue.Desktop.Tests.Live;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Settings;

/// <summary>
/// The real window on the real shell and the real page view models, over a <see cref="FakeSession"/>
/// and the test's own file system: a test puts the session into any state and looks at what the
/// Settings page and the mapping dialog draw. Nothing is started, bound, read or written; the window
/// is off-screen and never takes the focus. Create and use it on the application's thread.
/// </summary>
internal sealed class SettingsWindow : IDisposable
{
    private readonly ToastHost _toasts;

    public SettingsWindow(RelaySettings? saved = null)
    {
        if (saved is not null)
        {
            Session.Settings = saved;
        }

        var ui = new ImmediateDispatcher();
        var navigator = new Navigator();
        var dialogs = new DialogHost();
        _toasts = new ToastHost(ui, Clock);
        var hosting = HostingFactory.Create(
            new SupervisorOptions(@"C:\LiveCue-tests\data", "1.0.0"),
            new HostingOverrides { Files = Files, Processes = Processes });

        Settings = new SettingsViewModel(Session, dialogs, Clipboard, _toasts, new FakeLauncher(), hosting, ui);
        Shell = new ShellViewModel(
            Session,
            new LiveViewModel(Session, navigator, dialogs, Clipboard, _toasts, Clock, ui),
            new PairingViewModel(Session, navigator, dialogs, Clipboard, _toasts, Clock, ui),
            new ActivityViewModel(Session, Clock),
            Settings,
            navigator,
            dialogs,
            _toasts);

        Window = new MainWindow(Shell) { DrivesRelay = false, KeepActiveAppearance = true };
        WpfApp.ShowOffScreen(Window);
    }

    public FakeSession Session { get; } = new();

    public TestFiles Files { get; } = new();

    public RecordingProcesses Processes { get; } = new();

    public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 10, 9, 14, 2, TimeSpan.Zero));

    public FakeClipboard Clipboard { get; } = new();

    public SettingsViewModel Settings { get; }

    public ShellViewModel Shell { get; }

    public MainWindow Window { get; }

    /// <summary>The window's content: what a screenshot shows.</summary>
    public FrameworkElement Root => (FrameworkElement)Window.Content;

    /// <summary>The page that is shown: the content of the window's page host.</summary>
    public FrameworkElement Page => (FrameworkElement)Window.FindName("PageHost");

    /// <summary>The card of the open dialog.</summary>
    public ContentControl DialogCard => (ContentControl)Window.FindName("DialogCard");

    public ScrollViewer Scroller =>
        WpfApp.Descendants(Page).OfType<ScrollViewer>().First(scroller => scroller.Name == "PageScroller");

    public void Open()
    {
        Shell.Navigate(AppPage.Settings);
        WpfApp.Settle(TimeSpan.FromMilliseconds(260));
    }

    /// <summary>A check that passed everything, and the Private mapping in that state.</summary>
    public void Checked(MappingState mapping = MappingState.Absent, string? codexVersion = "1.2.3", string? checkedVersion = "1.2.3")
    {
        if (Session.Settings.CodexContainmentCheckedVersion != checkedVersion)
        {
            Session.Settings = Session.Settings with { CodexContainmentCheckedVersion = checkedVersion };
        }

        Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>(), codexVersion: codexVersion);
        Session.Connection = PageParts.Inspection(mapping);
        WpfApp.Settle();
    }

    public void Resize(double width, double height)
    {
        Window.Width = width;
        Window.Height = height;
        WpfApp.Settle(TimeSpan.FromMilliseconds(40));
        Window.UpdateLayout();
    }

    /// <summary>Scrolls the page so that the named part is at the top of what is shown, or as near as the page allows.</summary>
    public void ScrollTo(string name)
    {
        var scroller = Scroller;
        scroller.ScrollToVerticalOffset(0);
        WpfApp.Settle();
        var content = (FrameworkElement)scroller.Content;
        var top = Named(name).TransformToAncestor(content).Transform(default).Y;
        scroller.ScrollToVerticalOffset(top);
        WpfApp.Settle();
    }

    /// <summary>
    /// Draws the scene at the default and at the minimum window size. With the environment variable
    /// <c>LIVECUE_TEST_SHOTS</c> set, both are written as PNG files for a person to compare with the mockup.
    /// </summary>
    public void Capture(string scene, Action<double, double>? atEachSize = null, string? scrollTo = null)
    {
        foreach (var (width, height) in new[] { (1240d, 660d), (960d, 620d) })
        {
            Resize(width, height);
            if (scrollTo is not null)
            {
                ScrollTo(scrollTo);
            }

            Assert.Equal((width, height), (Root.ActualWidth, Root.ActualHeight));
            WpfApp.Render(Root, $"{scene}-{width}x{height}.png");
            Assert.True(VisualChecks.RequireCrispText(Root) > 0);
            VisualChecks.RequireEffectsOnlyOnCasters(Root);
            atEachSize?.Invoke(width, height);
        }

        Resize(1240, 660);
        if (scrollTo is not null)
        {
            Scroller.ScrollToVerticalOffset(0);
            WpfApp.Settle();
        }
    }

    public FrameworkElement Named(string name) =>
        WpfApp.Descendants(Page).OfType<FrameworkElement>().First(element => element.Name == name);

    /// <summary>The element of the page with that automation id that is shown now; null when none is.</summary>
    public FrameworkElement? Find(string automationId) => WpfApp.Descendants(Page)
        .OfType<FrameworkElement>()
        .FirstOrDefault(element => element.IsVisible && AutomationProperties.GetAutomationId(element) == automationId);

    public T Shown<T>(string automationId)
        where T : FrameworkElement =>
        Find(automationId) as T
        ?? throw new InvalidOperationException($"The page shows no {typeof(T).Name} with the id {automationId}.");

    /// <summary>Every text under <paramref name="root"/> that is shown now: text blocks and text boxes alike.</summary>
    public static List<string> Texts(DependencyObject root) => WpfApp.Descendants(root)
        .OfType<FrameworkElement>()
        .Where(element => element.IsVisible)
        .Select(element => element switch
        {
            TextBlock block => block.Text,
            TextBox box => box.Text,
            _ => null,
        })
        .Where(text => !string.IsNullOrEmpty(text))
        .Select(text => text!)
        .ToList();

    public List<string> Texts() => Texts(Page);

    /// <summary>Where an element lies in the window, in layout units (one pixel each in this window).</summary>
    public Rect Bounds(FrameworkElement element) =>
        element.TransformToAncestor(Root).TransformBounds(new Rect(element.RenderSize));

    public void Dispose()
    {
        Window.Close();
        _toasts.Dispose();
    }
}
