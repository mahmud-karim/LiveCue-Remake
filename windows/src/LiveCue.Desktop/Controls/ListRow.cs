using System.Windows;
using System.Windows.Controls.Primitives;

namespace LiveCue.Desktop.Controls;

/// <summary>Colour of a row's icon tile.</summary>
public enum RowTone
{
    /// <summary>accent.tint tile with an accent glyph.</summary>
    Accent,

    /// <summary>Ink-tinted tile with an ink.secondary glyph, for neutral rows.</summary>
    Neutral,
}

/// <summary>
/// A list row (design system 4.8): optional icon tile, title, optional subtitle and a trailing
/// accessory, which is the control's content (value text, a chip, a button). Rows are stacked in a
/// panel padded with InsetPanelRows. Only rows that do something set <see cref="IsInteractive"/>;
/// those get hover and pressed states and can take a command.
/// </summary>
public sealed class ListRow : ButtonBase
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(IconGlyph), typeof(ListRow), new PropertyMetadata(null));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(RowTone), typeof(ListRow), new PropertyMetadata(RowTone.Accent));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ListRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(ListRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowSeparatorProperty = DependencyProperty.Register(
        nameof(ShowSeparator), typeof(bool), typeof(ListRow), new PropertyMetadata(true));

    public static readonly DependencyProperty ShowChevronProperty = DependencyProperty.Register(
        nameof(ShowChevron), typeof(bool), typeof(ListRow), new PropertyMetadata(false));

    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive), typeof(bool), typeof(ListRow), new PropertyMetadata(false));

    public IconGlyph? Glyph
    {
        get => (IconGlyph?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public RowTone Tone
    {
        get => (RowTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>The hairline above the row. Turn it off on the first row of a group.</summary>
    public bool ShowSeparator
    {
        get => (bool)GetValue(ShowSeparatorProperty);
        set => SetValue(ShowSeparatorProperty, value);
    }

    /// <summary>A row that navigates shows a trailing chevron.</summary>
    public bool ShowChevron
    {
        get => (bool)GetValue(ShowChevronProperty);
        set => SetValue(ShowChevronProperty, value);
    }

    /// <summary>True for rows that do something when clicked. Others are plain content.</summary>
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }
}
