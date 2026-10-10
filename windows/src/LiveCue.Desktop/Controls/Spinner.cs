using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The indeterminate progress mark (design system 4.13): a 270-degree arc in the current text colour
/// that makes one turn every 900 ms. 20 across by default; 16 in buttons, 12 in chips and indicators.
/// It turns only while it is visible, and it keeps turning when Windows has animations off, because it
/// is the only sign of work (6.7). The <c>Spinner</c> style sets its size and colour.
/// </summary>
public sealed class Spinner : Control
{
    /// <summary>motion.spin.</summary>
    private static readonly Duration Turn = new(TimeSpan.FromMilliseconds(900));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Spinner),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly RotateTransform _rotation = new();

    static Spinner()
    {
        ForegroundProperty.OverrideMetadata(
            typeof(Spinner),
            new FrameworkPropertyMetadata(
                SystemColors.ControlTextBrush,
                FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public Spinner()
    {
        // Snapping would move each edge of the arc separately and make its weight uneven as it turns.
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        IsHitTestVisible = false;
        Focusable = false;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotation;
        IsVisibleChanged += (_, _) => TurnWhileVisible();
    }

    /// <summary>The weight of the arc. Unset: 2.5, or 2 below 16 across.</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var side = Math.Min(RenderSize.Width, RenderSize.Height);
        if (side <= 0 || Foreground is not { } brush)
        {
            return;
        }

        var thickness = double.IsNaN(StrokeThickness) ? (side < 16 ? 2.0 : 2.5) : StrokeThickness;
        var radius = (side - thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X - radius, center.Y);
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            // From twelve o'clock clockwise to nine o'clock: three quarters of the circle.
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, isLargeArc: true, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        arc.Freeze();
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        drawingContext.DrawGeometry(null, pen, arc);
    }

    private void TurnWhileVisible()
    {
        if (IsVisible)
        {
            var animation = new DoubleAnimation(0, 360, Turn) { RepeatBehavior = RepeatBehavior.Forever };
            _rotation.BeginAnimation(RotateTransform.AngleProperty, animation);
        }
        else
        {
            _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
