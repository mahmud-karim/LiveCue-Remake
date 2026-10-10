using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// Turns a resource key held by a view model (for example "Icon.Mic") into the resource itself, so
/// view models can name an icon without referencing any WPF type.
/// </summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource(key) is { } resource
            ? resource
            : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True becomes Visible, false becomes Collapsed; pass "Invert" as the parameter to swap them.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (parameter is "Invert")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
