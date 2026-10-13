using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// Draws one <see cref="StatusIndicator"/> (design system 4.22): its mark, its text in ink.primary and
/// its tooltip. Each mark has its own shape (ring, dot, spinner, pause, alert, check), so the state can
/// be told without the colour. Its look is the <c>Indicator</c> style.
///
/// With <see cref="Announces"/> set, a change of the text is announced to assistive technology:
/// assertively when the tone is danger, politely otherwise (screens section 8). Set it on the one
/// indicator that should speak, so a status that is shown twice is not read twice.
/// </summary>
public sealed class StatusIndicatorView : Control
{
    public static readonly DependencyProperty IndicatorProperty = DependencyProperty.Register(
        nameof(Indicator),
        typeof(StatusIndicator),
        typeof(StatusIndicatorView),
        new PropertyMetadata(null, OnIndicatorChanged));

    public static readonly DependencyProperty AnnouncesProperty = DependencyProperty.Register(
        nameof(Announces), typeof(bool), typeof(StatusIndicatorView), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey KindPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Kind), typeof(StatusKind), typeof(StatusIndicatorView), new PropertyMetadata(StatusKind.Neutral));

    private static readonly DependencyPropertyKey MarkPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Mark), typeof(StatusMark), typeof(StatusIndicatorView), new PropertyMetadata(StatusMark.Ring));

    private static readonly DependencyPropertyKey TonePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Tone), typeof(StatusTone), typeof(StatusIndicatorView), new PropertyMetadata(StatusTone.Offline));

    private static readonly DependencyPropertyKey TextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Text), typeof(string), typeof(StatusIndicatorView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty KindProperty = KindPropertyKey.DependencyProperty;

    public static readonly DependencyProperty MarkProperty = MarkPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ToneProperty = TonePropertyKey.DependencyProperty;

    public static readonly DependencyProperty TextProperty = TextPropertyKey.DependencyProperty;

    /// <summary>What to show. Null shows nothing.</summary>
    public StatusIndicator? Indicator
    {
        get => (StatusIndicator?)GetValue(IndicatorProperty);
        set => SetValue(IndicatorProperty, value);
    }

    /// <summary>True: this indicator is a live region and its changes are spoken.</summary>
    public bool Announces
    {
        get => (bool)GetValue(AnnouncesProperty);
        set => SetValue(AnnouncesProperty, value);
    }

    /// <summary>The indicator's mark and tone as one of the five status looks of the chip.</summary>
    public StatusKind Kind => (StatusKind)GetValue(KindProperty);

    /// <summary>The shape of the mark.</summary>
    public StatusMark Mark => (StatusMark)GetValue(MarkProperty);

    /// <summary>The colour of the mark.</summary>
    public StatusTone Tone => (StatusTone)GetValue(ToneProperty);

    /// <summary>The indicator's text.</summary>
    public string Text => (string)GetValue(TextProperty);

    protected override AutomationPeer OnCreateAutomationPeer() => new StatusIndicatorPeer(this);

    private static void OnIndicatorChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        var view = (StatusIndicatorView)element;
        var indicator = (StatusIndicator?)change.NewValue;
        var before = view.Text;

        view.SetValue(TextPropertyKey, indicator?.Text ?? string.Empty);
        view.SetValue(MarkPropertyKey, indicator?.Mark ?? StatusMark.Ring);
        view.SetValue(TonePropertyKey, indicator?.Tone ?? StatusTone.Offline);
        view.SetValue(KindPropertyKey, indicator is null ? StatusKind.Neutral : KindOf(indicator));

        // The tooltip is the indicator's own; without one, a long status still shows its full text.
        view.ToolTip = string.IsNullOrEmpty(indicator?.ToolTip) ? null : indicator.ToolTip;

        if (view.Announces && !string.Equals(before, view.Text, StringComparison.Ordinal))
        {
            view.SetCurrentValue(
                AutomationProperties.LiveSettingProperty,
                view.Tone == StatusTone.Danger ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
            UIElementAutomationPeer.FromElement(view)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
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

    /// <summary>Read as one text element: the mark is decoration, the words carry the state.</summary>
    private sealed class StatusIndicatorPeer(StatusIndicatorView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(StatusIndicatorView);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? ((StatusIndicatorView)Owner).Text : name;
        }

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
