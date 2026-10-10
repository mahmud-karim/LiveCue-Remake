using System.Windows;
using System.Windows.Controls;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// One row of the prerequisite list (design system 4.8, "Check row"; screens 2.6): the state of a check
/// as a glyph (check for a pass, alert for a failure, a ring for a check that was not made), its title
/// and its fix or detail line. Not interactive. Its look is the <c>Row.Check</c> style.
/// </summary>
public sealed class CheckRow : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PrerequisiteState), typeof(CheckRow), new PropertyMetadata(PrerequisiteState.NotChecked));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CheckRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(CheckRow), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowSeparatorProperty = DependencyProperty.Register(
        nameof(ShowSeparator), typeof(bool), typeof(CheckRow), new PropertyMetadata(true));

    public PrerequisiteState State
    {
        get => (PrerequisiteState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>
    /// The hairline above the row. In an items control the first row hides it by itself; set it to
    /// false for a first row that is placed by hand.
    /// </summary>
    public bool ShowSeparator
    {
        get => (bool)GetValue(ShowSeparatorProperty);
        set => SetValue(ShowSeparatorProperty, value);
    }
}
