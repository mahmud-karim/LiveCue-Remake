using System.Windows;
using System.Windows.Controls;
using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Theming;

namespace LiveCue.Desktop;

/// <summary>Application entry: either the normal window or the self-test, never both.</summary>
public partial class App : Application
{
    /// <summary>The relay's own stop takes up to 2.5 s; this leaves room for it and no more.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    private AppServices? _services;

    internal ThemeManager? Themes { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        // Design system 4.23: tooltips appear after 500 ms, everywhere.
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(500));

        AppArguments arguments;
        try
        {
            arguments = AppArguments.Parse(e.Args);
        }
        catch (ArgumentException exception)
        {
            SelfTestConsole.WriteError(exception.Message);
            Shutdown(SelfTestRunner.ExitUsage);
            return;
        }

        Themes = new ThemeManager(Resources);

        if (arguments.IsSelfTest)
        {
            // The self-test owns the lifetime: closing its window must not end the run early.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await SelfTestRunner.RunAsync(this, arguments);
            Shutdown(exitCode);
            return;
        }

        Themes.FollowSystem();
        _services = AppServices.CreateForUser(new WpfUiDispatcher(Dispatcher));
        MainWindow = new MainWindow(_services.Shell);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Closing the window always stops the relay, with no confirmation. The wait is bounded so a
        // stuck request cannot keep the process alive; the work runs on the thread pool, so blocking
        // here cannot deadlock.
        if (_services is { } services)
        {
            Task.Run(async () => await services.DisposeAsync()).Wait(ShutdownGrace);
        }

        base.OnExit(e);
    }
}
