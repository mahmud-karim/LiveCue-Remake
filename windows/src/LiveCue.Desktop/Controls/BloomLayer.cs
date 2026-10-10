using System.Windows;
using System.Windows.Media;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The Daylight ground (design system 2.2): an opaque base colour and four soft circular blooms.
/// It is the only thing behind the app. It is redrawn when the window changes size and never moves
/// otherwise.
/// </summary>
public sealed class BloomLayer : FrameworkElement
{
    /// <summary>A bloom fades from its colour at the centre to nothing at this fraction of its radius.</summary>
    private const double FadeEnd = 0.72;

    public static readonly DependencyProperty BaseProperty = Register<Brush?>(nameof(Base), null);
    public static readonly DependencyProperty Bloom1Property = Register(nameof(Bloom1), Colors.Transparent);
    public static readonly DependencyProperty Bloom2Property = Register(nameof(Bloom2), Colors.Transparent);
    public static readonly DependencyProperty Bloom3Property = Register(nameof(Bloom3), Colors.Transparent);
    public static readonly DependencyProperty Bloom4Property = Register(nameof(Bloom4), Colors.Transparent);

    /// <summary>
    /// Centre as a fraction of (width, height); radius as a fraction of the longer side. Blooms are
    /// circles, not ellipses.
    /// </summary>
    private static readonly (DependencyProperty Color, double CenterX, double CenterY, double Radius)[] Blooms =
    [
        (Bloom1Property, 0.06, -0.06, 0.46),
        (Bloom2Property, 0.96, 0.24, 0.44),
        (Bloom3Property, 0.02, 0.64, 0.48),
        (Bloom4Property, 0.92, 1.00, 0.50),
    ];

    public BloomLayer()
    {
        IsHitTestVisible = false;
    }

    /// <summary>ground.base. Opaque.</summary>
    public Brush? Base
    {
        get => (Brush?)GetValue(BaseProperty);
        set => SetValue(BaseProperty, value);
    }

    public Color Bloom1
    {
        get => (Color)GetValue(Bloom1Property);
        set => SetValue(Bloom1Property, value);
    }

    public Color Bloom2
    {
        get => (Color)GetValue(Bloom2Property);
        set => SetValue(Bloom2Property, value);
    }

    public Color Bloom3
    {
        get => (Color)GetValue(Bloom3Property);
        set => SetValue(Bloom3Property, value);
    }

    public Color Bloom4
    {
        get => (Color)GetValue(Bloom4Property);
        set => SetValue(Bloom4Property, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(Base, null, bounds);

        // Paint order, bottom to top: bloom 4, 3, 2, 1.
        var longerSide = Math.Max(bounds.Width, bounds.Height);
        for (var index = Blooms.Length - 1; index >= 0; index--)
        {
            var (property, centerX, centerY, radiusFraction) = Blooms[index];
            var color = (Color)GetValue(property);
            if (color.A == 0)
            {
                continue;
            }

            var center = new Point(centerX * bounds.Width, centerY * bounds.Height);
            var radius = radiusFraction * longerSide;
            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = center,
                GradientOrigin = center,
                RadiusX = radius,
                RadiusY = radius,
                GradientStops =
                {
                    new GradientStop(color, 0),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), FadeEnd),
                },
            };
            brush.Freeze();
            drawingContext.DrawRectangle(brush, null, bounds);
        }
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) => DependencyProperty.Register(
        name,
        typeof(T),
        typeof(BloomLayer),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
