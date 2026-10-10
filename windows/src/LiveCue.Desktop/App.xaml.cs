using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveCue.Desktop.Composition;
using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Shell;
using LiveCue.Desktop.Text;
using LiveCue.Desktop.Theming;
using LiveCue.Relay.Protocol;

namespace LiveCue.Desktop;

/// <summary>Application entry: either the normal window or the self-test, never both (W-design 5.12).</summary>
public partial class App : Application
{
    private const int ExitStartupFailed = 1;

    private AppServices? _services;

    internal ThemeManager? Themes { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        // Design system 4.23: tooltips appear after 500 ms, everywhere.
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(500));

        AppEnvironment environment;
        try
        {
            environment = AppEnvironment.Parse(e.Args);
        }
        catch (ArgumentException exception)
        {
            SelfTestConsole.WriteError(exception.Message);
            Shutdown(SelfTestRunner.ExitUsage);
            return;
        }

        Themes = new ThemeManager(Resources);

        if (environment.IsSelfTest)
        {
            // The self-test owns the lifetime: closing its window must not end the run early. What it
            // renders must not depend on the moment it looks, so nothing travels or fades; and it shows
            // Daylight at 100% whatever this PC is set to, so the system's look is not followed.
            SystemPreferences.PinAnimations(false);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await SelfTestRunner.RunAsync(this, environment);
            Shutdown(exitCode);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            Themes.FollowSystem();
            _services = AppServices.CreateForUser(new WpfUiDispatcher(Dispatcher));
            var window = new MainWindow(_services.Shell);
            _services.Attach(window);
            MainWindow = window;
            window.Show();
        }
#pragma warning disable CA1031 // Whatever stops the start-up is told to the user; only its type name is shown (R-33).
        catch (Exception exception)
#pragma warning restore CA1031
        {
            MessageBox.Show(
                ShellText.StartupFailed + Environment.NewLine + exception.GetType().Name,
                ShellText.WindowTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(ExitStartupFailed);
        }
    }

    /// <summary>
    /// An exception that reaches the dispatcher is handled, reported as a "Relay diagnostic: " line with
    /// the type name only, and the app continues (W-design 5.12).
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            _services?.ReportUnhandled(e.Exception);
        }
#pragma warning disable CA1031 // Reporting a failure must not become one.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Closing the window has already stopped the relay. This releases what is left; the wait is
        // bounded so that a stuck stop cannot keep the process alive, and the work runs on the thread
        // pool, so blocking here cannot deadlock.
        if (_services is { } services)
        {
            Task.Run(async () => await services.DisposeAsync()).Wait(ProtocolConstants.WindowCloseWait);
        }

        base.OnExit(e);
    }
}
