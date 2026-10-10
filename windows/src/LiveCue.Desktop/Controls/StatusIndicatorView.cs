using System.Windows;
using System.Windows.Controls;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// Draws one <see cref="StatusIndicator"/> (design system 4.22): its mark, its text and its tooltip.
///
/// First version (foundation): the mark is one of the three shapes the spike has (ring, dot, alert);
/// a spinner, pause or check mark is drawn as a dot. The desktop core gives each mark its own shape.
/// </summary>
public sealed class StatusIndicatorView : Control
{
    public static readonly DependencyProperty IndicatorProperty = DependencyProperty.Register(
        nameof(Indicator),
        typeof(StatusIndicator),
        typeof(StatusIndicatorView),
        new PropertyMetadata(null, OnIndicatorChanged));

    private static readonly DependencyPropertyKey KindPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Kind), typeof(StatusKind), typeof(StatusIndicatorView), new PropertyMetadata(StatusKind.Neutral));

    private static readonly DependencyPropertyKey TextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Text), typeof(string), typeof(StatusIndicatorView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty KindProperty = KindPropertyKey.DependencyProperty;

    public static readonly DependencyProperty TextProperty = TextPropertyKey.DependencyProperty;

    /// <summary>What to show. Null shows nothing.</summary>
    public StatusIndicator? Indicator
    {
        get => (StatusIndicator?)GetValue(IndicatorProperty);
        set => SetValue(IndicatorProperty, value);
    }

    /// <summary>The colour and shape of the mark, from the indicator's mark and tone.</summary>
    public StatusKind Kind => (StatusKind)GetValue(KindProperty);

    /// <summary>The indicator's text.</summary>
    public string Text => (string)GetValue(TextProperty);

    private static void OnIndicatorChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        var view = (StatusIndicatorView)element;
        var indicator = (StatusIndicator?)change.NewValue;
        view.SetValue(TextPropertyKey, indicator?.Text ?? string.Empty);
        view.SetValue(KindPropertyKey, indicator is null ? StatusKind.Neutral : KindOf(indicator));
        view.ToolTip = indicator?.ToolTip;
    }

    private static StatusKind KindOf(StatusIndicator indicator) => indicator.Mark switch
    {
        StatusMark.Ring => StatusKind.Neutral,
        StatusMark.Alert => StatusKind.Danger,
        _ => indicator.Tone switch
        {
            StatusTone.Live => StatusKind.Live,
            StatusTone.Success => StatusKind.Success,
            StatusTone.Warning => StatusKind.Warning,
            StatusTone.Danger => StatusKind.Danger,
            _ => StatusKind.Neutral,
        },
    };
}
