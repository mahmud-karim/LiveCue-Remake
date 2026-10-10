using System.Windows;
using System.Windows.Controls;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// Two panels side by side with a gap, sharing the width by their weights; while the panel is narrower
/// than <see cref="StackBelow"/> they stack, the first above the second (W-design 5.10: "side-by-side
/// panels stack"). Only the first two children are laid out.
///
/// First version (foundation): no page uses it yet; the Live page still uses a grid, as the spike did.
/// </summary>
public sealed class TwoColumnPanel : Panel
{
    public static readonly DependencyProperty GapProperty = Register(nameof(Gap), 16.0);
    public static readonly DependencyProperty FirstWeightProperty = Register(nameof(FirstWeight), 1.0);
    public static readonly DependencyProperty SecondWeightProperty = Register(nameof(SecondWeight), 1.0);
    public static readonly DependencyProperty StackBelowProperty = Register(nameof(StackBelow), 0.0);

    /// <summary>The space between the two children.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double FirstWeight
    {
        get => (double)GetValue(FirstWeightProperty);
        set => SetValue(FirstWeightProperty, value);
    }

    public double SecondWeight
    {
        get => (double)GetValue(SecondWeightProperty);
        set => SetValue(SecondWeightProperty, value);
    }

    /// <summary>The children stack while the panel is narrower than this. 0 never stacks.</summary>
    public double StackBelow
    {
        get => (double)GetValue(StackBelowProperty);
        set => SetValue(StackBelowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var stacked = IsStacked(availableSize.Width);
        var unbounded = double.IsInfinity(availableSize.Width);
        double width = 0;
        double height = 0;
        for (var index = 0; index < Math.Min(2, InternalChildren.Count); index++)
        {
            var child = InternalChildren[index];
            child.Measure(new Size(
                stacked || unbounded ? availableSize.Width : ColumnWidth(availableSize.Width, index),
                stacked ? double.PositiveInfinity : availableSize.Height));

            var gap = index == 0 ? 0 : Gap;
            if (stacked)
            {
                width = Math.Max(width, child.DesiredSize.Width);
                height += child.DesiredSize.Height + gap;
            }
            else
            {
                width += child.DesiredSize.Width + gap;
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        return new Size(unbounded ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        var first = InternalChildren[0];
        var second = InternalChildren.Count > 1 ? InternalChildren[1] : null;
        if (IsStacked(finalSize.Width))
        {
            var firstHeight = first.DesiredSize.Height;
            first.Arrange(new Rect(0, 0, finalSize.Width, firstHeight));
            second?.Arrange(new Rect(
                0, firstHeight + Gap, finalSize.Width, Math.Max(0, finalSize.Height - firstHeight - Gap)));
        }
        else
        {
            var firstWidth = second is null ? finalSize.Width : ColumnWidth(finalSize.Width, 0);
            first.Arrange(new Rect(0, 0, firstWidth, finalSize.Height));
            second?.Arrange(new Rect(firstWidth + Gap, 0, ColumnWidth(finalSize.Width, 1), finalSize.Height));
        }

        return finalSize;
    }

    private bool IsStacked(double width) => !double.IsInfinity(width) && width < StackBelow;

    private double ColumnWidth(double total, int index)
    {
        var usable = Math.Max(0, total - Gap);
        var weights = Math.Max(FirstWeight + SecondWeight, double.Epsilon);
        var first = Math.Round(usable * FirstWeight / weights);
        return index == 0 ? first : usable - first;
    }

    private static DependencyProperty Register(string name, double defaultValue) => DependencyProperty.Register(
        name,
        typeof(double),
        typeof(TwoColumnPanel),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsMeasure));
}
