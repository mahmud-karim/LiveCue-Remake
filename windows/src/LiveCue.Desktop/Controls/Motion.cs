using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LiveCue.Desktop.Theming;

namespace LiveCue.Desktop.Controls;

/// <summary>
/// The few movements the design system allows on Windows (7.1): an opacity or an offset that travels
/// to a new value with ease.standard. A style sets the target with a plain setter
/// (<c>ui:Motion.OffsetX="22"</c>, <c>ui:Motion.Fade="1"</c>); the element then goes there over
/// <see cref="DurationProperty"/>, or at once when it is not on screen yet, so nothing animates when a
/// view appears, and at once when Windows has animations off (6.7).
///
/// Use it on layers without text only: a text under a moving transform or a changing opacity loses
/// ClearType for as long as it lasts (3.5). The code-behind entrances of the window use
/// <see cref="Run"/> and clear the animated property when they end, so at rest every transform is the
/// identity and every opacity is exactly 1.
/// </summary>
public static class Motion
{
    /// <summary>ease.standard: cubic-bezier(0.2, 0, 0, 1).</summary>
    public static readonly KeySpline Standard = Frozen(new KeySpline(0.2, 0, 0, 1));

    /// <summary>ease.out: cubic-bezier(0, 0, 0.2, 1).</summary>
    public static readonly KeySpline Out = Frozen(new KeySpline(0, 0, 0.2, 1));

    /// <summary>ease.in: cubic-bezier(0.4, 0, 1, 1).</summary>
    public static readonly KeySpline In = Frozen(new KeySpline(0.4, 0, 1, 1));

    public static readonly TimeSpan Press = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(160);
    public static readonly TimeSpan Base = TimeSpan.FromMilliseconds(240);

    /// <summary>How long a change of <see cref="OffsetXProperty"/> or <see cref="FadeProperty"/> takes. motion.fast unless set.</summary>
    public static readonly DependencyProperty DurationProperty = DependencyProperty.RegisterAttached(
        "Duration", typeof(Duration), typeof(Motion), new PropertyMetadata(new Duration(Fast)));

    /// <summary>The horizontal offset the element rests at.</summary>
    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.RegisterAttached(
        "OffsetX", typeof(double), typeof(Motion), new PropertyMetadata(0.0, OnOffsetXChanged));

    /// <summary>The opacity the element rests at.</summary>
    public static readonly DependencyProperty FadeProperty = DependencyProperty.RegisterAttached(
        "Fade", typeof(double), typeof(Motion), new PropertyMetadata(1.0, OnFadeChanged));

    public static Duration GetDuration(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Duration)element.GetValue(DurationProperty);
    }

    public static void SetDuration(DependencyObject element, Duration value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(DurationProperty, value);
    }

    public static double GetOffsetX(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(OffsetXProperty);
    }

    public static void SetOffsetX(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(OffsetXProperty, value);
    }

    public static double GetFade(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(FadeProperty);
    }

    public static void SetFade(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FadeProperty, value);
    }

    /// <summary>True when a change that is asked for now should travel rather than jump.</summary>
    public static bool IsEnabledFor(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return SystemPreferences.AnimationsEnabled && element.IsVisible && element is not FrameworkElement { IsLoaded: false };
    }

    /// <summary>
    /// Takes <paramref name="property"/> of <paramref name="target"/> from <paramref name="from"/> to
    /// <paramref name="to"/>. When it ends the animation is removed, the property holds
    /// <paramref name="to"/> as a plain value, and <paramref name="then"/> runs. With
    /// <paramref name="animate"/> false all of that happens at once.
    /// </summary>
    public static void Run(
        IAnimatable target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration,
        KeySpline ease,
        bool animate,
        Action? then = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        var element = (DependencyObject)target;
        if (!animate || duration <= TimeSpan.Zero)
        {
            target.BeginAnimation(property, null);
            element.SetCurrentValue(property, to);
            then?.Invoke();
            return;
        }

        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), ease));
        animation.Completed += (_, _) => then?.Invoke();

        // The resting value is set first, so when the animation stops the property is already there.
        element.SetCurrentValue(property, to);
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnOffsetXChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is not UIElement visual)
        {
            return;
        }

        if (visual.RenderTransform is not TranslateTransform { IsFrozen: false } translate)
        {
            translate = new TranslateTransform();
            visual.RenderTransform = translate;
        }

        var to = (double)change.NewValue;
        Run(translate, TranslateTransform.XProperty, translate.X, to, DurationOf(element), Standard, IsEnabledFor(visual));
    }

    private static void OnFadeChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is UIElement visual)
        {
            var to = (double)change.NewValue;
            Run(visual, UIElement.OpacityProperty, visual.Opacity, to, DurationOf(element), Standard, IsEnabledFor(visual));
        }
    }

    private static TimeSpan DurationOf(DependencyObject element) =>
        GetDuration(element) is { HasTimeSpan: true } duration ? duration.TimeSpan : Fast;

    private static KeySpline Frozen(KeySpline spline)
    {
        spline.Freeze();
        return spline;
    }
}
