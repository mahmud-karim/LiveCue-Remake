using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Desktop.Theming;

namespace LiveCue.Desktop.Tests.Theme;

/// <summary>
/// The two theme dictionaries, read as data: both define the same tokens, and the Daylight values
/// keep the contrast figures the design system computed for them (section 2.9, table A).
/// </summary>
public sealed class ThemeTests
{
    private static readonly Color DarkestGround = Color.FromRgb(0x9D, 0xC1, 0xF5);
    private static readonly Color Shade = Color.FromRgb(0x28, 0x3C, 0x6E);

    [Fact]
    public void Daylight_and_high_contrast_define_exactly_the_same_keys()
    {
        StaThread.Run(() =>
        {
            var daylight = Keys(Load("Daylight"));
            var highContrast = Keys(Load("HighContrast"));

            Assert.Empty(daylight.Except(highContrast));
            Assert.Empty(highContrast.Except(daylight));
            Assert.Contains("SurfacePanelBrush", daylight);
            Assert.Contains("InkSecondaryBrush", daylight);
            Assert.Contains("ShadowPanel", daylight);
        });
    }

    [Fact]
    public void Daylight_tokens_have_the_design_systems_values()
    {
        StaThread.Run(() =>
        {
            var daylight = Load("Daylight");

            Assert.Equal(Color.FromRgb(0xE6, 0xEC, 0xF5), Solid(daylight, "GroundBaseBrush"));
            Assert.Equal(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF), Solid(daylight, "SurfacePanelBrush"));
            Assert.Equal(Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF), Solid(daylight, "SurfaceInsetBrush"));
            Assert.Equal(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF), Solid(daylight, "SurfaceRaisedBrush"));
            Assert.Equal(Color.FromRgb(0x0E, 0x1B, 0x33), Solid(daylight, "InkPrimaryBrush"));
            Assert.Equal(Color.FromRgb(0x3D, 0x4A, 0x63), Solid(daylight, "InkSecondaryBrush"));
            Assert.Equal(Color.FromRgb(0x0B, 0x57, 0xD0), Solid(daylight, "AccentFillBrush"));
            Assert.Equal(Solid(daylight, "AccentFillBrush"), Solid(daylight, "AccentTextBrush"));
            Assert.Equal(DarkestGround, (Color)daylight["GroundBloom1Color"]);
            Assert.Equal(Shade, (Color)daylight["ShadeColor"]);
        });
    }

    [Fact]
    public void Every_daylight_brush_and_shadow_is_frozen_so_one_instance_serves_the_whole_app()
    {
        StaThread.Run(() =>
        {
            var daylight = Load("Daylight");

            foreach (var key in daylight.Keys)
            {
                if (daylight[key] is Freezable freezable)
                {
                    Assert.True(freezable.IsFrozen, $"{key} is not frozen.");
                }
            }
        });
    }

    [Fact]
    public void Shadows_are_written_so_that_wpf_draws_them_in_the_shade_colour()
    {
        StaThread.Run(() =>
        {
            var daylight = Load("Daylight");
            var panel = Assert.IsType<DropShadowEffect>(daylight["ShadowPanel"]);

            // WPF reads the effect colour as linear light, so the stored colour is the sRGB-encoded shade.
            Assert.Equal(ShadowColorExtension.Compensate(Shade), panel.Color);
            Assert.Equal(Color.FromRgb(0x6E, 0x85, 0xAF), panel.Color);
            Assert.Equal(270, panel.Direction);
            Assert.Equal(12, panel.ShadowDepth);
            Assert.Equal(32, panel.BlurRadius);
            Assert.Equal(0.23, panel.Opacity, 3);
        });
    }

    [Theory]
    [InlineData("InkPrimaryBrush", 12.91)]
    [InlineData("InkSecondaryBrush", 6.70)]
    [InlineData("AccentTextBrush", 4.80)]
    [InlineData("StatusSuccessBrush", 5.41)]
    [InlineData("StatusWarningBrush", 5.17)]
    [InlineData("StatusDangerBrush", 4.87)]
    public void Text_on_a_panel_over_the_darkest_ground_has_the_computed_contrast(string inkKey, double expected)
    {
        StaThread.Run(() =>
        {
            var daylight = Load("Daylight");

            // Table A, "surface.panel": the darkest ground, the panel's own shadow (alpha 0.14) showing
            // through, then the panel fill.
            var underPanel = Over(Shade, 0.14, DarkestGround);
            var panel = Over(Solid(daylight, "SurfacePanelBrush"), underPanel);
            var ratio = Contrast(Solid(daylight, inkKey), panel);

            Assert.True(ratio >= 4.5, $"{inkKey} on a panel is {ratio:0.00}:1, below 4.5:1.");
            Assert.InRange(ratio, expected - 0.06, expected + 0.06);
        });
    }

    [Fact]
    public void White_on_the_accent_fill_passes_and_secondary_ink_passes_on_the_bare_ground()
    {
        StaThread.Run(() =>
        {
            var daylight = Load("Daylight");

            Assert.InRange(Contrast(Solid(daylight, "InkOnAccentBrush"), Solid(daylight, "AccentFillBrush")), 6.33, 6.45);
            Assert.InRange(Contrast(Solid(daylight, "InkSecondaryBrush"), DarkestGround), 4.77, 4.89);
        });
    }

    [Fact]
    public void The_high_contrast_dictionary_has_no_shadows_blooms_or_sheen()
    {
        StaThread.Run(() =>
        {
            var highContrast = Load("HighContrast");

            Assert.Null(highContrast["ShadowPanel"]);
            Assert.Null(highContrast["ShadowRaised"]);
            Assert.Equal(0, ((Color)highContrast["GroundBloom1Color"]).A);
            Assert.Equal(0, Solid(highContrast, "GlassSheenBrush").A);
            Assert.Equal(0, Solid(highContrast, "AccentTintBrush").A);
            Assert.Equal(SystemColors.WindowTextColor, Solid(highContrast, "InkPrimaryBrush"));
            Assert.Equal(SystemColors.WindowColor, Solid(highContrast, "SurfacePanelBrush"));
            Assert.Equal(SystemColors.HighlightColor, Solid(highContrast, "AccentFillBrush"));
            Assert.Equal(Colors.White, Solid(highContrast, "SurfaceQrBrush"));
        });
    }

    /// <summary>Loads a theme dictionary straight from the app's assembly; no Application object is needed.</summary>
    private static ResourceDictionary Load(string name) =>
        (ResourceDictionary)Application.LoadComponent(
            new Uri($"/LiveCueRemake;component/Themes/{name}.xaml", UriKind.Relative));

    private static HashSet<string> Keys(ResourceDictionary dictionary) =>
        dictionary.Keys.Cast<object>().Select(key => key.ToString()!).ToHashSet(StringComparer.Ordinal);

    private static Color Solid(ResourceDictionary dictionary, string key) =>
        Assert.IsType<SolidColorBrush>(dictionary[key]).Color;

    /// <summary>Source-over in gamma-encoded sRGB, as WPF composites.</summary>
    private static Color Over(Color top, Color bottom) => Over(top, top.A / 255.0, bottom);

    private static Color Over(Color top, double alpha, Color bottom)
    {
        byte Mix(byte over, byte under) => (byte)Math.Round((over * alpha) + (under * (1 - alpha)));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static double Contrast(Color first, Color second)
    {
        var lighter = Math.Max(VisualChecks.Luminance(first), VisualChecks.Luminance(second));
        var darker = Math.Min(VisualChecks.Luminance(first), VisualChecks.Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }
}
