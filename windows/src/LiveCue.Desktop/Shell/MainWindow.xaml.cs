using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LiveCue.Desktop.Controls;
using LiveCue.Desktop.Interop;
using LiveCue.Desktop.Text;

namespace LiveCue.Desktop.Shell;

/// <summary>
/// The one window (W-design 5.1). The code here is window plumbing only: the custom caption buttons,
/// the DWM frame settings, the inset a chrome-less window needs while maximised, the three layers that
/// follow the shell view model (the page, the dialog, the toast) with their focus and their motion,
/// the display affinity, and telling the shell when the window has rendered and when it closes.
/// Everything shown comes from <see cref="IShellViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan PageLeaves = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DialogLeaves = TimeSpan.FromMilliseconds(120);

    private readonly IShellViewModel _shell;
    private bool _keepActiveAppearance;
    private bool _rendered;
    private bool _closing;

    private int _pageChange;
    private int _dialogChange;
    private int _toastChange;
    private IDialogViewModel? _shownDialog;
    private IInputElement? _focusBeforeDialog;

    private IDesktopSession? _captureSession;
    private CaptureExclusion? _captureExclusion;

    public MainWindow(IShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        InitializeComponent();
        _shell = shell;
        DataContext = shell;

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));

        Toast.MouseEnter += (_, _) => (_shell as ShellViewModel)?.HoldToast();
        Toast.MouseLeave += (_, _) => (_shell as ShellViewModel)?.ReleaseToast();

        _shell.PropertyChanged += OnShellChanged;
        ShowCurrentPage();
        ShowActiveDialog();
        ShowCurrentToast();
    }

    /// <summary>
    /// True, the default, starts the relay on the first render and stops it when the window closes. The
    /// self-test sets it to false: it drives the session itself and closes its window at once.
    /// </summary>
    internal bool DrivesRelay { get; set; } = true;

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

    /// <summary>
    /// Makes the window follow the saved "hide from screen capture" setting (W-design 5.12): now, if the
    /// window has its handle, when it gets one, and whenever the session's settings change. A call
    /// that Windows refuses is reported once through <paramref name="report"/> and never tried again.
    /// The self-test does not call this, so its window is never touched.
    /// </summary>
    /// <param name="setAffinity">The display affinity call; null means the real one.</param>
    internal void FollowCaptureSetting(
        IDesktopSession session,
        Action<SessionMessageKind, string> report,
        Func<IntPtr, uint, bool>? setAffinity = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);

        if (_captureSession is not null)
        {
            _captureSession.PropertyChanged -= OnCaptureSessionChanged;
        }

        _captureSession = session;
        _captureExclusion = new CaptureExclusion(
            setAffinity ?? NativeMethods.SetWindowDisplayAffinity,
            () => report(SessionMessageKind.Warning, ShellText.CaptureExclusionUnavailable));
        session.PropertyChanged += OnCaptureSessionChanged;
        ApplyCaptureSetting();
    }

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

        ApplyCaptureSetting();
    }

    /// <summary>The first render starts the relay, once (WIN-2). The window is on screen first.</summary>
    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_rendered || !DrivesRelay)
        {
            return;
        }

        _rendered = true;
        try
        {
            await _shell.OnWindowRenderedAsync();
        }
#pragma warning disable CA1031 // An async void handler must not let anything escape to the dispatcher.
        catch (Exception)
#pragma warning restore CA1031
        {
            // The session reports its own failures as activity lines; nothing more to do here.
        }
    }

    /// <summary>
    /// Closing always stops the relay, with no confirmation. The first Closing is cancelled, the window
    /// is disabled, the stop is awaited for at most W-11 (the shell bounds it), and then the window
    /// closes for real.
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnClosing(e);
        if (e.Cancel || _closing || !DrivesRelay)
        {
            return;
        }

        e.Cancel = true;
        _closing = true;
        IsEnabled = false;
        try
        {
            await _shell.OnWindowClosingAsync();
        }
#pragma warning disable CA1031 // A stop that fails must not keep the window open.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _shell.PropertyChanged -= OnShellChanged;
        if (_captureSession is not null)
        {
            _captureSession.PropertyChanged -= OnCaptureSessionChanged;
        }

        base.OnClosed(e);
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

    /// <summary>While a dialog is open the keyboard focus does not leave it for the window behind.</summary>
    protected override void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPreviewGotKeyboardFocus(e);
        if (_shownDialog is { } dialog
            && e.NewFocus is Visual target
            && target.IsDescendantOf(Frame)
            && !target.IsDescendantOf(DialogCard))
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FocusDialog(dialog, keepWhatIsThere: true));
        }
    }

    // ---- The three layers that follow the shell ---------------------------------------------------

    private void OnShellChanged(object? sender, PropertyChangedEventArgs change)
    {
        switch (change.PropertyName)
        {
            case nameof(IShellViewModel.CurrentPage):
                ShowCurrentPage();
                break;

            case nameof(IShellViewModel.ActiveDialog):
                ShowActiveDialog();
                break;

            case nameof(IShellViewModel.ToastText):
                ShowCurrentToast();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// A change of view (design system 7.1): the outgoing content fades out over 100 ms, then the
    /// incoming content fades in and rises 8 over motion.base. The sidebar and the title bar do not
    /// move. At rest the page host has opacity 1 and no transform, so its text is crisp. With
    /// animations off the change is instant.
    /// </summary>
    private void ShowCurrentPage()
    {
        var page = _shell.CurrentPage;
        if (ReferenceEquals(PageHost.Content, page))
        {
            return;
        }

        var change = ++_pageChange;
        if (PageHost.Content is null || !Motion.IsEnabledFor(PageHost))
        {
            PageHost.Content = page;
            Motion.Run(PageHost, OpacityProperty, 1, 1, TimeSpan.Zero, Motion.Standard, animate: false);
            PageHost.ClearValue(RenderTransformProperty);
            return;
        }

        Motion.Run(PageHost, OpacityProperty, PageHost.Opacity, 0, PageLeaves, Motion.In, animate: true, then: () =>
        {
            if (change != _pageChange)
            {
                return;
            }

            PageHost.Content = _shell.CurrentPage;
            var rise = new TranslateTransform(0, 8);
            PageHost.RenderTransform = rise;
            Motion.Run(PageHost, OpacityProperty, 0, 1, Motion.Base, Motion.Standard, animate: true);
            Motion.Run(rise, TranslateTransform.YProperty, 8, 0, Motion.Base, Motion.Standard, animate: true, then: () =>
            {
                if (change == _pageChange)
                {
                    PageHost.ClearValue(RenderTransformProperty);
                }
            });
        });
    }

    /// <summary>
    /// The dialog layer (W-design 5.9; design system 4.10 and 7.1). Opening: the scrim fades over
    /// motion.fast, the card fades and grows from 0.96 over motion.base, and the focus moves into the
    /// dialog. Closing: 120 ms, and the focus returns to the control that had it.
    /// </summary>
    private void ShowActiveDialog()
    {
        var dialog = _shell.ActiveDialog;
        if (ReferenceEquals(dialog, _shownDialog))
        {
            return;
        }

        var change = ++_dialogChange;
        if (dialog is null)
        {
            _shownDialog = null;
            DialogLayer.IsHitTestVisible = false;
            RestoreFocus();
            Motion.Run(DialogLayer, OpacityProperty, DialogLayer.Opacity, 0, DialogLeaves, Motion.In, Motion.IsEnabledFor(DialogLayer), then: () =>
            {
                if (change == _dialogChange)
                {
                    DialogLayer.Visibility = Visibility.Collapsed;
                    DialogCard.Content = null;
                    Motion.Run(DialogLayer, OpacityProperty, 1, 1, TimeSpan.Zero, Motion.Standard, animate: false);
                }
            });
            return;
        }

        if (_shownDialog is null)
        {
            _focusBeforeDialog = FocusManager.GetFocusedElement(this);
        }

        _shownDialog = dialog;
        DialogCard.Content = dialog;
        DialogLayer.Visibility = Visibility.Visible;
        DialogLayer.IsHitTestVisible = true;
        Motion.Run(DialogLayer, OpacityProperty, 1, 1, TimeSpan.Zero, Motion.Standard, animate: false);

        var animate = Motion.IsEnabledFor(this);
        Motion.Run(Scrim, OpacityProperty, 0, 1, Motion.Fast, Motion.Standard, animate);
        Motion.Run(DialogCard, OpacityProperty, 0, 1, Motion.Base, Motion.Out, animate);
        if (animate)
        {
            var grow = new ScaleTransform(0.96, 0.96);
            DialogCard.RenderTransformOrigin = new Point(0.5, 0.5);
            DialogCard.RenderTransform = grow;
            Motion.Run(grow, ScaleTransform.ScaleXProperty, 0.96, 1, Motion.Base, Motion.Out, animate: true);
            Motion.Run(grow, ScaleTransform.ScaleYProperty, 0.96, 1, Motion.Base, Motion.Out, animate: true, then: () =>
            {
                if (change == _dialogChange)
                {
                    DialogCard.ClearValue(RenderTransformProperty);
                }
            });
        }
        else
        {
            DialogCard.ClearValue(RenderTransformProperty);
        }

        // The dialog's view exists after the next layout pass; the focus goes into it then.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusDialog(dialog, keepWhatIsThere: true));
    }

    /// <summary>
    /// Moves the focus into the open dialog (design system 4.10): to its first field; without one, to
    /// its default button; without one, to its cancel button, which is the least destructive choice of
    /// a dialog that confirms something destructive; otherwise to its first control.
    /// </summary>
    private void FocusDialog(IDialogViewModel dialog, bool keepWhatIsThere)
    {
        if (!ReferenceEquals(_shownDialog, dialog))
        {
            return;
        }

        DialogCard.UpdateLayout();
        if (keepWhatIsThere
            && FocusManager.GetFocusedElement(this) is Visual focused
            && focused.IsDescendantOf(DialogCard))
        {
            return;
        }

        var candidates = Descendants(DialogCard)
            .OfType<UIElement>()
            .Where(element => element is { Focusable: true, IsEnabled: true, IsVisible: true }
                && element is not Control { IsTabStop: false })
            .ToList();
        var target = candidates.FirstOrDefault(element => element is TextBoxBase { IsReadOnly: false })
            ?? candidates.FirstOrDefault(element => element is Button { IsDefault: true })
            ?? candidates.FirstOrDefault(element => element is Button { IsCancel: true })
            ?? candidates.FirstOrDefault();
        if (target is not null)
        {
            MoveFocusTo(target);
        }
    }

    /// <summary>Gives the focus back to the control that opened the dialog, or to the selected navigation row.</summary>
    private void RestoreFocus()
    {
        var previous = _focusBeforeDialog;
        _focusBeforeDialog = null;
        if (previous is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } element
            && element.IsDescendantOf(this))
        {
            MoveFocusTo(element);
        }
        else if (Navigation.ItemContainerGenerator.ContainerFromIndex(Navigation.SelectedIndex) is UIElement row)
        {
            MoveFocusTo(row);
        }
    }

    /// <summary>
    /// Sets the window's focused element. An inactive window cannot take the keyboard, but it remembers
    /// the element and hands it the keyboard when it becomes active.
    /// </summary>
    private void MoveFocusTo(UIElement element)
    {
        FocusManager.SetFocusedElement(this, element);
        if (IsActive)
        {
            Keyboard.Focus(element);
        }
    }

    private void OnScrimPressed(object sender, MouseButtonEventArgs e)
    {
        // The same rule as Escape: the dialog is cancelled unless it is busy.
        if (_shell.EscapeCommand.CanExecute(null))
        {
            _shell.EscapeCommand.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>
    /// The toast (design system 4.11 and 7.1): it fades in and moves 12 up from the bottom over
    /// motion.base and leaves over motion.fast. A new text while one is shown replaces it in place. It
    /// is announced politely.
    /// </summary>
    private void ShowCurrentToast()
    {
        var text = _shell.ToastText;
        var change = ++_toastChange;
        if (string.IsNullOrEmpty(text))
        {
            if (Toast.Visibility != Visibility.Visible)
            {
                return;
            }

            Motion.Run(Toast, OpacityProperty, Toast.Opacity, 0, Motion.Fast, Motion.Standard, Motion.IsEnabledFor(Toast), then: () =>
            {
                if (change == _toastChange)
                {
                    Toast.Visibility = Visibility.Collapsed;
                    Motion.Run(Toast, OpacityProperty, 1, 1, TimeSpan.Zero, Motion.Standard, animate: false);
                }
            });
            return;
        }

        var appears = Toast.Visibility != Visibility.Visible;
        ToastLabel.Text = text;
        Toast.Visibility = Visibility.Visible;
        var animate = appears && Motion.IsEnabledFor(this);
        Motion.Run(Toast, OpacityProperty, appears ? 0 : Toast.Opacity, 1, Motion.Base, Motion.Standard, animate);
        if (animate)
        {
            var rise = new TranslateTransform(0, 12);
            Toast.RenderTransform = rise;
            Motion.Run(rise, TranslateTransform.YProperty, 12, 0, Motion.Base, Motion.Standard, animate: true, then: () =>
            {
                if (change == _toastChange)
                {
                    Toast.ClearValue(RenderTransformProperty);
                }
            });
        }
        else
        {
            Toast.ClearValue(RenderTransformProperty);
        }

        (UIElementAutomationPeer.FromElement(ToastLabel) ?? UIElementAutomationPeer.CreatePeerForElement(ToastLabel))
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
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

    // ---- Screen capture ---------------------------------------------------------------------------

    private void OnCaptureSessionChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(IDesktopSession.Settings) or null or "")
        {
            ApplyCaptureSetting();
        }
    }

    private void ApplyCaptureSetting()
    {
        if (_captureSession is { } session && _captureExclusion is { } exclusion)
        {
            exclusion.Apply(new WindowInteropHelper(this).Handle, session.Settings.HideFromScreenCapture);
        }
    }

    // ---- Maximised ----------------------------------------------------------------------------------

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
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
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

/// <summary>
/// Keeps one window in or out of screen capture (architecture 3.5, <c>hideFromScreenCapture</c>;
/// W-design 5.12). Windows 10 before version 2004 refuses "exclude from capture": that is told once and
/// the call is never made again.
/// </summary>
internal sealed class CaptureExclusion(Func<IntPtr, uint, bool> setAffinity, Action onUnavailable)
{
    private bool _unavailable;
    private bool? _applied;

    /// <summary>True once Windows refused the call.</summary>
    public bool IsUnavailable => _unavailable;

    public void Apply(IntPtr handle, bool hide)
    {
        if (_unavailable || handle == IntPtr.Zero || _applied == hide)
        {
            return;
        }

        if (setAffinity(handle, hide ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone))
        {
            _applied = hide;
            return;
        }

        _unavailable = true;
        onUnavailable();
    }
}
