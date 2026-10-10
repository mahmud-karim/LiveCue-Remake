using System.Windows;
using System.Windows.Controls;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// A row with a leading part and a trailing part: the first child at the left, the last at the right.
/// When the two do not fit side by side, the last child wraps under the first, left-aligned. The Live
/// status strip uses it: "the status strip wraps the PC address under the two statuses if it does not
/// fit" (docs/design/03-windows-screens.md, section 7).
/// </summary>
public sealed class SplitRowPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap),
        typeof(double),
        typeof(SplitRowPanel),
        new FrameworkPropertyMetadata(32.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty WrapGapProperty = DependencyProperty.Register(
        nameof(WrapGap),
        typeof(double),
        typeof(SplitRowPanel),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The least space between the two parts while they share a line.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>The space between the two lines once the trailing part has wrapped.</summary>
    public double WrapGap
    {
        get => (double)GetValue(WrapGapProperty);
        set => SetValue(WrapGapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0)
        {
            return default;
        }

        var leading = InternalChildren[0];
        var trailing = InternalChildren.Count > 1 ? InternalChildren[^1] : null;
        var widthOnly = new Size(availableSize.Width, double.PositiveInfinity);

        leading.Measure(widthOnly);
        if (trailing is null)
        {
            return leading.DesiredSize;
        }

        trailing.Measure(widthOnly);
        var sideBySide = leading.DesiredSize.Width + Gap + trailing.DesiredSize.Width;
        if (Fits(sideBySide, availableSize.Width))
        {
            var width = double.IsInfinity(availableSize.Width) ? sideBySide : availableSize.Width;
            return new Size(width, Math.Max(leading.DesiredSize.Height, trailing.DesiredSize.Height));
        }

        return new Size(
            Math.Max(leading.DesiredSize.Width, trailing.DesiredSize.Width),
            leading.DesiredSize.Height + WrapGap + trailing.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var leading = InternalChildren[0];
        var trailing = InternalChildren.Count > 1 ? InternalChildren[^1] : null;
        if (trailing is null)
        {
            leading.Arrange(new Rect(finalSize));
            return finalSize;
        }

        var sideBySide = leading.DesiredSize.Width + Gap + trailing.DesiredSize.Width;
        if (Fits(sideBySide, finalSize.Width))
        {
            leading.Arrange(new Rect(0, 0, leading.DesiredSize.Width, finalSize.Height));
            trailing.Arrange(new Rect(
                finalSize.Width - trailing.DesiredSize.Width, 0, trailing.DesiredSize.Width, finalSize.Height));
        }
        else
        {
            leading.Arrange(new Rect(0, 0, finalSize.Width, leading.DesiredSize.Height));
            trailing.Arrange(new Rect(
                0, leading.DesiredSize.Height + WrapGap, finalSize.Width, trailing.DesiredSize.Height));
        }

        return finalSize;
    }

    private static bool Fits(double needed, double available) => needed <= available + 0.5;
}
