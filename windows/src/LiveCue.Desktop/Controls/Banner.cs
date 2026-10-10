using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// A banner (design system 4.11): a tone, an optional title, a body and at most one quiet button.
/// Bind <see cref="Model"/> to a <see cref="BannerModel"/>, or set the parts one by one.
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
        nameof(Body), typeof(string), typeof(Banner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionLabelProperty = DependencyProperty.Register(
        nameof(ActionLabel), typeof(string), typeof(Banner), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(Banner), new PropertyMetadata(null));

    /// <summary>Sets tone, title, body and action label together. Null hides the banner.</summary>
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

    private static void OnModelChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        var banner = (Banner)element;
        var model = (BannerModel?)change.NewValue;
        banner.SetCurrentValue(VisibilityProperty, model is null ? Visibility.Collapsed : Visibility.Visible);
        if (model is null)
        {
            return;
        }

        banner.SetCurrentValue(ToneProperty, model.Tone);
        banner.SetCurrentValue(TitleProperty, model.Title);
        banner.SetCurrentValue(BodyProperty, model.Body);
        banner.SetCurrentValue(ActionLabelProperty, model.ActionLabel);
    }
}
