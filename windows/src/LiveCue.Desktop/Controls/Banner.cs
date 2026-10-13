using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// A banner (design system 4.11): a persistent message inside the content, with a tone, an optional
/// title, a body and at most one quiet button. Bind <see cref="Model"/> to a <see cref="BannerModel"/>,
/// or set the parts one by one. Its look is the <c>Banner</c> style: a raised base under the tone's
/// tint, and the tone's glyph (information, check or alert), so the tone never rests on colour alone.
/// A banner without a body takes no room: a model that is null, or was never set, shows nothing.
///
/// A banner is read as one element. When it appears or its text changes it is announced once:
/// assertively for a warning or a danger, politely otherwise (design system 6.2).
/// </summary>
public sealed class Banner : Control
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(BannerModel), typeof(Banner), new PropertyMetadata(null, OnModelChanged));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(BannerTone), typeof(Banner), new PropertyMetadata(BannerTone.Info));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Banner), new PropertyMetadata(null));

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(string), typeof(Banner), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty ActionLabelProperty = DependencyProperty.Register(
        nameof(ActionLabel), typeof(string), typeof(Banner), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(Banner), new PropertyMetadata(null));

    /// <summary>Sets tone, title, body and action label together. Null empties the banner, which hides it.</summary>
    public BannerModel? Model
    {
        get => (BannerModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public BannerTone Tone
    {
        get => (BannerTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Body
    {
        get => (string)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>The label of the banner's one quiet button; null means no button.</summary>
    public string? ActionLabel
    {
        get => (string?)GetValue(ActionLabelProperty);
        set => SetValue(ActionLabelProperty, value);
    }

    /// <summary>What the button runs.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new BannerPeer(this);

    private void Announce()
    {
        if (!IsVisible || string.IsNullOrEmpty(Body))
        {
            return;
        }

        SetCurrentValue(
            AutomationProperties.LiveSettingProperty,
            Tone is BannerTone.Warning or BannerTone.Danger ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        // With a model the banner speaks once, when the whole model has been taken over.
        var banner = (Banner)element;
        if (banner.Model is null)
        {
            banner.Announce();
        }
    }

    private static void OnModelChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        var banner = (Banner)element;
        var model = (BannerModel?)change.NewValue;
        if (model is null)
        {
            // Nothing to say: the style hides a banner without a body.
            banner.SetCurrentValue(TitleProperty, null);
            banner.SetCurrentValue(BodyProperty, string.Empty);
            banner.SetCurrentValue(ActionLabelProperty, null);
            return;
        }

        banner.SetCurrentValue(ToneProperty, model.Tone);
        banner.SetCurrentValue(TitleProperty, model.Title);
        banner.SetCurrentValue(BodyProperty, model.Body);
        banner.SetCurrentValue(ActionLabelProperty, model.ActionLabel);
        banner.Announce();
    }

    /// <summary>One element: its name is the title and the body.</summary>
    private sealed class BannerPeer(Banner owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(Banner);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            var banner = (Banner)Owner;
            return string.IsNullOrEmpty(banner.Title) ? banner.Body : banner.Title + ". " + banner.Body;
        }

        protected override bool IsControlElementCore() => true;
    }
}
