using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.Interop;
using LiveCue.Desktop.ViewModels;

namespace LiveCue.Desktop;

/// <summary>
/// The one window. The code here is window plumbing only: the custom caption buttons, the DWM frame
/// settings and the inset a chrome-less window needs while maximised. Everything shown comes from
/// <see cref="ShellViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private bool _keepActiveAppearance;

    public MainWindow(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        InitializeComponent();
        DataContext = shell;

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
    }

    /// <summary>
    /// Draws the title bar as if the window were active even when it is not. The self-test sets it,
    /// because it shows its window without taking the focus from whatever the user is doing.
    /// </summary>
    internal bool KeepActiveAppearance
    {
        get => _keepActiveAppearance;
        set
        {
            _keepActiveAppearance = value;
            ChromeState.SetIsWindowActive(this, value || IsActive);
        }
    }

    /// <summary>The margin currently applied to keep a maximised window inside the work area.</summary>
    internal Thickness MaximizedInset => Frame.Margin;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;

        // Both calls fail harmlessly before Windows 11; the window is then square, which is correct there.
        var round = NativeMethods.DwmwcpRound;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DwmwaWindowCornerPreference, ref round, sizeof(int));

        // Light frame whatever the Windows app mode is (design system, section 5).
        var darkMode = 0;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        ChromeState.SetIsWindowActive(this, true);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        ChromeState.SetIsWindowActive(this, _keepActiveAppearance);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreButton.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        Frame.Margin = maximized ? MeasureMaximizedOverhang() : default;
    }

    /// <summary>
    /// A maximised window without a system frame is positioned so that its resize border hangs off
    /// every edge of the work area, which would cut off the title bar and the panels. This measures
    /// how far the client area really extends past the work area, in device-independent pixels, rather
    /// than assuming a system metric.
    /// </summary>
    private Thickness MeasureMaximizedOverhang()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var origin = default(NativeMethods.Point);
        var monitorInfo = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };

        if (handle == IntPtr.Zero
            || !NativeMethods.GetClientRect(handle, out var client)
            || !NativeMethods.ClientToScreen(handle, ref origin)
            || !NativeMethods.GetMonitorInfo(
                NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest), ref monitorInfo))
        {
            return FallbackMaximizedOverhang();
        }

        var work = monitorInfo.Work;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        double left = Math.Max(0, work.Left - origin.X);
        double top = Math.Max(0, work.Top - origin.Y);
        double right = Math.Max(0, origin.X + client.Right - work.Right);
        double bottom = Math.Max(0, origin.Y + client.Bottom - work.Bottom);
        return new Thickness(left * scale.M11, top * scale.M22, right * scale.M11, bottom * scale.M22);
    }

    private static Thickness FallbackMaximizedOverhang()
    {
        // The documented estimate (design system 3.5): the resize border plus the padded border of 4.
        var border = SystemParameters.WindowResizeBorderThickness;
        return new Thickness(border.Left + 4, border.Top + 4, border.Right + 4, border.Bottom + 4);
    }
}
