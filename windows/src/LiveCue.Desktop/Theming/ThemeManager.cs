using System.ComponentModel;
using System.Windows;

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
/// Owns the theme slot in the application's merged dictionaries. Daylight is the only theme, whatever
/// the Windows app mode says; the single switch is to HighContrast.xaml while a Windows contrast
/// theme is active (design system sections 5 and 6.5). Styles read every colour through
/// DynamicResource, so the swap takes effect while the app is running.
/// </summary>
public sealed class ThemeManager
{
    private static readonly Uri DaylightSource = new("/LiveCueRemake;component/Themes/Daylight.xaml", UriKind.Relative);
    private static readonly Uri HighContrastSource = new("/LiveCueRemake;component/Themes/HighContrast.xaml", UriKind.Relative);

    private readonly ResourceDictionary _applicationResources;
    private bool _followsSystem;

    public ThemeManager(ResourceDictionary applicationResources)
    {
        ArgumentNullException.ThrowIfNull(applicationResources);
        _applicationResources = applicationResources;
    }

    public AppTheme Current { get; private set; } = AppTheme.Daylight;

    /// <summary>Applies the theme for the current system setting and keeps following it.</summary>
    public void FollowSystem()
    {
        if (!_followsSystem)
        {
            _followsSystem = true;
            SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        }

        Apply(SystemParameters.HighContrast ? AppTheme.HighContrast : AppTheme.Daylight);
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
