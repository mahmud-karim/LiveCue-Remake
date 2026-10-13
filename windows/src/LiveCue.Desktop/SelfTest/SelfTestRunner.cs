using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
using LiveCue.Relay.Protocol;
using static LiveCue.Desktop.SelfTest.VisualChecks;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// <c>LiveCueRemake.exe --self-test [--screenshots &lt;folder&gt;] [--scale 1|1.5]</c> (architecture 3.13;
/// W-design 5.13).
///
/// Builds the real window on the real view models, the real session and the real supervisor, with a
/// scripted relay and scripted hosting parts. No port is bound, no Tailscale or Codex command runs,
/// nothing is read from or written to the data folder, and no network is used. It walks the nineteen
/// steps of <see cref="Scenes"/>, makes the named assertions after each, renders the fifteen scenes at
/// the default and at the minimum window size when screenshots were asked for, prints
/// "Desktop self-test passed" and exits 0. The first assertion that fails prints
/// "Desktop self-test FAILED: " and its name, and the process exits 1.
/// </summary>
internal static class SelfTestRunner
{
    public const int ExitPassed = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;
    public const int ExitTimedOut = 3;

    public const string PassedLine = "Desktop self-test passed";
    public const string FailedPrefix = "Desktop self-test FAILED: ";

    /// <summary>
    /// A switch for the tests only, not part of the product's command line: the assertion named after it
    /// fails although what it checks holds, so that a test can see a failure being reported.
    /// </summary>
    public const string FailSwitch = "--self-test-fail";

    public static async Task<int> RunAsync(App app, AppEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(environment);
        var log = new SelfTestLog(environment.ScreenshotDirectory);

        // A self-test that hangs must still end, or a CI job would wait for its own timeout (W-12).
        var deadline = ProtocolConstants.SelfTestWatchdog;
        using var watchdog = new Timer(
            _ =>
            {
                log.Write(FailedPrefix + "no result after " + deadline.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s");
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

        var options = new SelfTestOptions(
            environment.ScreenshotDirectory, environment.Scale, ForcedFailure(Environment.GetCommandLineArgs()));
        var session = new SelfTestSession(app, app.Themes, options, log);
        try
        {
            await session.RunAsync();
            if (dispatcherFailure is not null)
            {
                throw new SelfTestFailedException(
                    "Expected: no exception to reach the dispatcher, but one did: " + dispatcherFailure, "no-unhandled-exception");
            }

            log.Write(PassedLine);
            return ExitPassed;
        }
#pragma warning disable CA1031 // The self-test reports every failure as a non-zero exit code; nothing may escape.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // The name first, on the result line; what was expected on the line after it.
            log.Write(FailedPrefix + ((exception as SelfTestFailedException)?.Assertion ?? session.Step));
            log.Write(exception is SelfTestFailedException ? exception.Message : exception.ToString());
            return ExitFailed;
        }
        finally
        {
            log.Save();
        }
    }

    /// <summary>The value of <see cref="FailSwitch"/> on the command line, or null.</summary>
    internal static string? ForcedFailure(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var index = 0; index + 1 < args.Count; index++)
        {
            if (string.Equals(args[index], FailSwitch, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

/// <summary>What one run was asked for.</summary>
/// <param name="ScreenshotDirectory">The folder for the PNG files and the log; null writes nothing.</param>
/// <param name="Scale">Pixels per layout unit in the PNG files: 1 or 1.5.</param>
/// <param name="ForcedFailure">The assertion that is made to fail (<see cref="SelfTestRunner.FailSwitch"/>), or null.</param>
internal sealed record SelfTestOptions(string? ScreenshotDirectory = null, double Scale = 1, string? ForcedFailure = null);

/// <summary>
/// One run of the self-test: the window, the scripted parts behind it, and the tools the steps use.
/// The steps themselves are in <see cref="Scenes"/> (the other part of this class).
/// </summary>
internal sealed partial class SelfTestSession
{
    private const double OffScreen = -30000;

    private static readonly Size BeyondAnyDesktop = new(4096, 2304);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Application _app;
    private readonly ThemeManager? _themes;
    private readonly SelfTestLog _log;
    private readonly SelfTestAssertions _assert;
    private readonly SceneRenderer _renderer;
    private readonly List<string> _scenes = [];

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 10, 9, 14, 2, TimeSpan.Zero));
    private readonly RelayScript _script;

    // Set when the window exists; the steps run only then.
    private AppServices _services = null!;
    private MainWindow _window = null!;
    private FrameworkElement _root = null!;

    public SelfTestSession(Application app, ThemeManager? themes, SelfTestOptions options, SelfTestLog log)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _app = app;
        _themes = themes;
        _log = log;
        _assert = new SelfTestAssertions(log, options.ForcedFailure);
        _renderer = new SceneRenderer(options.ScreenshotDirectory, options.Scale);
        _script = new RelayScript(_clock);
        Hosting = new ScriptedHosting(_clock, _script);
    }

    /// <summary>The scripted hosting parts, with the recording process runner and file system.</summary>
    public ScriptedHosting Hosting { get; }

    public RecordingClipboard Clipboard { get; } = new();

    public RecordingLauncher Launcher { get; } = new();

    /// <summary>The scenes that were shown, in order.</summary>
    public IReadOnlyList<string> ScenesShown => _scenes;

    /// <summary>The assertions that held, in order.</summary>
    public IReadOnlyList<string> AssertionsPassed => _assert.Passed;

    /// <summary>The screenshot files written, in order.</summary>
    public IReadOnlyList<string> ScreenshotsWritten => _renderer.Written;

    /// <summary>The step the run is in, for a failure that no assertion names.</summary>
    public string Step => _assert.Step;

    private ShellViewModel Shell => _services.Shell;

    private IDesktopSession Session => _services.Session;

    /// <summary>Runs every step. Throws <see cref="SelfTestFailedException"/> at the first assertion that fails.</summary>
    public async Task RunAsync()
    {
        // Screenshots show Daylight whatever this PC is set to.
        _themes?.Apply(AppTheme.Daylight);

        _services = AppServices.Create(Hosting.Services, new WpfUiDispatcher(_app.Dispatcher), _clock, Clipboard, Launcher);
        try
        {
            _window = ShowWindow(_services.Shell);
            _root = (FrameworkElement)_window.Content;
            try
            {
                await RunStepsAsync();
            }
            finally
            {
                _window.Close();
            }
        }
        finally
        {
            await _services.DisposeAsync();
        }

        _log.Write("isolation: scripted relay and hosting, no port bound, no process, no file touched, no data folder used");
        _log.Write($"scenes: {_scenes.Count}, assertions: {_assert.Passed.Count}, texts looked at: {_assert.TextCount}");
        _log.Write($"screenshots: {_renderer.Written.Count}" + (_renderer.WritesFiles ? string.Empty : " (none requested)"));
    }

    // ---- The window -------------------------------------------------------------------------------

    /// <summary>
    /// Creates the window as a 96-DPI window, off-screen, without activating it. At 96 DPI one layout
    /// unit is one pixel, so the PNGs are pixel-exact at 1240 x 660 on any monitor. The hook lifts the
    /// system rule that a window cannot be larger than the desktop (hosted CI desktops are 1024 x 768).
    /// The window does not drive the relay and is never attached to the capture setting, so
    /// SetWindowDisplayAffinity is not called for it (W-design 5.12).
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

    // ---- Driving the window -----------------------------------------------------------------------

    /// <summary>Names the step that begins, for the log and for a failure that no assertion names.</summary>
    private void Begin(int number, string what)
    {
        _assert.Step = string.Create(CultureInfo.InvariantCulture, $"step-{number:00}");
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"step {number}: {what}"));
    }

    private async Task ShowAsync(AppPage page)
    {
        Shell.Navigate(page);
        await SettleAsync();
        Require(Shell.SelectedPage == page, $"the {page} view to be selected");
    }

    /// <summary>Clicks the button with that automation id the way a user would: through its command.</summary>
    private void Press(string automationId)
    {
        var button = Button(automationId);
        Require(button is { IsVisible: true, IsEnabled: true }, $"{automationId} to be there to press");
        Require(button!.Command is not null && button.Command.CanExecute(button.CommandParameter), $"{automationId} to be able to run");
        button.Command!.Execute(button.CommandParameter);
    }

    /// <summary>
    /// Presses a button whose command does nothing while the command before it is still finishing (the
    /// session's commands are single-flight): presses, waits a moment for <paramref name="taken"/> to
    /// hold, and presses again if it does not. <paramref name="taken"/> is something only the command
    /// itself brings about, so a press never takes effect twice. While the button cannot be pressed (its
    /// command is running) it only waits.
    /// </summary>
    private async Task PressUntilAsync(string automationId, Func<bool> taken, string expectation)
    {
        Require(Button(automationId) is { IsVisible: true }, $"{automationId} to be there to press");
        var until = DateTime.UtcNow + Patience;
        while (!taken())
        {
            Require(DateTime.UtcNow < until, expectation);
            if (Button(automationId) is { IsVisible: true, IsEnabled: true, Command: { } command } button
                && command.CanExecute(button.CommandParameter))
            {
                command.Execute(button.CommandParameter);
            }

            var retryAt = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            while (!taken() && DateTime.UtcNow < retryAt)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(15));
            }
        }
    }

    /// <summary>
    /// Waits until the supervisor's events have reached the session and the window shows them. Events
    /// travel from a thread-pool task to the dispatcher, so there is nothing to await but the result.
    /// </summary>
    private async Task WaitForAsync(Func<bool> condition, string expectation)
    {
        var until = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Require(DateTime.UtcNow < until, expectation);
            await Task.Delay(TimeSpan.FromMilliseconds(15));
        }

        await SettleAsync();
    }

    /// <summary>
    /// Moves the script's clock. The timers that came due have posted their work to the window; once
    /// that is done, the phone is heard from again when it was connected, as a real one would be: the
    /// script's phone never goes idle by itself.
    /// </summary>
    private async Task AdvanceAsync(TimeSpan by)
    {
        var phoneWasThere = Session.Presence != PhonePresence.NotConnected;
        _clock.Advance(by);
        await SettleAsync();
        if (phoneWasThere && Session.IsRelayRunning)
        {
            var seen = _clock.GetUtcNow();
            _script.PhoneSeen();
            await WaitForAsync(
                () => Session.Presence == PhonePresence.Connected && Session.LastPhoneSeen == seen,
                "the phone to be heard from again");
        }
    }

    private async Task ResizeAsync(Size size)
    {
        if (Math.Abs(_root.ActualWidth - size.Width) < 0.01 && Math.Abs(_root.ActualHeight - size.Height) < 0.01)
        {
            return;
        }

        InWindowDpiContext(() =>
        {
            _window.Width = size.Width;
            _window.Height = size.Height;
            return true;
        });
        await SettleAsync();

        Require(
            Math.Abs(_root.ActualWidth - size.Width) < 0.01 && Math.Abs(_root.ActualHeight - size.Height) < 0.01,
            $"the window content to be laid out at {size.Width} x {size.Height}, not {_root.ActualWidth} x {_root.ActualHeight}");
    }

    /// <summary>Lets bindings, layout, rendering and the short state changes finish.</summary>
    private async Task SettleAsync()
    {
        await _window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        _window.UpdateLayout();
        await _window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// The scene the window shows now: every text in it is remembered for the end-of-run assertions,
    /// it is drawn at the default size, and with a screenshot folder it is written at both sizes.
    /// Returns the picture at the default size, at one pixel per layout unit.
    /// </summary>
    private async Task<BitmapSource> SceneAsync(string scene)
    {
        Require(Scenes.Names.Contains(scene) && !_scenes.Contains(scene), $"\"{scene}\" to be a scene of the list, shown once");
        var step = _assert.Step;
        _assert.Step = scene;

        await ResizeAsync(SceneRenderer.DefaultSize);
        var texts = _assert.Observe(_root);
        Require(texts > 0, "the scene to show some text");
        var frame = SceneRenderer.Render(_root);
        Require(
            frame.PixelWidth == (int)SceneRenderer.DefaultSize.Width && frame.PixelHeight == (int)SceneRenderer.DefaultSize.Height,
            "the scene to be drawn at the default window size");

        if (_renderer.WritesFiles)
        {
            var written = _renderer.Save(_root, scene, SceneRenderer.DefaultSize, frame);
            await ResizeAsync(SceneRenderer.MinimumSize);
            _assert.Observe(_root);
            var writtenMin = _renderer.Save(_root, scene, SceneRenderer.MinimumSize);
            await ResizeAsync(SceneRenderer.DefaultSize);
            _log.Write($"scene: {written}, {writtenMin} ({texts} texts)");
        }
        else
        {
            _log.Write($"scene: {scene} ({texts} texts)");
        }

        _scenes.Add(scene);
        _assert.Step = step;
        return frame;
    }

    // ---- Reading the window -----------------------------------------------------------------------

    /// <summary>The element with that automation id; of two with the same id, the one that is shown.</summary>
    private FrameworkElement? Find(string automationId)
    {
        FrameworkElement? hidden = null;
        foreach (var element in Descendants(_root).OfType<FrameworkElement>())
        {
            if (AutomationProperties.GetAutomationId(element) != automationId)
            {
                continue;
            }

            if (element.IsVisible)
            {
                return element;
            }

            hidden ??= element;
        }

        return hidden;
    }

    /// <summary>The text a visible element with that automation id shows; null when there is none.</summary>
    private string? Text(string automationId) => Find(automationId) switch
    {
        TextBlock { IsVisible: true } text => TextOf(text),
        TextBox { IsVisible: true } box => box.Text,
        StatusIndicatorView { IsVisible: true } indicator => indicator.Text,
        _ => null,
    };

    private ButtonBase? Button(string automationId) => Find(automationId) as ButtonBase;

    /// <summary>Every text that is shown now: text blocks and the text of text boxes.</summary>
    private List<string> VisibleTexts()
    {
        var texts = new List<string>();
        foreach (var element in Descendants(_root).OfType<FrameworkElement>().Where(element => element.IsVisible))
        {
            switch (element)
            {
                case TextBlock block:
                    texts.Add(TextOf(block));
                    break;
                case TextBox box:
                    texts.Add(box.Text);
                    break;
                default:
                    break;
            }
        }

        return texts;
    }

    private bool Shows(string text) => VisibleTexts().Any(shown => shown.Contains(text, StringComparison.Ordinal));

    private static string TextOf(TextBlock block) => string.IsNullOrEmpty(block.Text)
        ? string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text))
        : block.Text;

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

    // ---- The look of the window (design system checklist) -----------------------------------------

    /// <summary>
    /// The ground is not flat (the blooms are there) and glass reads lighter than the ground beside
    /// it. Sample points are taken from the laid-out elements, not from fixed coordinates.
    /// </summary>
    private void CheckGroundAndGlass(BitmapSource frame)
    {
        // Top left is the heart of bloom 1 (#9DC1F5); the top centre is far from every bloom centre.
        var bloom = PixelAt(frame, 3, 3);
        var middle = PixelAt(frame, frame.PixelWidth / 2, 3);
        Require(bloom.B > bloom.R + 40, $"bloom 1 to tint the top-left corner blue, not {Hex(bloom)}");
        Require(Luminance(middle) > Luminance(bloom) + 0.05, $"the ground to get lighter away from bloom 1 ({Hex(bloom)} to {Hex(middle)})");

        // Inside the sidebar, in its empty middle, against the ground just left of it at the same height.
        var (x, y) = EmptySpotInSidebar();
        var glass = PixelAt(frame, x, y);
        var ground = PixelAt(frame, 6, y);
        Require(Luminance(glass) > Luminance(ground) + 0.02, $"the sidebar glass ({Hex(glass)}) to be lighter than the ground beside it ({Hex(ground)})");

        _log.Write($"look: bloom {Hex(bloom)}, ground {Hex(middle)}, sidebar glass {Hex(glass)} over ground {Hex(ground)}");
    }

    /// <summary>
    /// Design system checklist item 1: with the ground forced flat, a pixel inside a panel, below the
    /// sheen, is #EBEEF4 within 2 per channel. That figure includes the panel's own shadow showing
    /// through its translucent fill, so it proves the fill, the shadow and their compositing at once.
    /// </summary>
    private async Task<(Color Ground, Color Panel)> RenderOnFlatGroundAsync()
    {
        string[] bloomKeys = ["GroundBloom1Color", "GroundBloom2Color", "GroundBloom3Color", "GroundBloom4Color"];
        foreach (var key in bloomKeys)
        {
            _app.Resources[key] = Colors.Transparent;
        }

        try
        {
            await SettleAsync();
            var frame = SceneRenderer.Render(_root);
            var (x, y) = EmptySpotInSidebar();
            return (PixelAt(frame, frame.PixelWidth / 2, 3), PixelAt(frame, x, y));
        }
        finally
        {
            foreach (var key in bloomKeys)
            {
                _app.Resources.Remove(key);
            }

            await SettleAsync();
        }
    }

    /// <summary>
    /// The screenshots must not depend on the size of the desktop the test happens to run on. This
    /// lays the window out larger than any desktop and checks the content really got that size.
    /// </summary>
    private async Task CheckLayoutBeyondDesktopAsync()
    {
        await ResizeAsync(BeyondAnyDesktop);
        await ResizeAsync(SceneRenderer.DefaultSize);
    }

    /// <summary>A point inside the sidebar panel with nothing drawn on it: between the last row and the status card.</summary>
    private (int X, int Y) EmptySpotInSidebar()
    {
        var sidebar = (FrameworkElement)_window.FindName("Sidebar");
        var bounds = sidebar.TransformToAncestor(_root).TransformBounds(new Rect(sidebar.RenderSize));
        return ((int)(bounds.Left + (bounds.Width / 2)), (int)(bounds.Top + (bounds.Height * 0.55)));
    }
}
