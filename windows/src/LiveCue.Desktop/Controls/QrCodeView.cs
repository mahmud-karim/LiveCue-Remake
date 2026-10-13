using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The arithmetic of the pairing code's drawing (W-design 5.6; design system 4.15). A code of
/// <c>n</c> modules per side is drawn as a square of <c>n + 8</c> modules: the code and a quiet zone of
/// four modules on every side. A module is a whole number of device pixels.
/// </summary>
public static class QrGeometry
{
    /// <summary>The quiet zone of protocol 4.2, in modules, on every side.</summary>
    public const int QuietZoneModules = 4;

    /// <summary>The side the drawn square aims for, in layout units.</summary>
    public const double TargetSide = 240;

    /// <summary>The drawn square is never narrower than this, in layout units.</summary>
    public const double MinimumSide = 200;

    /// <summary>A module is never smaller than this, in device pixels.</summary>
    public const int MinimumModulePixels = 3;

    /// <summary>How many modules the drawn square has per side: the code and both quiet zones.</summary>
    public static int DrawnModules(int modules) => modules + (2 * QuietZoneModules);

    /// <summary>
    /// The side of one module in device pixels on a monitor whose device scale is
    /// <paramref name="scale"/>: the largest whole number that keeps the square inside 240 layout
    /// units, at least 3; and one pixel more when the square would otherwise be narrower than 200.
    /// </summary>
    public static int ModulePixels(int modules, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(modules, 1);
        if (!(scale > 0) || double.IsInfinity(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var drawn = DrawnModules(modules);
        var pixels = Math.Max(MinimumModulePixels, (int)Math.Floor(TargetSide * scale / drawn));
        return pixels * drawn / scale < MinimumSide ? pixels + 1 : pixels;
    }

    /// <summary>The side of the drawn square in device pixels.</summary>
    public static int SidePixels(int modules, double scale) => ModulePixels(modules, scale) * DrawnModules(modules);

    /// <summary>The side of the drawn square in layout units: what the element asks its parent for.</summary>
    public static double Side(int modules, double scale) => SidePixels(modules, scale) / scale;
}

/// <summary>
/// Draws a pairing matrix (design system 4.15; W-design 5.6): a white square of n + 8 modules with the
/// dark modules inset by four modules on every side, every module a whole number of device pixels and
/// every edge on a device pixel. Nothing is drawn over it. Without a matrix it draws nothing.
///
/// To assistive technology it is one image named "Pairing QR code" and its state (the element's
/// <c>AutomationProperties.Name</c> and <c>ItemStatus</c>). The matrix is never exposed as text.
/// </summary>
public sealed class QrCodeView : FrameworkElement
{
    /// <summary>The quiet zone of protocol 4.2, in modules, on every side.</summary>
    public const int QuietZoneModules = QrGeometry.QuietZoneModules;

    public static readonly DependencyProperty ModulesProperty = DependencyProperty.Register(
        nameof(Modules),
        typeof(bool[][]),
        typeof(QrCodeView),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModuleBrushProperty = DependencyProperty.Register(
        nameof(ModuleBrush),
        typeof(Brush),
        typeof(QrCodeView),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty QuietBrushProperty = DependencyProperty.Register(
        nameof(QuietBrush),
        typeof(Brush),
        typeof(QrCodeView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public QrCodeView()
    {
        // Module edges are never blended with the white beside them.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        SnapsToDevicePixels = true;
    }

    /// <summary>Square, row-major, true = dark, no quiet zone. Null draws nothing.</summary>
#pragma warning disable CA1819 // The matrix is the contract's own shape (PairingQr.Modules).
    public bool[][]? Modules
#pragma warning restore CA1819
    {
        get => (bool[][]?)GetValue(ModulesProperty);
        set => SetValue(ModulesProperty, value);
    }

    /// <summary>QrModuleBrush.</summary>
    public Brush ModuleBrush
    {
        get => (Brush)GetValue(ModuleBrushProperty);
        set => SetValue(ModuleBrushProperty, value);
    }

    /// <summary>SurfaceQrBrush.</summary>
    public Brush QuietBrush
    {
        get => (Brush)GetValue(QuietBrushProperty);
        set => SetValue(QuietBrushProperty, value);
    }

    /// <summary>The device scale of the monitor the element is on: 1 at 96 DPI.</summary>
    private double DeviceScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Modules?.Length ?? 0;
        if (count == 0)
        {
            return default;
        }

        var side = QrGeometry.Side(count, DeviceScale);
        return new Size(side, side);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new QrCodePeer(this);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var modules = Modules;
        if (modules is null || modules.Length == 0 || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        var count = modules.Length;
        var scale = DeviceScale;
        var pixels = QrGeometry.ModulePixels(count, scale);
        var module = pixels / scale;
        var drawn = QrGeometry.DrawnModules(count);
        var side = drawn * module;

        // Centred in whatever room the element was given, on a whole device pixel.
        var left = Math.Max(0, Math.Floor((RenderSize.Width - side) / 2 * scale) / scale);
        var top = Math.Max(0, Math.Floor((RenderSize.Height - side) / 2 * scale) / scale);

        // Every module boundary is a guideline, so each lands on a device pixel wherever the element is.
        var guidelines = new GuidelineSet();
        for (var index = 0; index <= drawn; index++)
        {
            guidelines.GuidelinesX.Add(left + (index * module));
            guidelines.GuidelinesY.Add(top + (index * module));
        }

        guidelines.Freeze();
        drawingContext.PushGuidelineSet(guidelines);
        drawingContext.DrawRectangle(QuietBrush, null, new Rect(left, top, side, side));

        var originX = left + (QuietZoneModules * module);
        var originY = top + (QuietZoneModules * module);
        var brush = ModuleBrush;
        for (var row = 0; row < count; row++)
        {
            var line = modules[row];
            var width = line is null ? 0 : Math.Min(count, line.Length);

            // A run of dark modules is one rectangle: no seam can show between neighbours.
            for (var column = 0; column < width; column++)
            {
                if (!line![column])
                {
                    continue;
                }

                var start = column;
                while (column + 1 < width && line[column + 1])
                {
                    column++;
                }

                drawingContext.DrawRectangle(
                    brush,
                    null,
                    new Rect(originX + (start * module), originY + (row * module), (column - start + 1) * module, module));
            }
        }

        drawingContext.Pop();
    }

    /// <summary>One image: its name and, after a comma, its state. Never the matrix.</summary>
    private sealed class QrCodePeer(QrCodeView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(QrCodeView);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            var status = AutomationProperties.GetItemStatus(Owner);
            return string.IsNullOrEmpty(status) ? name : name + ", " + status;
        }

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}
