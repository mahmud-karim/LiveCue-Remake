using System.Windows;
using System.Windows.Media;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The in-app brand mark below 64 px (design system 8.2): the speech bubble and its three voice
/// bars, flat, with no tile. Geometry is the master artwork's, moved so the bubble starts at the
/// origin. Set the height; the width follows.
/// </summary>
public sealed class BrandMark : FrameworkElement
{
    private const double ArtWidth = 560;
    private const double ArtHeight = 604;

    public static readonly DependencyProperty BubbleProperty = DependencyProperty.Register(
        nameof(Bubble),
        typeof(Brush),
        typeof(BrandMark),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars),
        typeof(Brush),
        typeof(BrandMark),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly GeometryGroup BubbleGeometry = CreateBubble();
    private static readonly GeometryGroup BarsGeometry = CreateBars();

    public BrandMark()
    {
        SnapsToDevicePixels = false;
        IsHitTestVisible = false;
    }

    public Brush? Bubble
    {
        get => (Brush?)GetValue(BubbleProperty);
        set => SetValue(BubbleProperty, value);
    }

    public Brush? Bars
    {
        get => (Brush?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = double.IsInfinity(availableSize.Height) ? 20 : availableSize.Height;
        return new Size(height * ArtWidth / ArtHeight, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (RenderSize.Height <= 0)
        {
            return;
        }

        var scale = RenderSize.Height / ArtHeight;
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawGeometry(Bubble, null, BubbleGeometry);
        drawingContext.DrawGeometry(Bars, null, BarsGeometry);
        drawingContext.Pop();
    }

    private static GeometryGroup CreateBubble()
    {
        var bubble = new GeometryGroup { FillRule = FillRule.Nonzero };
        bubble.Children.Add(new RectangleGeometry(new Rect(0, 0, 560, 460), 150, 150));
        bubble.Children.Add(Geometry.Parse("M 98,418 L 64,578 C 60,596 74,604 88,594 L 276,458 Z"));
        bubble.Freeze();
        return bubble;
    }

    private static GeometryGroup CreateBars()
    {
        // Three capsules 68 wide on the same centre line: short, tall, medium.
        const double centerY = 230;
        var bars = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var (centerX, height) in new[] { (160.0, 150.0), (280.0, 270.0), (400.0, 200.0) })
        {
            bars.Children.Add(new RectangleGeometry(new Rect(centerX - 34, centerY - (height / 2), 68, height), 34, 34));
        }

        bars.Freeze();
        return bars;
    }
}
