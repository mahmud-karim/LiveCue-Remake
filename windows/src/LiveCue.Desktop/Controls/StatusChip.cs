using System.Windows;
using System.Windows.Controls;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The five states a status can be drawn in (design system 2.7 and 4.12). Each has its own colour
/// and its own mark shape, and is always shown with a label in words.
/// </summary>
public enum StatusKind
{
    /// <summary>Offline, not paired, idle. Hollow ring.</summary>
    Neutral,

    /// <summary>Microphone open, listening, working. Filled dot in the accent.</summary>
    Live,

    /// <summary>Connected, paired, done. Filled dot.</summary>
    Success,

    /// <summary>Paused, reconnecting, expiring. Filled dot.</summary>
    Warning,

    /// <summary>Error. Alert glyph.</summary>
    Danger,
}

/// <summary>
/// The small mark that goes with a status label: a dot, a ring or an alert glyph. (The contract's
/// <see cref="StatusMark"/> enum names the mark of an indicator; this control draws one.)
/// </summary>
public sealed class StatusMarkView : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(StatusKind), typeof(StatusMarkView), new PropertyMetadata(StatusKind.Neutral));

    public StatusKind Kind
    {
        get => (StatusKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
}

/// <summary>A compact, non-interactive state label (design system 4.12).</summary>
public sealed class StatusChip : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(StatusKind), typeof(StatusChip), new PropertyMetadata(StatusKind.Neutral));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsFloatingProperty = DependencyProperty.Register(
        nameof(IsFloating), typeof(bool), typeof(StatusChip), new PropertyMetadata(false));

    public StatusKind Kind
    {
        get => (StatusKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>The label. It always carries the meaning in words.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>True when the chip sits directly on the ground, where it gets shadow.raised.</summary>
    public bool IsFloating
    {
        get => (bool)GetValue(IsFloatingProperty);
        set => SetValue(IsFloatingProperty, value);
    }
}
