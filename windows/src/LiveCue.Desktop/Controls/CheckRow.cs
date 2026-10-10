using System.Windows;
using System.Windows.Controls;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// One row of the prerequisite list (screens 2.6): the state of a check, its title and its fix or
/// detail line. Not interactive.
/// </summary>
public sealed class CheckRow : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PrerequisiteState), typeof(CheckRow), new PropertyMetadata(PrerequisiteState.NotChecked));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CheckRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(CheckRow), new PropertyMetadata(null));

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
}
