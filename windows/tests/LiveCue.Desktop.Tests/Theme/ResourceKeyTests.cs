using System.Windows;
using LiveCue.Desktop.Tests.Support;

namespace LiveCue.Desktop.Tests.Theme;

/// <summary>
/// The resource keys of W-design 5.3: the contrast dictionary has exactly the keys of Daylight, and
/// every key the design lists exists, so a page can be written against the list alone.
/// </summary>
public sealed class ResourceKeyTests
{
    private static readonly string[] Dictionaries =
    [
        "Daylight", "Metrics", "Typography", "Icons", "Interaction", "Glass", "Buttons", "Navigation",
        "Status", "Rows", "Fields", "Dialogs", "Chrome", "Supporting",
    ];

    /// <summary>The colour and effect tokens of both theme dictionaries.</summary>
    private static readonly string[] ThemeKeys =
    [
        // DS 2.2
        "GroundBaseBrush", "GroundBloom1Color", "GroundBloom2Color", "GroundBloom3Color", "GroundBloom4Color",
        // DS 2.3
        "SurfacePanelBrush", "SurfaceInsetBrush", "SurfaceRaisedBrush", "SurfaceRaisedHoverBrush",
        "SurfaceRaisedPressedBrush", "SurfaceOverlayBrush", "SurfaceInverseBrush", "SurfaceScrimBrush",
        "SurfaceQrBrush", "StateHoverBrush", "StatePressedBrush", "StateHoverOnOverlayBrush",
        "StatePressedOnOverlayBrush", "StateCaptionHoverBrush", "StateCaptionCloseHoverBrush",
        // DS 2.4
        "StrokeEdgeBrush", "StrokeRaisedBrush", "StrokeInsetBrush", "StrokeOnAccentBrush", "StrokeHairlineBrush",
        "StrokeControlBrush", "StrokeDangerBrush", "StrokeFocusBrush", "GlassSheenBrush",
        // DS 2.5
        "InkPrimaryBrush", "InkSecondaryBrush", "InkDisabledBrush", "InkOnAccentBrush", "InkOnInverseBrush",
        // DS 2.6
        "AccentFillBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentStrongBrush", "AccentTintBrush", "AccentTextBrush",
        // DS 2.7
        "StatusLiveBrush", "StatusLiveTintBrush", "StatusSuccessBrush", "StatusSuccessTintBrush",
        "StatusWarningBrush", "StatusWarningTintBrush", "StatusDangerBrush", "StatusDangerTintBrush",
        "StatusOfflineBrush", "StatusOfflineTintBrush", "StatusDangerHoverBrush", "StatusDangerPressedBrush",
        // DS 2.8
        "TrackProgressBrush", "TrackSegmentedBrush", "TrackToggleOffBrush", "ControlKnobBrush",
        "ControlScrollThumbBrush", "ControlScrollThumbHoverBrush", "ShadeColor",
        // DS 4.15, 8.2, 2.15, 2.13
        "QrModuleBrush", "BrandMarkBubbleBrush", "BrandMarkBarsBrush", "OpacityDisabled",
        "ShadowPanel", "ShadowRaised", "ShadowOverlay", "ShadowAccent", "ShadowKnob",
    ];

    /// <summary>The tokens that do not change with the theme, and the component style keys.</summary>
    private static readonly string[] OtherKeys =
    [
        // Metrics.xaml
        "RadiusPanel", "RadiusPanelCompact", "RadiusCard", "RadiusField", "RadiusTile", "RadiusSmall",
        "InsetScreen", "InsetPanel", "InsetPanelCompact", "InsetCard", "GapPanel", "GapGroup", "GapInline", "GapControl",
        "SizeSidebar", "SizeTitleBar", "SizeQrPanel", "SizeReadingMax",
        "MotionPress", "MotionFast", "MotionBase", "MotionEmphasis", "EaseStandard", "EaseOut", "EaseIn", "EaseInOut",
        // Typography.xaml
        "FontFamilyDisplay", "FontFamilyText", "FontFamilyMono",
        "TypeDisplaySize", "TypeDisplayLineHeight", "TypeTitleSize", "TypeTitleLineHeight",
        "TypeHeadlineSize", "TypeHeadlineLineHeight", "TypeCueSize", "TypeCueLineHeight",
        "TypeTranscriptSize", "TypeTranscriptLineHeight", "TypeBodySize", "TypeBodyLineHeight",
        "TypeLabelSize", "TypeLabelLineHeight", "TypeCaptionSize", "TypeCaptionLineHeight",
        "TypeMicroSize", "TypeMicroLineHeight", "TypeMonoSize", "TypeMonoLineHeight",
        "Type.Display", "Type.Title", "Type.Headline", "Type.Cue", "Type.Transcript", "Type.Body",
        "Type.Label", "Type.Caption", "Type.Micro", "Type.Mono",
        // Component styles
        "Glass.Panel", "Glass.PanelCompact", "Glass.Card", "Glass.CardProminent",
        "Button.Primary", "Button.Primary.Session", "Button.Secondary", "Button.Secondary.Session",
        "Button.Quiet", "Button.Destructive", "Button.DestructivePrimary", "Button.Icon", "Button.Icon.Quiet",
        "Segmented", "Field.Text", "Field.ReadOnly", "Nav.List", "Nav.Item", "Row.List", "Row.Check",
        "Dialog.Scrim", "Dialog.Card", "Toast", "Banner", "Chip", "Indicator", "Spinner",
        "Log.Text", "Page.Scroller", "Divider",
        "Chrome.CaptionCapsule", "Chrome.CaptionButton", "Chrome.CaptionButton.Close", "Chrome.Wordmark",
        "Focus.Pill", "Focus.Row", "EmptyState",
    ];

    [Fact]
    public void Key_sets_match()
    {
        StaThread.Run(() =>
        {
            var daylight = Keys(Load("Daylight"));
            var highContrast = Keys(Load("HighContrast"));

            Assert.Empty(daylight.Except(highContrast));
            Assert.Empty(highContrast.Except(daylight));
            Assert.Empty(ThemeKeys.Except(daylight));
        });
    }

    [Fact]
    public void Every_listed_key_exists()
    {
        StaThread.Run(() =>
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in Dictionaries)
            {
                keys.UnionWith(Keys(Load(name)));
            }

            Assert.Empty(ThemeKeys.Concat(OtherKeys).Except(keys));
            Assert.Contains(keys, key => key.StartsWith("Icon.", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Every_dictionary_of_the_app_is_one_this_test_reads()
    {
        var assembly = typeof(App).Assembly;
        using var resources = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources")!;
        using var reader = new System.Resources.ResourceReader(resources);

        var themes = reader.Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Where(key => key.StartsWith("themes/", StringComparison.Ordinal))
            .Select(key => Path.GetFileNameWithoutExtension(key))
            .ToList();

        Assert.Equal(
            Dictionaries.Append("HighContrast").Order(StringComparer.OrdinalIgnoreCase),
            themes.Order(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static ResourceDictionary Load(string name) =>
        (ResourceDictionary)Application.LoadComponent(
            new Uri($"/LiveCueRemake;component/Themes/{name}.xaml", UriKind.Relative));

    /// <summary>The string keys of a dictionary and of the dictionaries it merges.</summary>
    private static HashSet<string> Keys(ResourceDictionary dictionary)
    {
        var keys = dictionary.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var merged in dictionary.MergedDictionaries)
        {
            keys.UnionWith(Keys(merged));
        }

        return keys;
    }
}
