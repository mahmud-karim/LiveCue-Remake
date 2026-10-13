using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.SelfTest;

/// <summary>
/// The self-test as CI runs it (W-design 5.13): the app itself, as a child process,
/// <c>dotnet LiveCueRemake.dll --self-test …</c>, with its output and its exit code read. The child binds
/// no port, starts no process and touches no file but the screenshot folder a test gives it.
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class SelfTestRunTests
{
    private const int DefaultWidth = 1240;
    private const int DefaultHeight = 660;
    private const int MinimumWidth = 960;
    private const int MinimumHeight = 620;

    [Fact]
    public async Task The_self_test_passes()
    {
        var run = await RunAsync("--self-test");

        Assert.True(run.ExitCode == SelfTestRunner.ExitPassed, run.Describe());
        Assert.Equal(SelfTestRunner.PassedLine, run.Lines[^1]);
        Assert.DoesNotContain(run.Lines, line => line.StartsWith(SelfTestRunner.FailedPrefix, StringComparison.Ordinal));
        Assert.Equal(string.Empty, run.Error.Trim());

        // Every assertion of the table reported itself, once, in the order of the steps.
        string[] expected =
        [
            "initial-layout", "look-ground-and-glass", "look-reference-composites", "layout-beyond-desktop",
            "activity-empty", "pairing-placeholder", "ready-layout", "show-qr-enabled", "qr-drawn", "qr-quiet-zone",
            "qr-notice", "reset-dialog", "thinking", "reply-text", "timing-hint", "pause", "resume", "request-failed",
            "speech", "activity-lines", "settings-command", "settings-public", "settings-blocked", "fault", "clear",
            "activity-cap", "scenes", "no-token", "no-side-effects", "crisp-text",
        ];
        Assert.Equal(expected, run.Lines.Where(line => line.StartsWith("ok ", StringComparison.Ordinal)).Select(line => line[3..]));

        // Without a screenshot folder nothing is written, and the run says so.
        Assert.Contains("screenshots: 0 (none requested)", run.Lines);
        Assert.DoesNotContain(run.Lines, line => line.Contains(SyntheticData.Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Screenshots_are_written()
    {
        using var folder = new TempFolder();
        var shots = folder.Combine("shots");
        const double scale = 1.5;

        var run = await RunAsync("--self-test", "--screenshots", shots, "--scale", scale.ToString(CultureInfo.InvariantCulture));

        Assert.True(run.ExitCode == SelfTestRunner.ExitPassed, run.Describe());

        // Exactly the thirty pictures and the log: two per scene, nothing else.
        var written = Directory.EnumerateFiles(shots).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(15, Scenes.Names.Count);
        Assert.Equal(30, Scenes.FileNames.Count);
        Assert.Equal(Scenes.FileNames.Append(SelfTestLog.FileName).Order(StringComparer.Ordinal), written);
        Assert.Contains("live-stopped.png", written);
        Assert.Contains("live-stopped-min.png", written);
        Assert.Contains("fault-port-in-use-min.png", written);

        // Each picture has the pixel size of its window size times the scale.
        foreach (var scene in Scenes.Names)
        {
            Assert.Equal(((int)(DefaultWidth * scale), (int)(DefaultHeight * scale)), PngSize(Path.Combine(shots, scene + ".png")));
            Assert.Equal(((int)(MinimumWidth * scale), (int)(MinimumHeight * scale)), PngSize(Path.Combine(shots, scene + "-min.png")));
        }

        // The log holds every line that was printed.
        var log = await File.ReadAllLinesAsync(Path.Combine(shots, SelfTestLog.FileName));
        Assert.Equal(run.Lines, log);
        Assert.Contains("screenshots: 30", log);
    }

    [Fact]
    public async Task A_failure_exits_1_with_the_name()
    {
        // The hidden switch makes one assertion fail although what it checks holds.
        var run = await RunAsync("--self-test", SelfTestRunner.FailSwitch, "activity-empty");

        Assert.True(run.ExitCode == SelfTestRunner.ExitFailed, run.Describe());
        Assert.Contains(SelfTestRunner.FailedPrefix + "activity-empty", run.Lines);
        Assert.DoesNotContain(SelfTestRunner.PassedLine, run.Lines);

        // The assertions before it passed and were reported; none after it ran.
        Assert.Contains("ok initial-layout", run.Lines);
        Assert.DoesNotContain("ok activity-empty", run.Lines);
        Assert.DoesNotContain("ok pairing-placeholder", run.Lines);

        // The line after the result says what was expected.
        var result = run.Lines.ToList().IndexOf(SelfTestRunner.FailedPrefix + "activity-empty");
        Assert.StartsWith("Expected: ", run.Lines[result + 1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_usage_error_exits_2()
    {
        var run = await RunAsync("--self-test", "--scale", "2");

        Assert.True(run.ExitCode == SelfTestRunner.ExitUsage, run.Describe());
        Assert.Contains("--scale must be 1 or 1.5.", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(SelfTestRunner.PassedLine, run.Lines);
    }

    /// <summary>The width and height an image file declares in its PNG header.</summary>
    private static (int Width, int Height) PngSize(string path)
    {
        var header = new byte[24];
        using (var file = File.OpenRead(path))
        {
            file.ReadExactly(header);
        }

        Assert.Equal<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], header[..8]);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(header, 12, 4));
        return (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
    }

    /// <summary>Runs the app with the dotnet host that runs the tests, and waits for it to end.</summary>
    private static async Task<SelfTestRun> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(DotnetHost.Path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(DotnetHost.Beside("LiveCueRemake.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        // The app's own watchdog ends a run that hangs after two minutes, with exit code 3.
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The self-test did not end.");
        }

        var lines = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
        return new SelfTestRun(process.ExitCode, lines, await error);
    }

    private sealed record SelfTestRun(int ExitCode, string[] Lines, string Error)
    {
        public string Describe() =>
            $"Exit code {ExitCode}.{Environment.NewLine}{string.Join(Environment.NewLine, Lines)}{Environment.NewLine}{Error}";
    }
}
