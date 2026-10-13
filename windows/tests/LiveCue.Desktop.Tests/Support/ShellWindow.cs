using System.Windows;
using LiveCue.Desktop.Composition;
using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Shell;

namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// The real window on the real shell, session and supervisor, with the self-test's scripted relay and
/// hosting parts behind them: nothing is started, bound, read or written. The window is off-screen and
/// never takes the focus. Create and use it on the application's thread (<see cref="WpfApp"/>).
/// </summary>
internal sealed class ShellWindow : IDisposable
{
    public ShellWindow(bool show = true)
    {
        Clock = new ManualClock(new DateTimeOffset(2026, 10, 10, 9, 14, 2, TimeSpan.Zero));
        Script = new RelayScript(Clock);
        Hosting = new ScriptedHosting(Clock, Script);
        Services = AppServices.Create(
            Hosting.Services,
            new WpfUiDispatcher(Application.Current.Dispatcher),
            Clock,
            new RecordingClipboard(),
            new RecordingLauncher());
        Window = new MainWindow(Services.Shell) { DrivesRelay = false, KeepActiveAppearance = true };
        if (show)
        {
            WpfApp.ShowOffScreen(Window);
        }
    }

    public ManualClock Clock { get; }

    public RelayScript Script { get; }

    public ScriptedHosting Hosting { get; }

    public AppServices Services { get; }

    public ShellViewModel Shell => Services.Shell;

    public IDesktopSession Session => Services.Session;

    public MainWindow Window { get; }

    /// <summary>The window's content: what a screenshot shows.</summary>
    public FrameworkElement Root => (FrameworkElement)Window.Content;

    public T Named<T>(string name)
        where T : class =>
        Window.FindName(name) as T ?? throw new InvalidOperationException($"The window has no {typeof(T).Name} named {name}.");

    /// <summary>Waits, with the dispatcher running, until <paramref name="condition"/> holds.</summary>
    public static void Until(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, $"The window did not reach {what} in time.");
            WpfApp.Settle(TimeSpan.FromMilliseconds(10));
        }

        WpfApp.Settle();
    }

    public void Dispose()
    {
        Window.Close();
        Task.Run(async () => await Services.DisposeAsync()).Wait(TimeSpan.FromSeconds(10));
    }
}
