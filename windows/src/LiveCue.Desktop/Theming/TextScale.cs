using System.Windows;

namespace LiveCue.Desktop.Theming;

/// <summary>
/// Follows the Windows text size (design system 6.1; W-design 5.10) by replacing the size and line
/// height resources of the ten type tokens. Every text style reads them through DynamicResource, and
/// control heights are minimums, so the whole window grows with the text.
///
/// The base values stay in Typography.xaml; a scaled copy is put on top of them in the application's
/// own resources and taken away again at 100%.
/// </summary>
internal static class TextScale
{
    public const double Minimum = 1.0;
    public const double Maximum = 2.25;

    /// <summary>The ten type tokens of design system 2.10, as they appear in the resource keys.</summary>
    public static readonly IReadOnlyList<string> Tokens =
        ["Display", "Title", "Headline", "Cue", "Transcript", "Body", "Label", "Caption", "Micro", "Mono"];

    public static double Clamp(double factor) =>
        double.IsNaN(factor) ? Minimum : Math.Clamp(factor, Minimum, Maximum);

    public static string SizeKey(string token) => $"Type{token}Size";

    public static string LineHeightKey(string token) => $"Type{token}LineHeight";

    /// <summary>
    /// Rewrites the type resources of <paramref name="resources"/> for <paramref name="factor"/>.
    /// Sizes are rounded to half a pixel and line heights to whole pixels, which keeps lines on the
    /// pixel grid.
    /// </summary>
    public static void Apply(ResourceDictionary resources, double factor)
    {
        ArgumentNullException.ThrowIfNull(resources);
        factor = Clamp(factor);

        foreach (var token in Tokens)
        {
            Rewrite(resources, SizeKey(token), factor, step: 0.5);
            Rewrite(resources, LineHeightKey(token), factor, step: 1);
        }
    }

    private static void Rewrite(ResourceDictionary resources, string key, double factor, double step)
    {
        // Removing our own copy first makes the lookup below find the value Typography.xaml defines.
        resources.Remove(key);
        if (factor <= Minimum || resources[key] is not double baseValue)
        {
            return;
        }

        resources[key] = Math.Round(baseValue * factor / step, MidpointRounding.AwayFromZero) * step;
    }
}
