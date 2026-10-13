using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Desktop.Theming;

namespace LiveCue.Desktop.Tests.Theme;

/// <summary>
/// What Windows asks of the look (W-design 5.10; design system 6.1, 6.3 and 6.5): the contrast theme in
/// the theme slot, the solid twins while "Transparency effects" is off, and the text size. Each test
/// has resource dictionaries of its own, merged as App.xaml merges them, so the application's are left
/// alone.
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class ThemeManagerTests
{
    private static readonly string[] Shadows = ["ShadowPanel", "ShadowRaised", "ShadowOverlay", "ShadowAccent", "ShadowKnob"];
    private static readonly string[] Blooms = ["GroundBloom1Color", "GroundBloom2Color", "GroundBloom3Color", "GroundBloom4Color"];
    private static readonly string[] Tints = ["GlassSheenBrush", "AccentTintBrush", "StatusSuccessTintBrush", "StatusWarningTintBrush", "StatusDangerTintBrush"];
    private static readonly double[] Factors = [1.25, 1.5, 2.25];

    [Fact]
    public void A_contrast_theme_takes_the_theme_slot_and_daylight_returns() => WpfApp.Run(() =>
    {
        var resources = OwnResources();
        var themes = new ThemeManager(resources);
        Assert.Equal(AppTheme.Daylight, themes.Current);
        Assert.Equal(Color.FromRgb(0x0E, 0x1B, 0x33), Solid(resources, "InkPrimaryBrush"));

        themes.Apply(AppTheme.HighContrast);

        // Every surface Window, every ink WindowText, the accent Highlight; no shadow, bloom, sheen or tint.
        Assert.Equal(AppTheme.HighContrast, themes.Current);
        Assert.EndsWith("Themes/HighContrast.xaml", resources.MergedDictionaries[0].Source.OriginalString, StringComparison.Ordinal);
        Assert.Equal(2, resources.MergedDictionaries.Count);
        Assert.Equal(SystemColors.WindowTextColor, Solid(resources, "InkPrimaryBrush"));
        Assert.Equal(SystemColors.WindowTextColor, Solid(resources, "InkSecondaryBrush"));
        Assert.Equal(SystemColors.WindowColor, Solid(resources, "GroundBaseBrush"));
        Assert.Equal(SystemColors.WindowColor, Solid(resources, "SurfacePanelBrush"));
        Assert.Equal(SystemColors.WindowColor, Solid(resources, "SurfaceRaisedBrush"));
        Assert.Equal(SystemColors.HighlightColor, Solid(resources, "AccentFillBrush"));
        Assert.Equal(SystemColors.WindowTextColor, Solid(resources, "StrokeEdgeBrush"));
        Assert.Equal(SystemColors.WindowTextColor, Solid(resources, "StrokeControlBrush"));
        Assert.Equal(SystemColors.WindowTextColor, Solid(resources, "BrandMarkBubbleBrush"));
        foreach (var shadow in Shadows)
        {
            Assert.Null(resources[shadow]);
        }

        foreach (var bloom in Blooms)
        {
            Assert.Equal(0, ((Color)resources[bloom]).A);
        }

        foreach (var tint in Tints)
        {
            Assert.Equal(0, Solid(resources, tint).A);
        }

        // The QR stays black on white whatever the contrast theme's colours are.
        Assert.Equal(Colors.White, Solid(resources, "SurfaceQrBrush"));
        Assert.Equal(Colors.Black, Solid(resources, "QrModuleBrush"));

        // Applying the same theme again changes nothing; Daylight comes back as it was.
        var slot = resources.MergedDictionaries[0];
        themes.Apply(AppTheme.HighContrast);
        Assert.Same(slot, resources.MergedDictionaries[0]);
        themes.Apply(AppTheme.Daylight);
        Assert.Equal(AppTheme.Daylight, themes.Current);
        Assert.EndsWith("Themes/Daylight.xaml", resources.MergedDictionaries[0].Source.OriginalString, StringComparison.Ordinal);
        Assert.Equal(Color.FromRgb(0x0E, 0x1B, 0x33), Solid(resources, "InkPrimaryBrush"));
        Assert.IsType<DropShadowEffect>(resources["ShadowPanel"]);
    });

    [Fact]
    public void Transparency_off_puts_the_solid_twins_over_daylight_and_takes_the_sheen_away() => WpfApp.Run(() =>
    {
        var resources = OwnResources();
        var themes = new ThemeManager(resources);
        Assert.False(themes.ReducesTransparency);
        Assert.Equal(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF), Solid(resources, "SurfacePanelBrush"));

        themes.ReduceTransparency(true);

        // Design system 6.3: the solid twins, no sheen, and the canonical shadows of 2.13.
        Assert.True(themes.ReducesTransparency);
        Assert.Equal(Color.FromRgb(0xF6, 0xF8, 0xFB), Solid(resources, "SurfacePanelBrush"));
        Assert.Equal(Color.FromRgb(0xFA, 0xFB, 0xFD), Solid(resources, "SurfaceInsetBrush"));
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), Solid(resources, "SurfaceRaisedBrush"));
        Assert.Equal(Color.FromRgb(0xF8, 0xFB, 0xFF), Solid(resources, "SurfaceOverlayBrush"));
        Assert.Equal(Color.FromRgb(0x0E, 0x1B, 0x33), Solid(resources, "SurfaceInverseBrush"));
        Assert.Equal(0, Solid(resources, "GlassSheenBrush").A);
        var panelShadow = Assert.IsType<DropShadowEffect>(resources["ShadowPanel"]);
        Assert.Equal((12d, 32d, 0.14), (panelShadow.ShadowDepth, panelShadow.BlurRadius, Math.Round(panelShadow.Opacity, 3)));
        Assert.Equal(ShadowColorExtension.Compensate(Color.FromRgb(0x28, 0x3C, 0x6E)), panelShadow.Color);
        Assert.True(panelShadow.IsFrozen);

        // The ground, the strokes and the inks stay as they are.
        Assert.Equal(Color.FromRgb(0xE6, 0xEC, 0xF5), Solid(resources, "GroundBaseBrush"));
        Assert.Equal(Color.FromRgb(0x0E, 0x1B, 0x33), Solid(resources, "InkPrimaryBrush"));
        Assert.NotEqual(0, ((Color)resources["GroundBloom1Color"]).A);

        // The twins sit directly above the theme slot and are there once, however often it is asked for.
        themes.ReduceTransparency(true);
        Assert.Equal(3, resources.MergedDictionaries.Count);
        Assert.Null(resources.MergedDictionaries[1].Source);

        // A contrast theme is opaque already: the twins go, and return with Daylight.
        themes.Apply(AppTheme.HighContrast);
        Assert.Equal(2, resources.MergedDictionaries.Count);
        Assert.Equal(SystemColors.WindowColor, Solid(resources, "SurfacePanelBrush"));
        themes.Apply(AppTheme.Daylight);
        Assert.Equal(Color.FromRgb(0xF6, 0xF8, 0xFB), Solid(resources, "SurfacePanelBrush"));

        themes.ReduceTransparency(false);
        Assert.Equal(2, resources.MergedDictionaries.Count);
        Assert.Equal(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF), Solid(resources, "SurfacePanelBrush"));
        Assert.IsType<LinearGradientBrush>(resources["GlassSheenBrush"]);
    });

    [Fact]
    public void The_windows_text_size_scales_every_type_token_and_100_percent_restores_them() => WpfApp.Run(() =>
    {
        var resources = OwnResources();
        var themes = new ThemeManager(resources);
        Assert.Equal(10, TextScale.Tokens.Count);
        var baseValues = TextScale.Tokens.ToDictionary(
            token => token,
            token => ((double)resources[TextScale.SizeKey(token)], (double)resources[TextScale.LineHeightKey(token)]));
        Assert.Equal((14d, 20d), baseValues["Body"]);
        Assert.Equal((30d, 36d), baseValues["Display"]);

        foreach (var factor in Factors)
        {
            themes.ScaleText(factor);
            Assert.Equal(factor, themes.TextScaleFactor);
            foreach (var token in TextScale.Tokens)
            {
                var (size, lineHeight) = baseValues[token];
                var scaledSize = (double)resources[TextScale.SizeKey(token)];
                var scaledLineHeight = (double)resources[TextScale.LineHeightKey(token)];

                // Half pixels for sizes and whole pixels for line heights keep lines on the pixel grid.
                Assert.InRange(scaledSize, (size * factor) - 0.25, (size * factor) + 0.25);
                Assert.Equal(scaledSize * 2, Math.Round(scaledSize * 2));
                Assert.InRange(scaledLineHeight, (lineHeight * factor) - 0.5, (lineHeight * factor) + 0.5);
                Assert.Equal(scaledLineHeight, Math.Round(scaledLineHeight));
                Assert.True(scaledLineHeight > scaledSize);
            }
        }

        themes.ScaleText(1.5);
        Assert.Equal((21d, 30d), ((double)resources["TypeBodySize"], (double)resources["TypeBodyLineHeight"]));

        // Outside 100% to 225% the nearest end is used; a value that is no number is 100%.
        themes.ScaleText(4);
        Assert.Equal(2.25, themes.TextScaleFactor);
        themes.ScaleText(double.NaN);
        Assert.Equal(1, themes.TextScaleFactor);
        themes.ScaleText(0.5);
        Assert.Equal(1, themes.TextScaleFactor);

        // At 100% the values of Typography.xaml are back and the manager's own copies are gone.
        foreach (var token in TextScale.Tokens)
        {
            Assert.Equal(
                baseValues[token],
                ((double)resources[TextScale.SizeKey(token)], (double)resources[TextScale.LineHeightKey(token)]));
            Assert.DoesNotContain(TextScale.SizeKey(token), resources.Keys.Cast<object>());
        }
    });

    [Fact]
    public void The_look_follows_this_pcs_settings_without_failing() => WpfApp.Run(() =>
    {
        // Whatever this PC is set to: the values are read, never written, and stay in their ranges.
        Assert.InRange(SystemPreferences.TextScaleFactor, TextScale.Minimum, TextScale.Maximum);
        _ = SystemPreferences.TransparencyEnabled;

        // Animations can be pinned (the self-test and these tests pin them off) and released again.
        SystemPreferences.PinAnimations(true);
        Assert.True(SystemPreferences.AnimationsEnabled);
        SystemPreferences.PinAnimations(false);
        Assert.False(SystemPreferences.AnimationsEnabled);
    });

    /// <summary>The theme slot first, then the type tokens: the order of App.xaml.</summary>
    private static ResourceDictionary OwnResources()
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(Load("Daylight"));
        resources.MergedDictionaries.Add(Load("Typography"));
        return resources;
    }

    private static ResourceDictionary Load(string name) =>
        new() { Source = new Uri($"/LiveCueRemake;component/Themes/{name}.xaml", UriKind.Relative) };

    private static Color Solid(ResourceDictionary resources, string key) =>
        Assert.IsType<SolidColorBrush>(resources[key]).Color;
}
