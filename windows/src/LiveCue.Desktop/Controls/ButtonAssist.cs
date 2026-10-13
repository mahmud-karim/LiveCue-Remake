using System.Windows;
using System.Windows.Media;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// Extras the button styles understand, so a call site stays one line:
/// <c>&lt;Button Style="{DynamicResource Button.Primary}" ui:ButtonAssist.Icon="{StaticResource Icon.Play}" Content="Start relay"/&gt;</c>.
/// </summary>
public static class ButtonAssist
{
    /// <summary>Leading icon, drawn before the label in the label's colour.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(IconGlyph), typeof(ButtonAssist), new PropertyMetadata(null));

    /// <summary>Side of the leading icon: 16 in regular and compact buttons, 20 in large ones.</summary>
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.RegisterAttached(
        "IconSize", typeof(double), typeof(ButtonAssist), new PropertyMetadata(16.0));

    /// <summary>A colour for the icon alone; null means the label's colour.</summary>
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.RegisterAttached(
        "IconBrush", typeof(Brush), typeof(ButtonAssist), new PropertyMetadata(null));

    /// <summary>
    /// The button is doing what it was pressed for (design system 4.1, "Busy"): the leading icon becomes a
    /// spinner, the label stays, the button ignores the pointer, and it keeps its full opacity also
    /// while its command is disabled.
    /// </summary>
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached(
        "IsBusy", typeof(bool), typeof(ButtonAssist), new PropertyMetadata(false));

    public static bool GetIsBusy(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsBusyProperty);
    }

    public static void SetIsBusy(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsBusyProperty, value);
    }

    public static IconGlyph? GetIcon(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (IconGlyph?)element.GetValue(IconProperty);
    }

    public static void SetIcon(DependencyObject element, IconGlyph? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IconProperty, value);
    }

    public static double GetIconSize(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(IconSizeProperty);
    }

    public static void SetIconSize(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IconSizeProperty, value);
    }

    public static Brush? GetIconBrush(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(IconBrushProperty);
    }

    public static void SetIconBrush(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IconBrushProperty, value);
    }
}
