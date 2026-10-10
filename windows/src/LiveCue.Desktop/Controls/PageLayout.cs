using System.Windows;
using System.Windows.Controls;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The frame every page shares (screens 1.4): the page header with the view's name on the left and,
/// on the right, the page's own controls; then the page's content, which fills the rest.
///
/// First version (foundation): the properties. The four pages still write their header by hand, as
/// the spike did; the desktop core gives this control its template and moves them onto it.
/// </summary>
public sealed class PageLayout : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageLayout), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent), typeof(object), typeof(PageLayout), new PropertyMetadata(null));

    /// <summary>The view's name, shown in type.display.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What sits at the right of the header: the page's buttons.</summary>
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }
}
