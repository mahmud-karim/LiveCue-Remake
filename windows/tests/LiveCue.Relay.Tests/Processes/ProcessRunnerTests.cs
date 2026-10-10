using System.Text;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Processes;

/// <summary>
/// The production runner (W-design 4.13) against a real child process, <c>LiveCue.TestChild</c>: the
/// argument list, the three environment policies, standard input, the output cap, the time limit on the
/// runner's clock, cancellation, the job object and the refusals.
///
/// The class runs outside the parallel collections because one test changes environment variables of
/// the test process.
/// </summary>
[Collection(ProcessEnvironmentGroup.Name)]
public sealed class ProcessRunnerTests
{
    private const int Megabyte = 1_048_576;

    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>The thirteen names of protocol 11.5.</summary>
    internal static readonly string[] LabScriptNames =
    [
        "SystemRoot", "windir", "Path", "PathExt", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
        "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ComSpec",
    ];

    /// <summary>The names of protocol 12.6.</summary>
    internal static readonly string[] CodexNames =
    [
        .. LabScriptNames, "HOMEDRIVE", "HOMEPATH", "CODEX_HOME", "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY", "NO_PROXY",
    ];

    [Fact]
    public async Task Arguments_arrive_unchanged_including_quotes_and_spaces()
    {
        string[] arguments =
        [
            "plain",
            "two words",
            "model_reasoning_effort=\"low\"",
            "\"quoted\"",
            "a\"b",
            @"C:\folder with space\file.json",
            @"ends with a backslash\",
            @"ends with two\\",
            @"backslash before quote\""",
            string.Empty,
            " ",
            "-",
            "--disable",
            "%PATH%",
            "$env:PATH",
            "a&b|c<d>e^f",
            "naïve ☃ 你好",
            "tab\there",
        ];
        var runner = new ProcessRunner(TimeProvider.System);

        var result = await runner.RunAsync(
            TestChild.Request(["echo-args", .. arguments]) with { MaxStandardOutputBytes = Megabyte }, None);

        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        var printed = Encoding.UTF8.GetString(result.StandardOutput.Span).Split('\n');
        Assert.Equal([.. arguments, string.Empty], printed);
    }

    [Fact]
    public async Task The_three_environment_policies()
    {
        const string Ours = "LIVECUE_PROCESS_RUNNER_TEST";
        const string Proxy = "HTTPS_PROXY";
        var proxyBefore = Environment.GetEnvironmentVariable(Proxy);
        Environment.SetEnvironmentVariable(Ours, "1");
        Environment.SetEnvironmentVariable(Proxy, proxyBefore ?? "http://proxy.example.test:3128");
        try
        {
            var runner = new ProcessRunner(TimeProvider.System);
            var parent = Environment.GetEnvironmentVariables().Keys.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

            var inherit = await NamesAsync(runner, EnvironmentPolicy.Inherit);
            var lab = await NamesAsync(runner, EnvironmentPolicy.LabScriptAllowlist);
            var codex = await NamesAsync(runner, EnvironmentPolicy.CodexAllowlist);

            // Inherit: everything the parent has.
            Assert.Contains(Ours, inherit);
            Assert.Contains(Proxy, inherit);
            Assert.Superset(parent, inherit);

            // The lab scripts: only the thirteen, and each of them that the parent has.
            Assert.Subset(LabScriptNames.ToHashSet(StringComparer.OrdinalIgnoreCase), lab);
            Assert.Equal(
                LabScriptNames.Where(parent.Contains).Order(StringComparer.OrdinalIgnoreCase),
                lab.Order(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            Assert.Contains("SystemRoot", lab);
            Assert.Contains("Path", lab);
            Assert.DoesNotContain(Ours, lab);
            Assert.DoesNotContain(Proxy, lab);

            // Codex: only the names of protocol 12.6, and each of them that the parent has.
            Assert.Subset(CodexNames.ToHashSet(StringComparer.OrdinalIgnoreCase), codex);
            Assert.Equal(
                CodexNames.Where(parent.Contains).Order(StringComparer.OrdinalIgnoreCase),
                codex.Order(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Proxy, codex);
            Assert.DoesNotContain(Ours, codex);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Ours, null);
            Environment.SetEnvironmentVariable(Proxy, proxyBefore);
        }
    }

    [Fact]
    public async Task Standard_input_is_utf8_without_a_mark_and_closed()
    {
        const string Text = "first line\nnaïve ☃ 你好\r\nlast line without an end";
        var runner = new ProcessRunner(TimeProvider.System);

        // The child copies standard input to standard output and ends when the input ends.
        var copied = await runner.RunAsync(
            TestChild.Request("stdin") with { StandardInput = Text, MaxStandardOutputBytes = Megabyte }, None);
        var large = await runner.RunAsync(
            TestChild.Request("stdin") with { StandardInput = new string('p', 300_000), MaxStandardOutputBytes = Megabyte }, None);
        var nothing = await runner.RunAsync(
            TestChild.Request("stdin") with { StandardInput = null, MaxStandardOutputBytes = Megabyte }, None);

        Assert.Equal(ProcessOutcome.Exited, copied.Outcome);
        Assert.Equal(Encoding.UTF8.GetBytes(Text), copied.StandardOutput.ToArray());
        Assert.NotEqual((byte)0xEF, copied.StandardOutput.Span[0]);
        Assert.Equal(ProcessOutcome.Exited, large.Outcome);
        Assert.Equal(300_000, large.StandardOutput.Length);

        // Without input the pipe is closed at once: the child sees the end and ends by itself.
        Assert.Equal(ProcessOutcome.Exited, nothing.Outcome);
        Assert.Equal(0, nothing.ExitCode);
        Assert.True(nothing.StandardOutput.IsEmpty);
    }

    [Fact]
    public async Task Output_beyond_the_limit_ends_the_process()
    {
        var runner = new ProcessRunner(TimeProvider.System);

        var atTheLimit = await runner.RunAsync(TestChild.Request("write", "20000") with { MaxStandardOutputBytes = 20_000 }, None);
        var beyond = await runner.RunAsync(TestChild.Request("write", "20001") with { MaxStandardOutputBytes = 20_000 }, None);
        var farBeyond = await runner.RunAsync(TestChild.Request("write", "5000000") with { MaxStandardOutputBytes = 1_000 }, None);
        var discarded = await runner.RunAsync(TestChild.Request("write", "5000000") with { MaxStandardOutputBytes = 0 }, None);

        Assert.Equal(ProcessOutcome.Exited, atTheLimit.Outcome);
        Assert.Equal(20_000, atTheLimit.StandardOutput.Length);
        Assert.All(atTheLimit.StandardOutput.ToArray(), value => Assert.Equal((byte)'x', value));

        Assert.Equal(ProcessOutcome.OutputLimitExceeded, beyond.Outcome);
        Assert.True(beyond.StandardOutput.IsEmpty);
        Assert.Equal(ProcessOutcome.OutputLimitExceeded, farBeyond.Outcome);
        Assert.True(farBeyond.StandardOutput.IsEmpty);

        // A limit of zero discards the output; it is never an overflow.
        Assert.Equal(ProcessOutcome.Exited, discarded.Outcome);
        Assert.Equal(0, discarded.ExitCode);
        Assert.True(discarded.StandardOutput.IsEmpty);
    }

    [Fact]
    public async Task A_timeout_kills_the_whole_tree()
    {
        using var folder = new TempFolder();
        var clock = new ManualTimeProvider();
        var runner = new ProcessRunner(clock);
        var limit = TimeSpan.FromSeconds(42);

        var run = runner.RunAsync(
            TestChild.Request("spawn", folder.Combine("ids.txt"), "600000") with { Timeout = limit }, None);
        var (child, grandchild) = await TestChild.WaitForSpawnAsync(folder.Combine("ids.txt"));

        // The limit is a timer of the runner's clock: nothing happens until that clock reaches it.
        clock.Advance(limit - TimeSpan.FromMilliseconds(1));
        await Task.Delay(100);
        Assert.False(run.IsCompleted);
        Assert.False(TestChild.IsGone(child));
        Assert.False(TestChild.IsGone(grandchild));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var result = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
        Assert.True(result.StandardOutput.IsEmpty);
        Assert.True(TestChild.IsGone(child));
        await TestChild.WaitUntilGoneAsync(child, grandchild);
        Assert.Equal(0, clock.ScheduledTimers);
    }

    [Fact]
    public async Task Cancellation_kills_the_whole_tree()
    {
        using var folder = new TempFolder();
        var clock = new ManualTimeProvider();
        var runner = new ProcessRunner(clock);
        using var leaving = new CancellationTokenSource();

        var run = runner.RunAsync(TestChild.Request("spawn", folder.Combine("ids.txt"), "600000"), leaving.Token);
        var (child, grandchild) = await TestChild.WaitForSpawnAsync(folder.Combine("ids.txt"));
        await leaving.CancelAsync();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(30));

        // The outcome is Cancelled, never a failure and never a time-out, and the process is dead when
        // the caller hears of it.
        Assert.Equal(ProcessOutcome.Cancelled, result.Outcome);
        Assert.True(TestChild.IsGone(child));
        await TestChild.WaitUntilGoneAsync(child, grandchild);
        Assert.Equal(0, clock.ScheduledTimers);

        // A run that is cancelled before it starts starts nothing.
        var never = await runner.RunAsync(TestChild.Request("spawn", folder.Combine("never.txt"), "600000"), leaving.Token);
        Assert.Equal(ProcessOutcome.Cancelled, never.Outcome);
        Assert.False(File.Exists(folder.Combine("never.txt")));
    }

    [Fact]
    public async Task Without_the_job_only_the_process_itself_is_killed()
    {
        using var folder = new TempFolder();
        var runner = new ProcessRunner(TimeProvider.System);
        using var leaving = new CancellationTokenSource();

        var run = runner.RunAsync(
            TestChild.Request("spawn", folder.Combine("ids.txt"), "600000") with { KillWithApplication = false },
            leaving.Token);
        var (child, grandchild) = await TestChild.WaitForSpawnAsync(folder.Combine("ids.txt"));
        try
        {
            await leaving.CancelAsync();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(ProcessOutcome.Cancelled, result.Outcome);
            await TestChild.WaitUntilGoneAsync(child);

            // What the process started is left alone (for the lab scripts: Docker Desktop).
            await Task.Delay(300);
            Assert.False(TestChild.IsGone(grandchild));
        }
        finally
        {
            TestChild.Kill(grandchild);
            TestChild.Kill(child);
        }

        await TestChild.WaitUntilGoneAsync(child, grandchild);
    }

    [Fact]
    public async Task A_missing_executable_is_start_failed()
    {
        using var folder = new TempFolder();
        var runner = new ProcessRunner(TimeProvider.System);
        var missing = new ProcessRequest
        {
            FileName = folder.Combine("nothing-here.exe"),
            Arguments = ["--version"],
            Timeout = TimeSpan.FromSeconds(5),
        };

        var run = await runner.RunAsync(missing, None);
        var session = await Assert.ThrowsAsync<ProcessFailedException>(() => runner.StartSessionAsync(missing, None));
        var badFolder = await runner.RunAsync(
            TestChild.Request("exit", "0") with { WorkingDirectory = folder.Combine("no-such-folder") }, None);

        Assert.Equal(ProcessOutcome.StartFailed, run.Outcome);
        Assert.True(run.StandardOutput.IsEmpty);
        Assert.Equal(ProcessOutcome.StartFailed, session.Outcome);
        Assert.Equal(ProcessOutcome.StartFailed, badFolder.Outcome);

        // The message is fixed: no path and nothing of the operating system's text.
        Assert.Equal("The child process failed: StartFailed.", session.Message);
        Assert.Null(session.InnerException);
    }

    [Theory]
    [InlineData("codex.exe")]
    [InlineData(@"tools\codex.exe")]
    [InlineData(@"\codex.exe")]
    [InlineData(@"C:codex.exe")]
    [InlineData(@"C:\LiveCue-tests\codex")]
    [InlineData(@"C:\LiveCue-tests\codex.cmd")]
    [InlineData(@"C:\LiveCue-tests\codex.ps1")]
    [InlineData(@"C:\LiveCue-tests\codex.exe.bat")]
    [InlineData("")]
    public async Task A_relative_path_or_a_non_exe_is_refused(string fileName)
    {
        var runner = new ProcessRunner(TimeProvider.System);
        var request = new ProcessRequest { FileName = fileName, Arguments = [], Timeout = TimeSpan.FromSeconds(5) };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => runner.RunAsync(request, None));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => runner.StartSessionAsync(request, None));
    }

    [Fact]
    public async Task An_exit_code_is_reported_and_standard_error_is_discarded()
    {
        var runner = new ProcessRunner(TimeProvider.System);

        var seven = await runner.RunAsync(TestChild.Request("exit", "7") with { MaxStandardOutputBytes = Megabyte }, None);

        // A mode the child does not know prints its usage to standard error and ends with 64.
        var usage = await runner.RunAsync(TestChild.Request("no-such-mode") with { MaxStandardOutputBytes = Megabyte }, None);

        Assert.Equal(new { Outcome = ProcessOutcome.Exited, ExitCode = 7 }, new { seven.Outcome, seven.ExitCode });
        Assert.Equal(new { Outcome = ProcessOutcome.Exited, ExitCode = 64 }, new { usage.Outcome, usage.ExitCode });
        Assert.True(usage.StandardOutput.IsEmpty);
    }

    [Fact]
    public async Task A_session_exchanges_lines_and_its_dispose_kills_the_tree()
    {
        var clock = new ManualTimeProvider();
        var runner = new ProcessRunner(clock);

        // The child copies standard input to standard output: a line written comes back.
        var session = await runner.StartSessionAsync(
            TestChild.Request("stdin") with { MaxStandardOutputBytes = Megabyte, Timeout = TimeSpan.FromSeconds(12) }, None);
        await session.WriteLineAsync("first", None);
        var first = await session.ReadLineAsync(None).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        await session.WriteLineAsync("naïve ☃ {\"id\":2}", None);
        var second = await session.ReadLineAsync(None).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        await session.DisposeAsync();
        var afterDispose = await session.ReadLineAsync(None);
        await session.DisposeAsync();

        Assert.Equal("first", first);
        Assert.Equal("naïve ☃ {\"id\":2}", second);
        Assert.Null(afterDispose);
        Assert.Equal(0, clock.ScheduledTimers);
        await Assert.ThrowsAsync<ProcessFailedException>(() => session.WriteLineAsync("too late", None).AsTask());
    }

    [Fact]
    public async Task A_session_ends_at_its_limit_at_its_output_cap_and_when_the_process_ends()
    {
        using var folder = new TempFolder();
        var clock = new ManualTimeProvider();
        var runner = new ProcessRunner(clock);
        var limit = TimeSpan.FromSeconds(12);

        // The limit: a process that prints nothing.
        await using (var silent = await runner.StartSessionAsync(
            TestChild.Request("spawn", folder.Combine("ids.txt"), "600000") with { MaxStandardOutputBytes = Megabyte, Timeout = limit }, None))
        {
            var (child, grandchild) = await TestChild.WaitForSpawnAsync(folder.Combine("ids.txt"));
            var read = silent.ReadLineAsync(None).AsTask();
            clock.Advance(limit - TimeSpan.FromMilliseconds(1));
            await Task.Delay(100);
            Assert.False(read.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));

            var failure = await Assert.ThrowsAsync<ProcessFailedException>(() => read.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(ProcessOutcome.TimedOut, failure.Outcome);
            await TestChild.WaitUntilGoneAsync(child, grandchild);
        }

        // The cap: more output than allowed.
        await using (var flood = await runner.StartSessionAsync(
            TestChild.Request("write", "200000") with { MaxStandardOutputBytes = 100_000, Timeout = limit }, None))
        {
            var failure = await Assert.ThrowsAsync<ProcessFailedException>(
                () => flood.ReadLineAsync(None).AsTask().WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(ProcessOutcome.OutputLimitExceeded, failure.Outcome);
        }

        // The end: a process that prints one unfinished line and ends.
        await using (var brief = await runner.StartSessionAsync(
            TestChild.Request("write", "10") with { MaxStandardOutputBytes = Megabyte, Timeout = limit }, None))
        {
            Assert.Equal("xxxxxxxxxx", await brief.ReadLineAsync(None).AsTask().WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Null(await brief.ReadLineAsync(None).AsTask().WaitAsync(TimeSpan.FromSeconds(30)));
        }

        Assert.Equal(TimeSpan.FromSeconds(5), ProtocolConstants.KilledProcessWait);
        Assert.Equal(0, clock.ScheduledTimers);
    }

    /// <summary>The names of the environment variables a child of that policy sees.</summary>
    private static async Task<HashSet<string>> NamesAsync(ProcessRunner runner, EnvironmentPolicy policy)
    {
        var result = await runner.RunAsync(
            TestChild.Request("env-names") with { Environment = policy, MaxStandardOutputBytes = Megabyte }, None);
        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        return Encoding.UTF8.GetString(result.StandardOutput.Span)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
