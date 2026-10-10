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
using LiveCue.Desktop.Infrastructure;
using LiveCue.Desktop.Interop;
using LiveCue.Desktop.Theming;
using LiveCue.Desktop.ViewModels;
using LiveCue.Hosting;
using LiveCue.Relay;
using static LiveCue.Desktop.SelfTest.VisualChecks;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// <c>LiveCueRemake.exe --self-test [--screenshots &lt;folder&gt;] [--scale 1|1.5]</c> (architecture 3.13).
///
/// Builds the real window on the real view models and the real supervisor, with a scripted relay and
/// an in-memory settings store. No port is bound, no Tailscale or Codex command runs, nothing is read
/// from or written to the data folder, and no network is used. It feeds synthetic events, checks what
/// the window shows, renders each scene to a PNG at the default and at the minimum window size,
/// prints "Desktop self-test passed" and exits 0. The first failed check prints its name and exits 1.
///
/// The window is shown off-screen and never takes the focus.
///
/// The PNGs come from WPF's own software renderer (RenderTargetBitmap), which draws the same tree
/// with the same brushes and effects but with greyscale text. They are not a capture of the screen:
/// WPF presents nothing for a window that is off-screen, or while the display is asleep, and
/// PrintWindow returns an empty client area for it. So ClearType on the real window is protected by
/// the crisp-text rule checked on every scene, not by pixels.
/// </summary>
internal static class SelfTestRunner
{
    public const int ExitPassed = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;
    public const int ExitTimedOut = 3;

    public const string PassedLine = "Desktop self-test passed";

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(120);

    public static async Task<int> RunAsync(App app, AppArguments arguments)
    {
        var log = new SelfTestLog(arguments.ScreenshotDirectory);

        // A self-test that hangs must still end, or a CI job would wait for its own timeout.
        using var watchdog = new Timer(
            _ =>
            {
                log.Write("Desktop self-test FAILED: no result after " + Deadline.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s");
                log.Save();
                Environment.Exit(ExitTimedOut);
            },
            null,
            Deadline,
            Timeout.InfiniteTimeSpan);

        Exception? dispatcherFailure = null;
        app.DispatcherUnhandledException += (_, unhandled) =>
        {
            dispatcherFailure ??= unhandled.Exception;
            unhandled.Handled = true;
        };

        try
        {
            await new SelfTestSession(app, arguments, log).RunAsync();
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
internal sealed partial class SelfTestSession(App app, AppArguments arguments, SelfTestLog log)
{
    private const double OffScreen = -30000;
    private const string TestEndpoint = "https://pc.example.test:11443/";

    private static readonly Size DefaultSize = new(1240, 660);
    private static readonly Size MinimumSize = new(960, 620);
    private static readonly Size BeyondAnyDesktop = new(4096, 2304);

    /// <summary>A path the self-test hands to the supervisor and then proves nobody created.</summary>
    private static readonly string UnusedDataDirectory =
        Path.Combine(Path.GetTempPath(), "LiveCue-self-test-data-folder-that-must-never-exist");

    private readonly List<string> _screenshots = [];

    public async Task RunAsync()
    {
        var themes = app.Themes ?? throw new InvalidOperationException("The theme manager is not ready.");

        // Screenshots show Daylight whatever this PC is set to; the contrast look is captured on purpose below.
        themes.Apply(AppTheme.Daylight);

        var realDataFolder = Snapshot(DataFolder.DefaultPath);
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 10, 9, 14, 2, TimeSpan.Zero));
        var script = new RelayScript(clock);

        await using var services = AppServices.Create(
            new WpfUiDispatcher(app.Dispatcher),
            new InMemorySettingsStore(),
            UnusedDataDirectory,
            script.Create,
            clock);

        var window = ShowWindow(services.Shell);
        try
        {
            await ExerciseAsync(window, services, script, clock, themes);
        }
        finally
        {
            window.Close();
        }

        Require(!Directory.Exists(UnusedDataDirectory), "nothing to be written where the data folder would be");
        Require(Snapshot(DataFolder.DefaultPath) == realDataFolder, "the real data folder to be exactly as it was");
        log.Write("isolation: scripted relay, in-memory settings, no port bound, data folder untouched");
        log.Write($"screenshots: {_screenshots.Count}" + (arguments.ScreenshotDirectory is null ? " (none requested)" : string.Empty));
    }

    private async Task ExerciseAsync(MainWindow window, AppServices services, RelayScript script, ManualClock clock, ThemeManager themes)
    {
        var shell = services.Shell;
        var relay = shell.Relay;
        var root = (FrameworkElement)window.Content;

        await SettleAsync(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        log.Write($"window: dpi scale {dpi.DpiScaleX:0.##}, output scale {arguments.Scale.ToString(CultureInfo.InvariantCulture)}, text family \"{DescribeTextFont(window)}\"");

        // Stopped: what the window shows at launch.
        Require(shell.SelectedPage.Id == PageId.Live, "the Live view to be selected at launch");
        Require(Text(root, "RelayStatusText") == "Stopped", "the relay status to read \"Stopped\" at launch");
        Require(Text(root, "SidebarRelayStatus") == "Stopped", "the sidebar to read \"Stopped\" at launch");
        Require(Text(root, "PhoneStatusText") == "iPhone not connected", "the phone status to read \"iPhone not connected\" at launch");
        Require(Text(root, "PcAddressText") == "Shown when the relay is running", "the PC address placeholder before the first start");
        Require(Button(root, "StartRelayButton") is { IsVisible: true, IsEnabled: true }, "Start relay to be offered while stopped");
        Require(Button(root, "StopRelayButton") is { IsVisible: false }, "Stop relay to be hidden while stopped");
        Require(Text(root, "TranscriptText") == "Your next question will appear here.", "the transcript placeholder");
        var stopped = await CaptureAsync(window, root, "live-stopped");
        CheckGroundAndGlass(window, root, stopped);
        await CheckReferenceCompositesAsync(window, root);
        await CheckLayoutBeyondDesktopAsync(window, root);

        await ShowAsync(window, shell, PageId.Pairing);
        Require(Button(root, "ShowQrButton") is { IsEnabled: false }, "Show QR to be disabled until the relay is ready");
        Require(Button(root, "ResetPairingButton") is { IsEnabled: false }, "Reset pairing to be disabled while the relay is not running");
        await CaptureAsync(window, root, "pairing-empty");

        await ShowAsync(window, shell, PageId.Activity);
        Require(Button(root, "ClearActivityButton") is { IsEnabled: false }, "Clear activity to be disabled while the log is empty");
        Require(VisibleTexts(root).Contains("No activity yet"), "the empty state of the activity view");
        await CaptureAsync(window, root, "activity-empty");

        await ShowAsync(window, shell, PageId.Settings);
        Require(VisibleTexts(root).Contains("Private tailnet"), "the settings view to show the Private tailnet mode");
        Require(VisibleTexts(root).Contains("47841"), "the settings view to show relay port 47841");
        await CaptureAsync(window, root, "settings-private");

        // Started: a synthetic RelayStarted with an endpoint on a reserved test domain, then a phone.
        await ShowAsync(window, shell, PageId.Live);
        await relay.StartCommand.ExecuteAsync(null);
        await SettleAsync(window);
        Require(script.Current is not null && services.Supervisor.IsRunning, "the supervisor to be running the scripted relay");
        Require(relay.State == RelayState.Ready, "the relay state to be Ready after the started event");
        Require(Text(root, "RelayStatusText") == "Ready", "the relay status to read \"Ready\"");
        Require(Text(root, "SidebarRelayStatus") == "Ready", "the sidebar to read \"Ready\"");
        Require(Text(root, "PcAddressText") == TestEndpoint, "the PC address to be the advertised endpoint");
        Require(Button(root, "StartRelayButton") is { IsVisible: false }, "Start relay to be replaced while the relay runs");
        Require(Button(root, "StopRelayButton") is { IsVisible: true, IsEnabled: true }, "Stop relay to be offered while the relay runs");

        script.PhoneSeen();
        await DeliverAsync(window, services);
        Require(Text(root, "PhoneStatusText") == "iPhone connected", "a phone-seen event to show \"iPhone connected\"");
        Require(Text(root, "SidebarPhoneStatus") == "iPhone connected", "the sidebar to show \"iPhone connected\"");
        await CaptureAsync(window, root, "live-ready");

        // Presence: connected for 15 s after the last request, then idle; it never falls back to "not connected".
        clock.Advance(RelayViewModel.ConnectedWindow + TimeSpan.FromSeconds(1));
        await SettleAsync(window);
        Require(Text(root, "PhoneStatusText") == "iPhone idle", "the phone to read \"iPhone idle\" 16 s after its last request");
        script.PhoneSeen();
        await DeliverAsync(window, services);
        Require(Text(root, "PhoneStatusText") == "iPhone connected", "a new request to make the phone connected again");

        // The contrast-theme dictionary, forced (system colours stay whatever this PC uses).
        themes.Apply(AppTheme.HighContrast);
        await SettleAsync(window);
        Require(CountEffects(root) == 0, "no shadow anywhere under the contrast theme");
        await CaptureAsync(window, root, "live-ready-high-contrast", sizes: [DefaultSize]);
        themes.Apply(AppTheme.Daylight);
        await SettleAsync(window);
        Require(CountEffects(root) > 0, "shadows to return with Daylight");

        await ShowAsync(window, shell, PageId.Pairing);
        Require(Button(root, "ShowQrButton") is { IsEnabled: true }, "Show QR to be enabled once the relay is ready");

        // Activity: the log has the start line; a diagnostic is shown with its prefix; Clear empties it.
        script.Diagnostic("An event handler failed (InvalidOperationException).");
        await DeliverAsync(window, services);
        await ShowAsync(window, shell, PageId.Activity);
        var activity = VisibleTexts(root);
        Require(activity.Any(text => text.Contains("Relay started. Waiting for your iPhone.", StringComparison.Ordinal)), "the activity log to record the start");
        Require(activity.Any(text => text.Contains("Relay diagnostic: ", StringComparison.Ordinal)), "the activity log to prefix a diagnostic");
        Require(activity.Any(text => text.Contains("09:14:02", StringComparison.Ordinal)), "each activity entry to start with its time");
        Require(VisibleTexts(root).Contains("2 entries"), "the footer to count the entries");
        await CaptureAsync(window, root, "activity-filled");
        var clear = Button(root, "ClearActivityButton");
        Require(clear is { IsEnabled: true }, "Clear activity to be enabled while the log has entries");
        clear!.Command.Execute(null);
        await SettleAsync(window);
        Require(services.Activity.Entries.Count == 0 && VisibleTexts(root).Contains("No activity yet"), "Clear activity to empty the log");

        // Stop: the stopped layout. The PC address is kept; the phone goes back to "not connected".
        await ShowAsync(window, shell, PageId.Live);
        await relay.StopCommand.ExecuteAsync(null);
        await SettleAsync(window);
        Require(Text(root, "RelayStatusText") == "Stopped", "the relay status to read \"Stopped\" after Stop relay");
        Require(Text(root, "PhoneStatusText") == "iPhone not connected", "the phone to reset when the relay stops");
        Require(Text(root, "PcAddressText") == TestEndpoint, "the PC address to stay after Stop relay");
        Require(Button(root, "StartRelayButton") is { IsVisible: true, IsEnabled: true }, "Start relay to be offered again");
        Require(!services.Supervisor.IsRunning, "the supervisor to have stopped the relay");

        // A port-in-use fault: the stopped layout, and the fault text stays.
        script.NextStartFault = RelayFaultCode.PortInUse;
        await relay.StartCommand.ExecuteAsync(null);
        await SettleAsync(window);
        const string portInUse = "The relay port is already in use. Close the other LiveCue relay, then click Start relay.";
        Require(relay.State == RelayState.Faulted, "a port-in-use fault to leave the relay faulted");
        Require(Text(root, "RelayStatusText") == portInUse, "the fault to show its text as the status");
        Require(Text(root, "SidebarRelayStatus") == "Cannot start yet", "the sidebar to read \"Cannot start yet\" after a failed start");
        Require(Button(root, "StartRelayButton") is { IsVisible: true, IsEnabled: true }, "a fault to return the stopped layout with Start relay");
        Require(services.Activity.Entries.Any(entry => entry.Message == portInUse), "the fault to be recorded in the activity log");
        await CaptureAsync(window, root, "fault-port-in-use");

        // A new start is a new relay instance, and a successful one clears the fault text.
        await relay.StartCommand.ExecuteAsync(null);
        await SettleAsync(window);
        Require(Text(root, "RelayStatusText") == "Ready", "the next start to succeed and clear the fault text");
        Require(script.Created == 3, "every start to create a new relay instance");
        await relay.StopCommand.ExecuteAsync(null);
        await SettleAsync(window);
        Require(relay.State == RelayState.Stopped, "the relay to be stopped at the end");
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

    private static async Task ShowAsync(MainWindow window, ShellViewModel shell, PageId page)
    {
        shell.Navigate(page);
        await SettleAsync(window);
        Require(shell.SelectedPage.Id == page, $"the {page} view to be selected");
    }

    /// <summary>Waits until the supervisor has handed every raised event to the view model and the window has shown it.</summary>
    private static async Task DeliverAsync(MainWindow window, AppServices services)
    {
        await services.Supervisor.WhenIdleAsync();
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
            var fileName = Save(Math.Abs(arguments.Scale - 1) < 0.001 ? frame : Render(root, arguments.Scale), scene, size);
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
        var scaleSuffix = Math.Abs(arguments.Scale - 1) < 0.001
            ? string.Empty
            : "@" + arguments.Scale.ToString(CultureInfo.InvariantCulture) + "x";
        var fileName = $"{scene}-{size.Width}x{size.Height}{scaleSuffix}.png";
        if (arguments.ScreenshotDirectory is { } directory)
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

    private static string? Text(FrameworkElement root, string automationId) =>
        Find(root, automationId) is TextBlock { IsVisible: true } text ? text.Text : null;

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

    /// <summary>What exists at a path, and when it last changed: enough to tell that nothing touched it.</summary>
    private static string Snapshot(string directory) =>
        !Directory.Exists(directory)
            ? "absent"
            : string.Join(
                "|",
                new DirectoryInfo(directory).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                    .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                    .Select(entry => $"{entry.FullName}:{entry.LastWriteTimeUtc.Ticks}"));

    [GeneratedRegex("[A-Za-z0-9_-]{43}")]
    private static partial Regex TokenShape();
}
