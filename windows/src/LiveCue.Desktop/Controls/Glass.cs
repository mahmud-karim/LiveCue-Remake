using System.Windows;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The corner radius of a glass surface. Each Glass.* style sets its token (radius.panel,
/// radius.card); a variant style or a call site overrides it where the design nests one surface in
/// another and the inner radius has to stay concentric.
/// </summary>
public static class Glass
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius",
        typeof(CornerRadius),
        typeof(Glass),
        new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender));

    public static CornerRadius GetCornerRadius(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (CornerRadius)element.GetValue(CornerRadiusProperty);
    }

    public static void SetCornerRadius(DependencyObject element, CornerRadius value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CornerRadiusProperty, value);
    }
}
