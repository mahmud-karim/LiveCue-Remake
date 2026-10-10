using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiveCue.Desktop.Composition;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.Interop;
using LiveCue.Desktop.Shell;
using LiveCue.Desktop.Theming;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Protocol;
using static LiveCue.Desktop.SelfTest.VisualChecks;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// <c>LiveCueRemake.exe --self-test [--screenshots &lt;folder&gt;] [--scale 1|1.5]</c> (architecture 3.13).
///
/// Builds the real window on the real view models, the real session and the real supervisor, with a
/// scripted relay and scripted hosting parts. No port is bound, no Tailscale or Codex command runs,
/// nothing is read from or written to the data folder, and no network is used. It feeds synthetic
/// events, checks what the window shows, renders each scene to a PNG at the default and at the minimum
/// window size, prints "Desktop self-test passed" and exits 0. The first failed check prints its name
/// and exits 1.
///
/// First version (foundation): the spike's run, as far as the foundation's view models carry it: the
/// stopped window, the four pages, a start, a phone, the contrast theme, the activity log, a stop and
/// a port-in-use fault, with the look and crisp-text checks on every scene. The fifteen scenes, the
/// named assertions and the 30 screenshots of W-design 5.13 are work package WP-W11.
///
/// The window is shown off-screen and never takes the focus. The PNGs come from WPF's own software
/// renderer (RenderTargetBitmap), which draws the same tree with the same brushes and effects but
/// with greyscale text. They are not a capture of the screen: WPF presents nothing for a window that
/// is off-screen, or while the display is asleep. So ClearType on the real window is protected by the
/// crisp-text rule checked on every scene, not by pixels.
/// </summary>
internal static class SelfTestRunner
{
    public const int ExitPassed = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;
    public const int ExitTimedOut = 3;

    public const string PassedLine = "Desktop self-test passed";

    public static async Task<int> RunAsync(App app, AppEnvironment environment)
    {
        var log = new SelfTestLog(environment.ScreenshotDirectory);

        // A self-test that hangs must still end, or a CI job would wait for its own timeout (W-12).
        var deadline = ProtocolConstants.SelfTestWatchdog;
        using var watchdog = new Timer(
            _ =>
            {
                log.Write("Desktop self-test FAILED: no result after " + deadline.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s");
                log.Save();
                Environment.Exit(ExitTimedOut);
            },
            null,
            deadline,
            Timeout.InfiniteTimeSpan);

        Exception? dispatcherFailure = null;
        app.DispatcherUnhandledException += (_, unhandled) =>
        {
            dispatcherFailure ??= unhandled.Exception;
            unhandled.Handled = true;
        };

        try
        {
            await new SelfTestSession(app, environment, log).RunAsync();
            if (dispatcherFailure is not null)
            {
                throw new SelfTestFailedException("no exception to reach the dispatcher, but one did: " + dispatcherFailure);
            }

            log.Write(PassedLine);
            return ExitPassed;
        }
#pragma warning disable CA1031 // The self-test reports every failure as a non-zero exit code; nothing may escape.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            log.Write("Desktop self-test FAILED: " + exception.Message);
            if (exception is not SelfTestFailedException)
            {
                log.Write(exception.ToString());
            }

            return ExitFailed;
        }
        finally
        {
            log.Save();
        }
    }
}

/// <summary>One run of the self-test.</summary>
internal sealed partial class SelfTestSession(App app, AppEnvironment environment, SelfTestLog log)
{
    private const double OffScreen = -30000;
    private const string TestEndpoint = "https://pc.example.test:11443/";
    private const string PortInUse =
        "Port 47841 is already in use. Close the other LiveCue window, or change the relay port in Settings, then start the relay.";

    private static readonly Size DefaultSize = new(1240, 660);
    private static readonly Size MinimumSize = new(960, 620);
    private static readonly Size BeyondAnyDesktop = new(4096, 2304);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly List<string> _screenshots = [];

    public async Task RunAsync()
    {
        var themes = app.Themes ?? throw new InvalidOperationException("The theme manager is not ready.");

        // Screenshots show Daylight whatever this PC is set to; the contrast look is captured on purpose below.
        themes.Apply(AppTheme.Daylight);

        var clock = new ManualClock(new DateTimeOffset(2026, 10, 10, 9, 14, 2, TimeSpan.Zero));
        var script = new RelayScript(clock);
        var hosting = new ScriptedHosting(clock, script);
        var clipboard = new RecordingClipboard();
        var launcher = new RecordingLauncher();

        await using var services = AppServices.Create(
            hosting.Services, new WpfUiDispatcher(app.Dispatcher), clock, clipboard, launcher);

        var window = ShowWindow(services.Shell);
        try
        {
            await ExerciseAsync(window, services, script, themes);
        }
        finally
        {
            window.Close();
        }

        Require(hosting.Processes.Calls == 0, "no process to be started");
        Require(hosting.Files.Touches == 0, "no file to be read or written");
        Require(hosting.Mappings.Executions == 0, "no Tailscale mapping command to be asked for");
        Require(clipboard.Texts.Count == 0 && launcher.Opened.Count == 0, "the clipboard and the launcher to be left alone");
        Require(!Directory.Exists(ScriptedHosting.UnusedDataDirectory), "nothing to be written where the data folder would be");
        log.Write("isolation: scripted relay and hosting, no port bound, no process, no file touched, no data folder used");
        log.Write($"screenshots: {_screenshots.Count}" + (environment.ScreenshotDirectory is null ? " (none requested)" : string.Empty));
    }

    private async Task ExerciseAsync(MainWindow window, AppServices services, RelayScript script, ThemeManager themes)
    {
        var shell = services.Shell;
        var session = services.Session;
        var root = (FrameworkElement)window.Content;

        await SettleAsync(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        log.Write($"window: dpi scale {dpi.DpiScaleX:0.##}, output scale {environment.Scale.ToString(CultureInfo.InvariantCulture)}, text family \"{DescribeTextFont(window)}\"");

        // Stopped: what the window shows at launch. Nothing has been started.
        Require(shell.SelectedPage == AppPage.Live, "the Live view to be selected at launch");
        Require(Text(root, AutomationIds.RelayStatusText) == "Stopped", "the relay status to read \"Stopped\" at launch");
        Require(Text(root, AutomationIds.SidebarRelayStatus) == "Stopped", "the sidebar to read \"Stopped\" at launch");
        Require(Text(root, AutomationIds.PhoneStatusText) == "iPhone not connected", "the phone status to read \"iPhone not connected\" at launch");
        Require(Text(root, AutomationIds.PcAddressText) == "Shown when the relay is running", "the PC address placeholder before the first start");
        Require(Button(root, AutomationIds.StartRelayButton) is { IsVisible: true, IsEnabled: true }, "Start relay to be offered while stopped");
        Require(Button(root, AutomationIds.PauseRelayButton) is { IsVisible: false }, "Pause relay to be hidden while stopped");
        Require(Button(root, AutomationIds.StopRelayButton) is { IsVisible: false }, "Stop relay to be hidden while stopped");
        Require(Text(root, AutomationIds.TranscriptText) == "Your next question will appear here.", "the transcript placeholder");
        Require(script.Created == 0, "no relay to exist before Start relay");
        var stopped = await CaptureAsync(window, root, "live-stopped");
        CheckGroundAndGlass(window, root, stopped);
        await CheckReferenceCompositesAsync(window, root);
        await CheckLayoutBeyondDesktopAsync(window, root);

        await ShowAsync(window, shell, AppPage.Pairing);
        Require(Button(root, AutomationIds.ShowQrButton) is { IsEnabled: false }, "Show QR to be disabled until the relay is ready");
        Require(VisibleTexts(root).Contains("Start the relay to show the QR"), "the QR tile's placeholder while the relay is stopped");
        await CaptureAsync(window, root, "pairing-empty");

        await ShowAsync(window, shell, AppPage.Activity);
        Require(Button(root, AutomationIds.ClearActivityButton) is { IsEnabled: false }, "Clear activity to be disabled while the log is empty");
        Require(VisibleTexts(root).Contains("No activity yet"), "the empty state of the activity view");
        await CaptureAsync(window, root, "activity-empty");

        await ShowAsync(window, shell, AppPage.Settings);
        Require(VisibleTexts(root).Contains("Private tailnet"), "the settings view to show the Private tailnet mode");
        Require(VisibleTexts(root).Contains("47841"), "the settings view to show relay port 47841");
        await CaptureAsync(window, root, "settings-private");

        // Started: the scripted checks pass, the scripted relay starts, then a phone is seen.
        await ShowAsync(window, shell, AppPage.Live);
        Press(root, AutomationIds.StartRelayButton);
        await WaitForAsync(window, () => session.State == RelayUiState.Ready, "the relay state to become Ready after Start relay");
        Require(script.Current is not null && services.Hosting.Supervisor.State == SupervisorState.Running, "the supervisor to be running the scripted relay");
        Require(Text(root, AutomationIds.RelayStatusText) == "Ready", "the relay status to read \"Ready\"");
        Require(Text(root, AutomationIds.SidebarRelayStatus) == "Ready", "the sidebar to read \"Ready\"");
        Require(Text(root, AutomationIds.PcAddressText) == TestEndpoint, "the PC address to be the advertised endpoint");
        Require(Button(root, AutomationIds.StartRelayButton) is { IsVisible: false }, "Start relay to be replaced while the relay runs");
        Require(Button(root, AutomationIds.PauseRelayButton) is { IsVisible: true, Content: "Pause relay" }, "Pause relay to be offered while the relay runs");
        Require(Button(root, AutomationIds.StopRelayButton) is { IsVisible: true, IsEnabled: true }, "Stop relay to be offered while the relay runs");

        script.PhoneSeen();
        await WaitForAsync(window, () => session.Presence == PhonePresence.Connected, "a phone-seen event to reach the session");
        Require(Text(root, AutomationIds.PhoneStatusText) == "iPhone connected", "a phone-seen event to show \"iPhone connected\"");
        Require(Text(root, AutomationIds.SidebarPhoneStatus) == "iPhone connected", "the sidebar to show \"iPhone connected\"");
        await CaptureAsync(window, root, "live-ready");

        // Pause and resume go through the relay and come back as events.
        Press(root, AutomationIds.PauseRelayButton);
        await WaitForAsync(window, () => session.State == RelayUiState.Paused, "Pause relay to pause the relay");
        Require(Text(root, AutomationIds.RelayStatusText) == "Paused", "the relay status to read \"Paused\"");
        Require(Button(root, AutomationIds.PauseRelayButton) is { Content: "Resume relay" }, "the button to read \"Resume relay\" while paused");
        Press(root, AutomationIds.PauseRelayButton);
        await WaitForAsync(window, () => session.State == RelayUiState.Ready, "Resume relay to resume the relay");

        // The contrast-theme dictionary, forced (system colours stay whatever this PC uses).
        themes.Apply(AppTheme.HighContrast);
        await SettleAsync(window);
        Require(CountEffects(root) == 0, "no shadow anywhere under the contrast theme");
        await CaptureAsync(window, root, "live-ready-high-contrast", sizes: [DefaultSize]);
        themes.Apply(AppTheme.Daylight);
        await SettleAsync(window);
        Require(CountEffects(root) > 0, "shadows to return with Daylight");

        // The mapping is present in this script, so the connection is ready and Show QR is offered.
        await ShowAsync(window, shell, AppPage.Pairing);
        Require(session.IsConnectionReady, "the connection to be ready once the scripted mapping was inspected");
        Require(Button(root, AutomationIds.ShowQrButton) is { IsEnabled: true }, "Show QR to be enabled once the connection is ready");

        // Activity: the log has the start line; a diagnostic is shown with its prefix; Clear empties it.
        script.Diagnostic("An event handler failed (InvalidOperationException).");
        await WaitForAsync(window, () => shell.Activity.Entries.Count == 2, "the diagnostic to reach the activity log");
        await ShowAsync(window, shell, AppPage.Activity);
        var activity = VisibleTexts(root);
        Require(activity.Any(text => text.Contains("Relay started. Waiting for your iPhone.", StringComparison.Ordinal)), "the activity log to record the start");
        Require(activity.Any(text => text.Contains("Relay diagnostic: ", StringComparison.Ordinal)), "the activity log to prefix a diagnostic");
        Require(activity.Any(text => text.Contains("09:14:02", StringComparison.Ordinal)), "each activity entry to start with its time");
        Require(VisibleTexts(root).Contains("2 entries"), "the footer to count the entries");
        await CaptureAsync(window, root, "activity-filled");
        Press(root, AutomationIds.ClearActivityButton);
        await SettleAsync(window);
        Require(shell.Activity.IsEmpty && VisibleTexts(root).Contains("No activity yet"), "Clear activity to empty the log");

        // Stop: the stopped layout. The PC address is kept; the phone goes back to "not connected".
        await ShowAsync(window, shell, AppPage.Live);
        Press(root, AutomationIds.StopRelayButton);
        await WaitForAsync(window, () => session.State == RelayUiState.Stopped, "Stop relay to stop the relay");
        Require(Text(root, AutomationIds.RelayStatusText) == "Stopped", "the relay status to read \"Stopped\" after Stop relay");
        Require(Text(root, AutomationIds.PhoneStatusText) == "iPhone not connected", "the phone to reset when the relay stops");
        Require(Text(root, AutomationIds.PcAddressText) == TestEndpoint, "the PC address to stay after Stop relay");
        Require(Button(root, AutomationIds.StartRelayButton) is { IsVisible: true, IsEnabled: true }, "Start relay to be offered again");
        Require(services.Hosting.Supervisor.State == SupervisorState.Stopped, "the supervisor to have stopped the relay");

        // A port-in-use fault: the stopped layout, and the fault text, with the port, stays.
        script.NextStartFault = RelayFaultCode.PortInUse;
        Press(root, AutomationIds.StartRelayButton);
        await WaitForAsync(window, () => session.State == RelayUiState.Faulted, "a port-in-use fault to leave the relay faulted");
        Require(Text(root, AutomationIds.RelayStatusText) == PortInUse, "the fault to show its text, with the port, as the status");
        Require(Text(root, AutomationIds.SidebarRelayStatus) == "Relay stopped", "the sidebar to read \"Relay stopped\" after a failed start");
        Require(Button(root, AutomationIds.StartRelayButton) is { IsVisible: true, IsEnabled: true }, "a fault to return the stopped layout with Start relay");
        Require(shell.Activity.Entries.Any(entry => entry.Message == PortInUse), "the fault to be recorded in the activity log");
        await CaptureAsync(window, root, "fault-port-in-use");

        // A new start is a new relay instance, and a successful one clears the fault text.
        Press(root, AutomationIds.StartRelayButton);
        await WaitForAsync(window, () => session.State == RelayUiState.Ready, "the next start to succeed");
        Require(Text(root, AutomationIds.RelayStatusText) == "Ready", "the next start to clear the fault text");
        Require(script.Created == 3, "every start to create a new relay instance");
        Press(root, AutomationIds.StopRelayButton);
        await WaitForAsync(window, () => session.State == RelayUiState.Stopped, "the relay to be stopped at the end");
    }

    /// <summary>
    /// Creates the window as a 96-DPI window, off-screen, without activating it. At 96 DPI one layout
    /// unit is one pixel, so the PNGs are pixel-exact at 1240 x 660 on any monitor. The hook lifts the
    /// system rule that a window cannot be larger than the desktop (hosted CI desktops are 1024 x 768).
    /// </summary>
    private static MainWindow ShowWindow(ShellViewModel shell) => InWindowDpiContext(() =>
    {
        var window = new MainWindow(shell)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = OffScreen,
            Top = OffScreen,
            ShowActivated = false,
            ShowInTaskbar = false,
            KeepActiveAppearance = true,
            DrivesRelay = false,
        };
        window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(AllowSizesBeyondTheDesktop);
        window.Show();
        return window;
    });

    /// <summary>
    /// Runs <paramref name="action"/> with the thread in the DPI-unaware context the self-test window
    /// was created in. Window coordinates are interpreted in the calling thread's context, so every
    /// call that creates, moves or sizes that window has to be made from the same one.
    /// </summary>
    private static T InWindowDpiContext<T>(Func<T> action)
    {
        var previousContext = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextUnaware);
        try
        {
            return action();
        }
        finally
        {
            if (previousContext != IntPtr.Zero)
            {
                NativeMethods.SetThreadDpiAwarenessContext(previousContext);
            }
        }
    }

    private static IntPtr AllowSizesBeyondTheDesktop(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmGetMinMaxInfo)
        {
            var info = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
            info.MaxTrackSize = new NativeMethods.Point { X = 16000, Y = 16000 };
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
        }

        return IntPtr.Zero;
    }

    private static async Task ShowAsync(MainWindow window, ShellViewModel shell, AppPage page)
    {
        shell.Navigate(page);
        await SettleAsync(window);
        Require(shell.SelectedPage == page, $"the {page} view to be selected");
    }

    /// <summary>Clicks the button with that automation id the way a user would: through its command.</summary>
    private static void Press(FrameworkElement root, string automationId)
    {
        var button = Button(root, automationId);
        Require(button is { IsVisible: true, IsEnabled: true }, $"{automationId} to be there to press");
        Require(button!.Command is not null && button.Command.CanExecute(button.CommandParameter), $"{automationId} to be able to run");
        button.Command!.Execute(button.CommandParameter);
    }

    /// <summary>
    /// Waits until the supervisor's events have reached the session and the window shows them. Events
    /// travel from a thread-pool task to the dispatcher, so there is nothing to await but the result.
    /// </summary>
    private static async Task WaitForAsync(MainWindow window, Func<bool> condition, string expectation)
    {
        var until = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Require(DateTime.UtcNow < until, expectation);
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        await SettleAsync(window);
    }

    private static async Task ResizeAsync(MainWindow window, FrameworkElement root, Size size)
    {
        InWindowDpiContext(() =>
        {
            window.Width = size.Width;
            window.Height = size.Height;
            return true;
        });
        await SettleAsync(window);

        Require(
            Math.Abs(root.ActualWidth - size.Width) < 0.01 && Math.Abs(root.ActualHeight - size.Height) < 0.01,
            $"the window content to be laid out at {size.Width} x {size.Height}, not {root.ActualWidth} x {root.ActualHeight}");
    }

    /// <summary>
    /// Renders the current scene at each size, after checking the rules that hold for every scene:
    /// crisp text, shadows on caster layers only, and no token-shaped text anywhere. Returns the frame
    /// at the first size, at one pixel per layout unit.
    /// </summary>
    private async Task<BitmapSource> CaptureAsync(MainWindow window, FrameworkElement root, string scene, Size[]? sizes = null)
    {
        BitmapSource? first = null;
        foreach (var size in sizes ?? [DefaultSize, MinimumSize])
        {
            await ResizeAsync(window, root, size);

            var texts = RequireCrispText(root);
            var casters = RequireEffectsOnlyOnCasters(root);
            Require(texts > 0, "the scene to show some text");
            Require(!VisibleTexts(root).Any(text => TokenShape().IsMatch(text)), "no visible text to look like a pairing token");

            var frame = Render(root, 1);
            first ??= frame;
            var fileName = Save(Math.Abs(environment.Scale - 1) < 0.001 ? frame : Render(root, environment.Scale), scene, size);
            log.Write($"scene: {fileName} ({texts} text blocks crisp, {casters} shadow casters)");
        }

        await ResizeAsync(window, root, DefaultSize);
        return first!;
    }

    private static RenderTargetBitmap Render(FrameworkElement root, double scale)
    {
        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(root.ActualWidth * scale),
            (int)Math.Round(root.ActualHeight * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private string Save(BitmapSource bitmap, string scene, Size size)
    {
        var scaleSuffix = Math.Abs(environment.Scale - 1) < 0.001
            ? string.Empty
            : "@" + environment.Scale.ToString(CultureInfo.InvariantCulture) + "x";
        var fileName = $"{scene}-{size.Width}x{size.Height}{scaleSuffix}.png";
        if (environment.ScreenshotDirectory is { } directory)
        {
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, fileName));
            encoder.Save(stream);
            _screenshots.Add(fileName);
        }

        return fileName;
    }

    /// <summary>
    /// The ground is not flat (the blooms are there) and glass reads lighter than the ground beside
    /// it. Sample points are taken from the laid-out elements, not from fixed coordinates.
    /// </summary>
    private void CheckGroundAndGlass(MainWindow window, FrameworkElement root, BitmapSource frame)
    {
        // Top left is the heart of bloom 1 (#9DC1F5); the top centre is far from every bloom centre.
        var bloom = PixelAt(frame, 3, 3);
        var middle = PixelAt(frame, frame.PixelWidth / 2, 3);
        Require(bloom.B > bloom.R + 40, $"bloom 1 to tint the top-left corner blue, not {Hex(bloom)}");
        Require(Luminance(middle) > Luminance(bloom) + 0.05, $"the ground to get lighter away from bloom 1 ({Hex(bloom)} to {Hex(middle)})");

        // Inside the sidebar, in its empty middle, against the ground just left of it at the same height.
        var (x, y) = EmptySpotInSidebar(window, root);
        var glass = PixelAt(frame, x, y);
        var ground = PixelAt(frame, 6, y);
        Require(Luminance(glass) > Luminance(ground) + 0.02, $"the sidebar glass ({Hex(glass)}) to be lighter than the ground beside it ({Hex(ground)})");

        log.Write($"look: bloom {Hex(bloom)}, ground {Hex(middle)}, sidebar glass {Hex(glass)} over ground {Hex(ground)}");
    }

    /// <summary>
    /// Design system checklist item 1: with the ground forced flat, a pixel inside a panel, below the
    /// sheen, is #EBEEF4 within 2 per channel. That figure includes the panel's own shadow showing
    /// through its translucent fill, so it proves the fill, the shadow and their compositing at once.
    /// </summary>
    private async Task CheckReferenceCompositesAsync(MainWindow window, FrameworkElement root)
    {
        string[] bloomKeys = ["GroundBloom1Color", "GroundBloom2Color", "GroundBloom3Color", "GroundBloom4Color"];
        foreach (var key in bloomKeys)
        {
            app.Resources[key] = Colors.Transparent;
        }

        try
        {
            await SettleAsync(window);
            var frame = Render(root, 1);
            var (x, y) = EmptySpotInSidebar(window, root);
            var panel = PixelAt(frame, x, y);
            var ground = PixelAt(frame, frame.PixelWidth / 2, 3);

            Require(IsWithin(ground, Color.FromRgb(0xE6, 0xEC, 0xF5), 1), $"the flat ground to be #E6ECF5, not {Hex(ground)}");
            Require(IsWithin(panel, Color.FromRgb(0xEB, 0xEE, 0xF4), 2), $"a panel on the flat ground to composite to #EBEEF4 within 2, not {Hex(panel)}");
            log.Write($"reference composites: flat ground {Hex(ground)}, panel {Hex(panel)} (expected #EBEEF4)");
        }
        finally
        {
            foreach (var key in bloomKeys)
            {
                app.Resources.Remove(key);
            }

            await SettleAsync(window);
        }
    }

    /// <summary>
    /// The screenshots must not depend on the size of the desktop the test happens to run on. This
    /// lays the window out larger than any desktop and checks the content really got that size.
    /// </summary>
    private async Task CheckLayoutBeyondDesktopAsync(MainWindow window, FrameworkElement root)
    {
        await ResizeAsync(window, root, BeyondAnyDesktop);
        log.Write($"layout: holds at {BeyondAnyDesktop.Width} x {BeyondAnyDesktop.Height}, beyond the desktop size");
        await ResizeAsync(window, root, DefaultSize);
    }

    /// <summary>A point inside the sidebar panel with nothing drawn on it: between the last row and the status card.</summary>
    private static (int X, int Y) EmptySpotInSidebar(MainWindow window, FrameworkElement root)
    {
        var sidebar = (FrameworkElement)window.FindName("Sidebar");
        var bounds = sidebar.TransformToAncestor(root).TransformBounds(new Rect(sidebar.RenderSize));
        return ((int)(bounds.Left + (bounds.Width / 2)), (int)(bounds.Top + (bounds.Height * 0.55)));
    }

    /// <summary>Lets bindings, layout, rendering and the short state animations finish.</summary>
    private static async Task SettleAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(TimeSpan.FromMilliseconds(220));
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    private static FrameworkElement? Find(FrameworkElement root, string automationId) =>
        Descendants(root).OfType<FrameworkElement>()
            .FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == automationId);

    /// <summary>The text a visible element with that automation id shows; null when there is none.</summary>
    private static string? Text(FrameworkElement root, string automationId) => Find(root, automationId) switch
    {
        TextBlock { IsVisible: true } text => text.Text,
        StatusIndicatorView { IsVisible: true } indicator => indicator.Text,
        _ => null,
    };

    private static ButtonBase? Button(FrameworkElement root, string automationId) => Find(root, automationId) as ButtonBase;

    private static List<string> VisibleTexts(FrameworkElement root) =>
        Descendants(root).OfType<TextBlock>()
            .Where(text => text.IsVisible)
            .Select(text => string.IsNullOrEmpty(text.Text)
                ? string.Concat(text.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text))
                : text.Text)
            .ToList();

    /// <summary>Which font file the text family really resolved to, and whether a weight was faked.</summary>
    private static string DescribeTextFont(Window window)
    {
        var typeface = new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out var glyphs))
        {
            return "unresolved";
        }

        var family = glyphs.Win32FamilyNames.Values.FirstOrDefault() ?? "unknown";
        var face = glyphs.Win32FaceNames.Values.FirstOrDefault() ?? "unknown";
        return $"{family} {face}, simulations {glyphs.StyleSimulations}";
    }

    [GeneratedRegex("[A-Za-z0-9_-]{43}")]
    private static partial Regex TokenShape();
}
