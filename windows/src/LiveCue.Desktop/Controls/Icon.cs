using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The drawing of one icon on the design system's 24-unit grid (2.18): a part that is stroked as a
/// line and, for the few state glyphs that must read at a glance, a part that is filled.
/// </summary>
public sealed class IconGlyph
{
    /// <summary>Drawn with a round-capped, round-joined line. No fill.</summary>
    public Geometry? Stroke { get; set; }

    /// <summary>Filled solid. Author its corners rounded; it is not stroked.</summary>
    public Geometry? Fill { get; set; }
}

/// <summary>
/// Draws an <see cref="IconGlyph"/> scaled as a whole to the element's size, so the stroke scales
/// with the icon. The colour is the inherited text colour: an icon always matches the text beside it.
/// </summary>
public sealed class Icon : FrameworkElement
{
    /// <summary>Side of the square grid icons are drawn on.</summary>
    public const double GridSize = 24;

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(IconGlyph),
        typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon),
        new FrameworkPropertyMetadata(
            SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint),
        typeof(Brush),
        typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Icon),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Icon()
    {
        // Snapping would move each edge of a scaled stroke separately and make its weight uneven.
        SnapsToDevicePixels = false;
        IsHitTestVisible = false;
    }

    public IconGlyph? Glyph
    {
        get => (IconGlyph?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// A colour for this icon alone, for the rare glyph that does not match its label (the red stop
    /// glyph beside an ink "Stop relay"). Null, the default, means the inherited text colour.
    /// </summary>
    public Brush? Tint
    {
        get => (Brush?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>Line weight in grid units. 2 for icons; caption glyphs use a heavier value at 10 px.</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? GridSize : Math.Min(GridSize, availableSize.Width),
        double.IsInfinity(availableSize.Height) ? GridSize : Math.Min(GridSize, availableSize.Height));

    protected override void OnRender(DrawingContext drawingContext)
    {
        var glyph = Glyph;
        var brush = Tint ?? Foreground;
        var side = Math.Min(RenderSize.Width, RenderSize.Height);
        if (glyph is null || brush is null || side <= 0)
        {
            return;
        }

        var scale = side / GridSize;
        drawingContext.PushTransform(new TranslateTransform(
            (RenderSize.Width - side) / 2,
            (RenderSize.Height - side) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        if (glyph.Fill is { } fill)
        {
            drawingContext.DrawGeometry(brush, null, fill);
        }

        if (glyph.Stroke is { } stroke)
        {
            var pen = new Pen(brush, StrokeThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            if (pen.CanFreeze)
            {
                pen.Freeze();
            }

            drawingContext.DrawGeometry(null, pen, stroke);
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }
}
