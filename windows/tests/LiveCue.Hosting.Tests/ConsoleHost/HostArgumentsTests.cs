using LiveCue.Relay;
using LiveCue.Relay.Host;

namespace LiveCue.Hosting.Tests.ConsoleHost;

/// <summary>The command line of LiveCueRelayHost.exe (architecture 3.14, test plan 2.4).</summary>
public sealed class HostArgumentsTests
{
    private const string Endpoint = "https://pc.example.test:11443/";

    [Fact]
    public void Parse_cases()
    {
        // Nothing given: every setting applies.
        Assert.True(HostArguments.TryParse([], out var none));
        Assert.Equal(new HostArguments(false, false, false, false, null, new RelayOverrides()), none);

        // Every argument and override at once. The endpoint is made canonical.
        Assert.True(HostArguments.TryParse(
            [
                "--check-only", "--show-pairing", "--events", "json", "--include-content",
                "--endpoint", "https://PC.example.test:11443", "--port", "0", "--data-dir", @"C:\data",
                "--codex", @"C:\x\codex.exe", "--lab", @"C:\asr", "--mode", "public",
            ],
            out var all));
        Assert.Equal(
            new HostArguments(
                CheckOnly: true,
                ShowPairing: true,
                JsonEvents: true,
                IncludeContent: true,
                DataDirectory: @"C:\data",
                new RelayOverrides(Endpoint, 0, @"C:\x\codex.exe", @"C:\asr", ConnectionMode.Public)),
            all);

        // Each one alone.
        Assert.True(Parse("--check-only").CheckOnly);
        Assert.True(Parse("--show-pairing").ShowPairing);
        Assert.True(Parse("--events", "json").JsonEvents);
        Assert.True(Parse("--include-content").IncludeContent);
        Assert.Equal("https://pc.example.test:10000/livecue/", Parse("--endpoint", "https://pc.example.test:10000/livecue").Overrides.Endpoint);
        Assert.Equal(50123, Parse("--port", "50123").Overrides.Port);
        Assert.Equal(1024, Parse("--port", "1024").Overrides.Port);
        Assert.Equal(65535, Parse("--port", "65535").Overrides.Port);
        Assert.Equal(ConnectionMode.Private, Parse("--mode", "private").Overrides.Mode);
        Assert.Equal(ConnectionMode.Public, Parse("--mode", "public").Overrides.Mode);
        Assert.Null(Parse("--check-only").Overrides.Mode);
        Assert.Null(Parse("--check-only").Overrides.Port);
        Assert.Null(Parse("--check-only").DataDirectory);

        // The three paths are made absolute against the current folder.
        var current = Environment.CurrentDirectory;
        Assert.Equal(Path.Combine(current, "data"), Parse("--data-dir", "data").DataDirectory);
        Assert.Equal(Path.Combine(current, "tools", "codex.exe"), Parse("--codex", @"tools\codex.exe").Overrides.CodexPath);
        Assert.Equal(Path.Combine(current, "asr"), Parse("--lab", "asr").Overrides.LabFolder);

        // A later value replaces an earlier one.
        Assert.Equal(50002, Parse("--port", "50001", "--port", "50002").Overrides.Port);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("check-only")]
    [InlineData("/?")]
    [InlineData("--help")]
    [InlineData("--port")]
    [InlineData("--port", "47831")]
    [InlineData("--port", "80")]
    [InlineData("--port", "1023")]
    [InlineData("--port", "65536")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "x")]
    [InlineData("--port", "50123 ")]
    [InlineData("--endpoint")]
    [InlineData("--endpoint", "http://pc.example.test/")]
    [InlineData("--endpoint", "pc.example.test")]
    [InlineData("--endpoint", "https://pc.example.test/\n")]
    [InlineData("--mode")]
    [InlineData("--mode", "tailnet")]
    [InlineData("--mode", "Public")]
    [InlineData("--events")]
    [InlineData("--events", "xml")]
    [InlineData("--data-dir")]
    [InlineData("--data-dir", "")]
    [InlineData("--data-dir", " ")]
    [InlineData("--codex")]
    [InlineData("--codex", "")]
    [InlineData("--lab")]
    [InlineData("--lab", "")]
    [InlineData("--check-only", "--events")]
    [InlineData("--token", "abc")]
    public async Task An_argument_that_is_not_accepted_is_exit_2_with_the_usage(params string[] args)
    {
        Assert.False(HostArguments.TryParse(args, out var parsed));
        Assert.Null(parsed);

        // The host prints the usage and composes nothing.
        using var output = new StringWriter();
        var exitCode = await HostProgram.RunAsync(args, Fail, new HostOutput(output, IsRedirected: false), CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(HostArguments.Usage + Environment.NewLine, output.ToString());
        Assert.StartsWith("Usage: LiveCueRelayHost", HostArguments.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_usage_names_every_argument()
    {
        foreach (var name in (string[])["--check-only", "--show-pairing", "--events json", "--include-content", "--endpoint", "--port", "--data-dir", "--codex", "--lab", "--mode private|public"])
        {
            Assert.Contains(name, HostArguments.Usage, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("--reset-pairing")]
    [InlineData("--reset")]
    [InlineData("--reset-stored-pairing")]
    [InlineData("--new-pairing")]
    [InlineData("--token", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Reset_is_not_an_argument(params string[] args)
    {
        Assert.False(HostArguments.TryParse(args, out _));

        // Nor does the usage offer one, or any way to hand the host a token.
        Assert.DoesNotContain("reset", HostArguments.Usage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", HostArguments.Usage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_previous_apps_data_folder_is_not_accepted_as_a_data_folder()
    {
        var previous = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveCue");

        Assert.False(HostArguments.TryParse(["--data-dir", previous], out _));
        Assert.False(HostArguments.TryParse(["--data-dir", Path.Combine(previous, "relay")], out _));
        Assert.True(HostArguments.TryParse(["--data-dir", previous + "Remake"], out var remake));
        Assert.Equal(DataFolder.DefaultPath, remake.DataDirectory);
    }

    private static HostArguments Parse(params string[] args)
    {
        Assert.True(HostArguments.TryParse(args, out var parsed));
        return parsed;
    }

    private static HostingServices Fail(HostArguments arguments) =>
        throw new InvalidOperationException("No services are composed for a usage error.");
}
