using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LiveCue.Desktop.Theming;

/// <summary>The dictionaries that can fill the theme slot of the application's resources.</summary>
public enum AppTheme
{
    /// <summary>The one theme of the app.</summary>
    Daylight,

    /// <summary>Same keys mapped to system colours, used while a Windows contrast theme is on.</summary>
    HighContrast,
}

/// <summary>
/// Owns the theme slot in the application's merged dictionaries, and what Windows asks of the look
/// (W-design 5.10; design system sections 5 and 6).
///
/// Daylight is the only theme, whatever the Windows app mode says. The single switch is to
/// HighContrast.xaml while a Windows contrast theme is active. With "Transparency effects" off the
/// translucent surfaces of Daylight are replaced by their solid twins and the sheen is removed. The
/// Windows text size scales every type token. Styles read all of these through DynamicResource, so
/// each change takes effect while the app is running.
/// </summary>
public sealed class ThemeManager
{
    private static readonly Uri DaylightSource = new("/LiveCueRemake;component/Themes/Daylight.xaml", UriKind.Relative);
    private static readonly Uri HighContrastSource = new("/LiveCueRemake;component/Themes/HighContrast.xaml", UriKind.Relative);

    private readonly ResourceDictionary _applicationResources;
    private ResourceDictionary? _solidTwins;
    private bool _followsSystem;

    public ThemeManager(ResourceDictionary applicationResources)
    {
        ArgumentNullException.ThrowIfNull(applicationResources);
        _applicationResources = applicationResources;
    }

    public AppTheme Current { get; private set; } = AppTheme.Daylight;

    /// <summary>True while the solid twins of design system 6.3 replace the translucent fills.</summary>
    public bool ReducesTransparency { get; private set; }

    /// <summary>The factor the type tokens are scaled by: 1 to 2.25.</summary>
    public double TextScaleFactor { get; private set; } = TextScale.Minimum;

    /// <summary>Applies the look for the current Windows settings and keeps following them.</summary>
    public void FollowSystem()
    {
        if (!_followsSystem)
        {
            _followsSystem = true;
            SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;

            // Text size and "Transparency effects" are announced as a change of user preferences, on a
            // thread of the system's choosing.
            var dispatcher = Dispatcher.CurrentDispatcher;
            SystemEvents.UserPreferenceChanged += (_, _) => dispatcher.BeginInvoke(ApplySystemSettings);
        }

        ApplySystemSettings();
    }

    /// <summary>Puts <paramref name="theme"/> in the theme slot. Used directly only by the self-test.</summary>
    public void Apply(AppTheme theme)
    {
        var merged = _applicationResources.MergedDictionaries;
        var slot = FindThemeSlot();
        var source = theme == AppTheme.HighContrast ? HighContrastSource : DaylightSource;
        if (slot >= 0 && theme == Current && merged[slot].Source is not null)
        {
            return;
        }

        var dictionary = new ResourceDictionary { Source = source };
        if (slot >= 0)
        {
            merged[slot] = dictionary;
        }
        else
        {
            merged.Insert(0, dictionary);
        }

        Current = theme;
        PlaceSolidTwins();
    }

    /// <summary>
    /// Design system 6.3. <paramref name="reduce"/> true replaces the translucent Daylight surfaces by
    /// their solid twins and removes the sheen; the ground, the strokes and the shadows stay. A contrast
    /// theme is opaque already and is left alone.
    /// </summary>
    public void ReduceTransparency(bool reduce)
    {
        ReducesTransparency = reduce;
        PlaceSolidTwins();
    }

    /// <summary>Scales every type token (design system 6.1).</summary>
    public void ScaleText(double factor)
    {
        TextScaleFactor = TextScale.Clamp(factor);
        TextScale.Apply(_applicationResources, TextScaleFactor);
    }

    private void ApplySystemSettings()
    {
        Apply(SystemParameters.HighContrast ? AppTheme.HighContrast : AppTheme.Daylight);
        ReduceTransparency(!SystemPreferences.TransparencyEnabled);
        ScaleText(SystemPreferences.TextScaleFactor);
    }

    /// <summary>The solid twins sit directly above the theme slot, so they win over it and over nothing else.</summary>
    private void PlaceSolidTwins()
    {
        var merged = _applicationResources.MergedDictionaries;
        if (_solidTwins is not null)
        {
            merged.Remove(_solidTwins);
        }

        if (!ReducesTransparency || Current != AppTheme.Daylight)
        {
            return;
        }

        _solidTwins ??= CreateSolidTwins();
        merged.Insert(FindThemeSlot() + 1, _solidTwins);
    }

    private ResourceDictionary CreateSolidTwins()
    {
        var shade = _applicationResources["ShadeColor"] is Color token ? token : Color.FromRgb(0x28, 0x3C, 0x6E);
        return new ResourceDictionary
        {
            ["SurfacePanelBrush"] = Solid(0xF6, 0xF8, 0xFB),
            ["SurfaceInsetBrush"] = Solid(0xFA, 0xFB, 0xFD),
            ["SurfaceRaisedBrush"] = Solid(0xFF, 0xFF, 0xFF),
            ["SurfaceOverlayBrush"] = Solid(0xF8, 0xFB, 0xFF),
            ["SurfaceInverseBrush"] = Solid(0x0E, 0x1B, 0x33),
            ["GlassSheenBrush"] = Frozen(new SolidColorBrush(Colors.Transparent)),

            // An opaque fill casts its whole shadow, so the canonical alphas of 2.13 are used as they are.
            ["ShadowPanel"] = Shadow(shade, depth: 12, blur: 32, opacity: 0.14),
            ["ShadowRaised"] = Shadow(shade, depth: 4, blur: 12, opacity: 0.16),
            ["ShadowOverlay"] = Shadow(shade, depth: 24, blur: 60, opacity: 0.22),
        };
    }

    private static SolidColorBrush Solid(byte red, byte green, byte blue) =>
        Frozen(new SolidColorBrush(Color.FromRgb(red, green, blue)));

    private static DropShadowEffect Shadow(Color shade, double depth, double blur, double opacity) =>
        Frozen(new DropShadowEffect
        {
            Color = ShadowColorExtension.Compensate(shade),
            Direction = 270,
            ShadowDepth = depth,
            BlurRadius = blur,
            Opacity = opacity,
            RenderingBias = RenderingBias.Quality,
        });

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private int FindThemeSlot()
    {
        var merged = _applicationResources.MergedDictionaries;
        for (var index = 0; index < merged.Count; index++)
        {
            var path = merged[index].Source?.OriginalString;
            if (path is not null
                && (path.EndsWith("Themes/Daylight.xaml", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("Themes/HighContrast.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Apply(SystemParameters.HighContrast ? AppTheme.HighContrast : AppTheme.Daylight);
        }
    }
}
