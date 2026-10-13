using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveCue.Desktop.Controls;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// A self-test expectation that did not hold. <see cref="Assertion"/> is the name of the assertion it
/// belongs to (W-design 5.13), once the runner knows it; the message says what was expected.
/// </summary>
internal sealed class SelfTestFailedException(string message, string? assertion = null) : Exception(message)
{
    public string? Assertion { get; } = assertion;
}

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
            if (CrispTextProblem(text, text.Text, root) is { } problem)
            {
                throw new SelfTestFailedException("Expected: " + problem);
            }
        }

        return checkedCount;
    }

    /// <summary>
    /// The same rule, for every visible text of the tree, a text box's own text included, as a list of
    /// what breaks it instead of a failure: the self-test collects these over all its steps and
    /// reports them at the end. <paramref name="texts"/> receives the number of texts looked at.
    /// </summary>
    public static List<string> CrispTextProblems(FrameworkElement root, out int texts)
    {
        var problems = new List<string>();
        texts = 0;
        foreach (var element in Descendants(root).OfType<FrameworkElement>().Where(element => element.IsVisible))
        {
            var shown = element switch
            {
                TextBlock block => block.Text,
                TextBoxBase box => (box as TextBox)?.Text ?? string.Empty,
                _ => null,
            };
            if (shown is null)
            {
                continue;
            }

            texts++;
            if (CrispTextProblem(element, shown, root) is { } problem)
            {
                problems.Add(problem);
            }
        }

        return problems;
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

    /// <summary>
    /// Everything the tree would show or say to a person or to assistive technology: the text of text
    /// blocks, text boxes and status indicators, string contents, tooltips, and the automation name,
    /// help text and item status of every element, visible or not.
    /// </summary>
    public static IEnumerable<string> SpokenAndShownTexts(FrameworkElement root)
    {
        foreach (var node in Descendants(root).Prepend(root))
        {
            switch (node)
            {
                case TextBlock block:
                    yield return block.Text;
                    foreach (var run in block.Inlines.OfType<Run>())
                    {
                        yield return run.Text;
                    }

                    break;
                case TextBox box:
                    yield return box.Text;
                    break;
                case StatusIndicatorView indicator:
                    yield return indicator.Text;
                    break;
                case ContentControl { Content: string content }:
                    yield return content;
                    break;
                default:
                    break;
            }

            if (node is FrameworkElement element)
            {
                yield return element.ToolTip switch
                {
                    string text => text,
                    ToolTip { Content: string text } => text,
                    _ => string.Empty,
                };
            }

            yield return AutomationProperties.GetName(node) ?? string.Empty;
            yield return AutomationProperties.GetHelpText(node) ?? string.Empty;
            yield return AutomationProperties.GetItemStatus(node) ?? string.Empty;
        }
    }

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

    /// <summary>What breaks the crisp-text rule above <paramref name="text"/>, or null.</summary>
    private static string? CrispTextProblem(DependencyObject text, string shown, FrameworkElement root)
    {
        for (var current = text; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement element)
            {
                var where = $"\"{Shorten(shown)}\" (ancestor {element.GetType().Name})";
                if (element.Effect is not null)
                {
                    return $"no Effect above the text {where}";
                }

                if (element.OpacityMask is not null)
                {
                    return $"no OpacityMask above the text {where}";
                }

                if (element.CacheMode is not null)
                {
                    return $"no BitmapCache above the text {where}";
                }

                if (element.Opacity < 1 && element.IsEnabled)
                {
                    return $"no resting Opacity below 1 above the text {where}";
                }
            }

            if (ReferenceEquals(current, root))
            {
                break;
            }
        }

        return null;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "…";
}
