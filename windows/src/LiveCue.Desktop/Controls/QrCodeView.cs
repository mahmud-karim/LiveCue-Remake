using System.Windows;
using System.Windows.Media;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// Draws a pairing matrix (design system 4.15): a white square of n + 8 modules with the dark modules
/// inset by four modules on every side. Nothing is drawn over it, and the matrix is never exposed as
/// text.
///
/// First version (foundation): modules are sized to fill the element. Snapping every edge to a device
/// pixel and the module-size rule of W-design 5.6 are work package WP-W10.
/// </summary>
public sealed class QrCodeView : FrameworkElement
{
    /// <summary>The quiet zone of protocol 4.2, in modules, on every side.</summary>
    public const int QuietZoneModules = 4;

    public static readonly DependencyProperty ModulesProperty = DependencyProperty.Register(
        nameof(Modules),
        typeof(bool[][]),
        typeof(QrCodeView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

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

    protected override void OnRender(DrawingContext drawingContext)
    {
        var modules = Modules;
        var side = Math.Min(RenderSize.Width, RenderSize.Height);
        if (modules is null || modules.Length == 0 || side <= 0)
        {
            return;
        }

        var count = modules.Length;
        var module = Math.Floor(side / (count + (2 * QuietZoneModules)));
        if (module < 1)
        {
            return;
        }

        var drawn = module * (count + (2 * QuietZoneModules));
        var left = Math.Floor((RenderSize.Width - drawn) / 2);
        var top = Math.Floor((RenderSize.Height - drawn) / 2);
        drawingContext.DrawRectangle(QuietBrush, null, new Rect(left, top, drawn, drawn));

        var origin = QuietZoneModules * module;
        for (var row = 0; row < count; row++)
        {
            var line = modules[row];
            for (var column = 0; column < Math.Min(count, line.Length); column++)
            {
                if (line[column])
                {
                    drawingContext.DrawRectangle(
                        ModuleBrush,
                        null,
                        new Rect(left + origin + (column * module), top + origin + (row * module), module, module));
                }
            }
        }
    }
}
