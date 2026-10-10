using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// What a button style tells the one shared button template besides the resting fill
/// (<c>Background</c>) and the outline (<c>BorderBrush</c>): the hover fill, the pressed fill and the
/// shadow. Styles set these from tokens through DynamicResource, so they follow the theme.
/// </summary>
public static class ButtonFills
{
    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached(
        "Hover", typeof(Brush), typeof(ButtonFills), new PropertyMetadata(null));

    public static readonly DependencyProperty PressedProperty = DependencyProperty.RegisterAttached(
        "Pressed", typeof(Brush), typeof(ButtonFills), new PropertyMetadata(null));

    public static readonly DependencyProperty ShadowProperty = DependencyProperty.RegisterAttached(
        "Shadow", typeof(Effect), typeof(ButtonFills), new PropertyMetadata(null));

    public static Brush? GetHover(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(HoverProperty);
    }

    public static void SetHover(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HoverProperty, value);
    }

    public static Brush? GetPressed(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(PressedProperty);
    }

    public static void SetPressed(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PressedProperty, value);
    }

    public static Effect? GetShadow(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Effect?)element.GetValue(ShadowProperty);
    }

    public static void SetShadow(DependencyObject element, Effect? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ShadowProperty, value);
    }
}
