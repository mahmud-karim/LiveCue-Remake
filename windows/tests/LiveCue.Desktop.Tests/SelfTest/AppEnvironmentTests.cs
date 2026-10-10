using LiveCue.Desktop.Composition;

namespace LiveCue.Desktop.Tests.SelfTest;

/// <summary>The command line of LiveCueRemake.exe. Carried over from the spike; work package WP-W11 owns this folder.</summary>
public sealed class AppEnvironmentTests
{
    [Fact]
    public void No_arguments_means_the_normal_window()
    {
        var environment = AppEnvironment.Parse([]);

        Assert.False(environment.IsSelfTest);
        Assert.Null(environment.ScreenshotDirectory);
        Assert.Equal(1, environment.Scale);
    }

    [Fact]
    public void The_self_test_switches_are_read_and_unknown_arguments_are_ignored()
    {
        var folder = Path.Combine(Path.GetTempPath(), "LiveCue-shots");

        var environment = AppEnvironment.Parse(["--unknown", "--self-test", "--screenshots", folder, "--scale", "1.5"]);

        Assert.True(environment.IsSelfTest);
        Assert.Equal(Path.GetFullPath(folder), environment.ScreenshotDirectory);
        Assert.Equal(1.5, environment.Scale);
    }

    [Theory]
    [InlineData("--screenshots")]
    [InlineData("--screenshots", "--self-test")]
    [InlineData("--scale")]
    [InlineData("--scale", "2")]
    [InlineData("--scale", "big")]
    public void A_switch_without_a_usable_value_is_an_error(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => AppEnvironment.Parse(args));
    }

    [Fact]
    public void The_version_is_the_one_the_relay_reports()
    {
        Assert.Equal("1.0.0", AppEnvironment.Version);
        Assert.EndsWith("THIRD-PARTY-NOTICES.md", AppEnvironment.ThirdPartyNoticesPath, StringComparison.Ordinal);
    }
}
