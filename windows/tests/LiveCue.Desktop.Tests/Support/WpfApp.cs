using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiveCue.Desktop.Interop;
using LiveCue.Desktop.Theming;

namespace LiveCue.Desktop.Tests.Support;

/// <summary>
/// The one WPF application of the test run, for the tests that create a window (category <c>Ui</c>).
/// A process can have only one <see cref="Application"/> and it belongs to the thread that made it, so
/// it lives on a thread of its own for the whole run, with the app's real resources merged, and every
/// such test runs its body there, one at a time. Animations are pinned off, as in the self-test: what
/// a test sees must not depend on the moment it looks.
/// </summary>
internal static class WpfApp
{
    private static readonly Lazy<Dispatcher> Started = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The application's own resources: every dictionary of App.xaml.</summary>
    public static ResourceDictionary Resources => Run(() => Application.Current.Resources);

    /// <summary>Runs <paramref name="body"/> on the application's thread. What it throws is thrown here.</summary>
    public static void Run(Action body) => Started.Value.Invoke(body);

    public static T Run<T>(Func<T> body) => Started.Value.Invoke(body);

    /// <summary>Runs an asynchronous body on the application's thread; its awaits come back to that thread.</summary>
    public static Task RunAsync(Func<Task> body) => Started.Value.InvokeAsync(body).Task.Unwrap();

    /// <summary>
    /// Lets bindings, layout and rendering finish. Call it on the application's thread: it runs the
    /// dispatcher until it is idle, then waits <paramref name="wait"/> for the short state changes.
    /// </summary>
    public static void Settle(TimeSpan? wait = null)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        if (wait is { } time && time > TimeSpan.Zero)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(time, DispatcherPriority.Background, (_, _) => frame.Continue = false, dispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
            dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// Shows <paramref name="window"/> far off the screen without activating it, as a 96-DPI window that
    /// may be larger than the desktop (hosted CI desktops are 1024 x 768), and lets it lay itself out.
    /// </summary>
    public static void ShowOffScreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(AllowSizesBeyondTheDesktop);

        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextUnaware);
        try
        {
            window.Show();
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                NativeMethods.SetThreadDpiAwarenessContext(previous);
            }
        }

        Settle();
        window.UpdateLayout();
    }

    /// <summary>
    /// Draws <paramref name="root"/> with WPF's software renderer, one pixel per layout unit, and writes
    /// it as <paramref name="fileName"/> into the folder that the environment variable
    /// <c>LIVECUE_TEST_SHOTS</c> names. Without that variable nothing is written: the pictures are for a
    /// person to look at, not for the test.
    /// </summary>
    public static BitmapSource Render(FrameworkElement root, string? fileName = null)
    {
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(root.ActualWidth)),
            Math.Max(1, (int)Math.Round(root.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();

        if (fileName is not null && Environment.GetEnvironmentVariable("LIVECUE_TEST_SHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, fileName));
            encoder.Save(stream);
        }

        return bitmap;
    }

    public static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                SystemPreferences.PinAnimations(false);
                ready.SetResult(Dispatcher.CurrentDispatcher);
            }
#pragma warning disable CA1031 // Carried to the first test that needs the application.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                ready.SetException(exception);
                return;
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Test WPF application",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
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
}
