using System.Windows;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// Whether the window that owns an element is the active one. The window sets it on itself and the
/// value is inherited down the tree, so title bar styles can dim to ink.secondary when the window is
/// inactive (design system 3.5) without knowing the window class.
/// </summary>
public static class ChromeState
{
    public static readonly DependencyProperty IsWindowActiveProperty = DependencyProperty.RegisterAttached(
        "IsWindowActive",
        typeof(bool),
        typeof(ChromeState),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsWindowActive(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsWindowActiveProperty);
    }

    public static void SetIsWindowActive(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsWindowActiveProperty, value);
    }
}
