using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace LiveCue.Desktop.Theming;

/// <summary>
/// The Windows settings the look follows besides the contrast theme (W-design 5.10; design system 6.1,
/// 6.3 and 6.7): "Animation effects", "Transparency effects" and the text size. Reading only; nothing
/// here is ever written.
/// </summary>
internal static class SystemPreferences
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccessibilityKey = @"Software\Microsoft\Accessibility";

    private static bool? _animationsPinned;

    /// <summary>
    /// False when Windows has animations turned off, or when the app pinned them off. Cross-fades,
    /// entrances and the segmented thumb's travel are then instant. Spinners keep turning: they are
    /// the only sign of work (design system 6.7).
    /// </summary>
    public static bool AnimationsEnabled => _animationsPinned ?? SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// Fixes <see cref="AnimationsEnabled"/> whatever Windows says; null follows Windows again. The
    /// self-test pins it off, so that what it renders never depends on the moment it looks.
    /// </summary>
    public static void PinAnimations(bool? enabled) => _animationsPinned = enabled;

    /// <summary>"Transparency effects" in Windows. On when the value cannot be read.</summary>
    public static bool TransparencyEnabled => ReadNumber(PersonalizeKey, "EnableTransparency") != 0;

    /// <summary>The Windows text size as a factor, 1 to 2.25. 1 when the value cannot be read.</summary>
    public static double TextScaleFactor =>
        TextScale.Clamp((ReadNumber(AccessibilityKey, "TextScaleFactor") ?? 100) / 100.0);

    private static int? ReadNumber(string key, string name)
    {
        try
        {
            using var opened = Registry.CurrentUser.OpenSubKey(key);
            return opened?.GetValue(name) as int?;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return null;
        }
    }
}
