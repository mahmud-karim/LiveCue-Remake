using LiveCue.Desktop.Composition;
using LiveCue.Desktop.SelfTest;

namespace LiveCue.Desktop.Tests.SelfTest;

/// <summary>
/// The command line of LiveCueRemake.exe (architecture 3.13). A switch without a usable value is a usage
/// error: the app prints the message and exits with code 2 before any window exists.
/// </summary>
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

    /// <summary>Every form of the command line: what is accepted, and what is a usage error (exit code 2).</summary>
    [Theory]
    [InlineData(true, "--self-test")]
    [InlineData(true, "--self-test", "--scale", "1")]
    [InlineData(true, "--self-test", "--scale", "1.5")]
    [InlineData(true, "--self-test", "--screenshots", "shots")]
    [InlineData(true, "--self-test", "--screenshots", "shots", "--scale", "1.5")]
    [InlineData(true, "--self-test", "--self-test-fail", "initial-layout")]
    [InlineData(false, "--screenshots")]
    [InlineData(false, "--self-test", "--screenshots")]
    [InlineData(false, "--screenshots", "--self-test")]
    [InlineData(false, "--self-test", "--scale")]
    [InlineData(false, "--self-test", "--scale", "2")]
    [InlineData(false, "--self-test", "--scale", "1,5")]
    [InlineData(false, "--self-test", "--scale", "big")]
    [InlineData(false, "--self-test", "--scale", "--screenshots", "shots")]
    public void Parse_cases(bool accepted, params string[] args)
    {
        if (!accepted)
        {
            var error = Assert.Throws<ArgumentException>(() => AppEnvironment.Parse(args));
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            return;
        }

        var environment = AppEnvironment.Parse(args);

        Assert.True(environment.IsSelfTest);
        Assert.Equal(args.Contains("1.5") ? 1.5 : 1, environment.Scale);
        Assert.Equal(args.Contains("shots") ? Path.GetFullPath("shots") : null, environment.ScreenshotDirectory);
    }

    [Fact]
    public void A_usage_error_is_exit_code_2_and_differs_from_a_failed_run()
    {
        Assert.Equal(0, SelfTestRunner.ExitPassed);
        Assert.Equal(1, SelfTestRunner.ExitFailed);
        Assert.Equal(2, SelfTestRunner.ExitUsage);
        Assert.Equal(3, SelfTestRunner.ExitTimedOut);
    }

    [Fact]
    public void The_switch_that_makes_an_assertion_fail_is_read_by_the_self_test_alone()
    {
        Assert.Equal("qr-drawn", SelfTestRunner.ForcedFailure(["app.dll", "--self-test", SelfTestRunner.FailSwitch, "qr-drawn"]));
        Assert.Null(SelfTestRunner.ForcedFailure(["app.dll", "--self-test"]));
        Assert.Null(SelfTestRunner.ForcedFailure(["app.dll", "--self-test", SelfTestRunner.FailSwitch]));
    }

    [Fact]
    public void The_version_is_the_one_the_relay_reports()
    {
        Assert.Equal("1.0.0", AppEnvironment.Version);
        Assert.EndsWith("THIRD-PARTY-NOTICES.md", AppEnvironment.ThirdPartyNoticesPath, StringComparison.Ordinal);
    }
}
