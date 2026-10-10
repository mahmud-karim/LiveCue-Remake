using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveCue.Desktop.SelfTest;

/// <summary>A self-test expectation that did not hold.</summary>
internal sealed class SelfTestFailedException(string message) : Exception(message);

/// <summary>Checks the self-test makes on the live visual tree and on rendered pixels.</summary>
internal static class VisualChecks
{
    public static void Require(bool condition, string expectation)
    {
        if (!condition)
        {
            throw new SelfTestFailedException("Expected: " + expectation);
        }
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// The crisp-text rule (design system 3.5 and checklist item 6): no text has an ancestor with an
    /// Effect, an OpacityMask, a BitmapCache or a resting Opacity below 1, because each of those
    /// renders the subtree to an intermediate surface and drops ClearType. A disabled control is the
    /// one allowed exception (opacity.disabled on its root). Returns how many text blocks were checked.
    /// </summary>
    public static int RequireCrispText(FrameworkElement root)
    {
        var checkedCount = 0;
        foreach (var text in Descendants(root).OfType<TextBlock>().Where(text => text.IsVisible))
        {
            checkedCount++;
            for (DependencyObject? current = text; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is UIElement element)
                {
                    var where = $"\"{text.Text}\" (ancestor {element.GetType().Name})";
                    Require(element.Effect is null, $"no Effect above the text {where}");
                    Require(element.OpacityMask is null, $"no OpacityMask above the text {where}");
                    Require(element.CacheMode is null, $"no BitmapCache above the text {where}");
                    Require(element.Opacity >= 1 || !element.IsEnabled, $"no resting Opacity below 1 above the text {where}");
                }

                if (ReferenceEquals(current, root))
                {
                    break;
                }
            }
        }

        return checkedCount;
    }

    /// <summary>
    /// Shadows live on caster layers only: an element with an Effect has no children. Returns how many
    /// casters were found.
    /// </summary>
    public static int RequireEffectsOnlyOnCasters(FrameworkElement root)
    {
        var casters = 0;
        foreach (var element in Descendants(root).OfType<UIElement>().Where(element => element.Effect is not null))
        {
            casters++;
            Require(
                VisualTreeHelper.GetChildrenCount(element) == 0,
                $"the {element.GetType().Name} that carries an Effect to have no children");
        }

        return casters;
    }

    public static int CountEffects(FrameworkElement root) =>
        Descendants(root).OfType<UIElement>().Count(element => element.IsVisible && element.Effect is not null);

    /// <summary>The colour of one pixel of a rendered frame (premultiplied BGRA, opaque here).</summary>
    public static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    public static bool IsWithin(Color actual, Color expected, int tolerance) =>
        Math.Abs(actual.R - expected.R) <= tolerance
        && Math.Abs(actual.G - expected.G) <= tolerance
        && Math.Abs(actual.B - expected.B) <= tolerance;

    /// <summary>WCAG relative luminance of an opaque sRGB colour.</summary>
    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
