using System.Windows;
using System.Windows.Controls;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// radius.pill for a <see cref="Border"/>: keeps the corner radius at exactly half the border's
/// height. A fixed large radius does not work in WPF (it squashes the ends into ellipses), and a
/// fixed exact one breaks as soon as the control grows with its text.
/// </summary>
public static class Pill
{
    public static readonly DependencyProperty IsPillProperty = DependencyProperty.RegisterAttached(
        "IsPill",
        typeof(bool),
        typeof(Pill),
        new PropertyMetadata(false, OnIsPillChanged));

    public static bool GetIsPill(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsPillProperty);
    }

    public static void SetIsPill(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsPillProperty, value);
    }

    private static void OnIsPillChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is not Border border)
        {
            return;
        }

        border.SizeChanged -= OnBorderSizeChanged;
        if ((bool)change.NewValue)
        {
            border.SizeChanged += OnBorderSizeChanged;
            Apply(border);
        }
    }

    private static void OnBorderSizeChanged(object sender, SizeChangedEventArgs change)
    {
        if (change.HeightChanged)
        {
            Apply((Border)sender);
        }
    }

    private static void Apply(Border border) =>
        border.SetCurrentValue(Border.CornerRadiusProperty, new CornerRadius(border.ActualHeight / 2));
}
