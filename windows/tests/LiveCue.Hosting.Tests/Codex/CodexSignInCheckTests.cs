using System.Text;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Hosting.Tests.Codex;

/// <summary>
/// The two questions asked of the Codex executable (architecture 3.7, test plan 2.2 and ST-03). Nothing
/// is started: the process runner is the fake one, and the request it receives is what is checked.
/// </summary>
public sealed class CodexSignInCheckTests
{
    private const string Codex = @"C:\Tools\codex.exe";

    private readonly FakeProcessRunner _processes = new();

    [Fact]
    public async Task Command_limit_environment_and_exit_code()
    {
        var check = new CodexSignInCheck(_processes);

        _processes.EnqueueExit(0, "Logged in using ChatGPT");
        Assert.True(await check.IsSignedInAsync(Codex, CancellationToken.None));

        var request = Assert.Single(_processes.Requests);
        Assert.Equal(Codex, request.FileName);
        Assert.Equal(["login", "status"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(15), request.Timeout);
        Assert.Equal(EnvironmentPolicy.CodexAllowlist, request.Environment);
        Assert.Equal(0, request.MaxStandardOutputBytes);
        Assert.Null(request.StandardInput);
        Assert.Null(request.WorkingDirectory);
        Assert.True(request.KillWithApplication);

        // Exit code 0 only. Everything else is "not signed in", a time-out included.
        _processes.EnqueueExit(1, "Not logged in");
        Assert.False(await check.IsSignedInAsync(Codex, CancellationToken.None));
        _processes.EnqueueExit(-1);
        Assert.False(await check.IsSignedInAsync(Codex, CancellationToken.None));
        foreach (var outcome in (ProcessOutcome[])[ProcessOutcome.TimedOut, ProcessOutcome.StartFailed, ProcessOutcome.OutputLimitExceeded])
        {
            _processes.Enqueue(new ProcessResult(outcome, 0, ReadOnlyMemory<byte>.Empty));
            Assert.False(await check.IsSignedInAsync(Codex, CancellationToken.None));
        }

        // A path the runner refuses to start is "not signed in", not an exception.
        var refusing = new CodexSignInCheck(new RefusingRunner());
        Assert.False(await refusing.IsSignedInAsync("codex.cmd", CancellationToken.None));
        Assert.Null(await refusing.ReadVersionAsync("codex.cmd", CancellationToken.None));
    }

    public static TheoryData<int, string, string?> VersionOutputs => new()
    {
        // Accepted: a plain version text, the first line only, trimmed.
        { 0, "codex-cli 0.46.0", "codex-cli 0.46.0" },
        { 0, "codex-cli 0.46.0\n", "codex-cli 0.46.0" },
        { 0, "  codex-cli 0.46.0 \r\nsecond line \"with\" anything\r\n", "codex-cli 0.46.0" },
        { 0, "1.2.3-beta_4+build.5", "1.2.3-beta_4+build.5" },
        { 0, new string('7', 64), new string('7', 64) },

        // Refused: nothing that is not the fixed shape is ever kept or shown.
        { 0, "", null },
        { 0, "\ncodex-cli 0.46.0", null },
        { 0, "   \n", null },
        { 0, new string('7', 65), null },
        { 0, "codex-cli \"0.46.0\"", null },
        { 0, "codex-cli '0.46.0'", null },
        { 0, @"C:\Users\someone\codex.exe 0.46.0", null },
        { 0, "/usr/bin/codex 0.46.0", null },
        { 0, "codex-cli\t0.46.0", null },
        { 0, "codex-cli 0.46.0<BEL>", null },
        { 0, "codex-cli<NUL>0.46.0", null },
        { 0, "codex-cli<ESC>[31m 0.46.0", null },
        { 0, "codex-cli 0.46.0\u2028more", null },
        { 0, "códex 1.0", null },
        { 0, "codex <1.0>", null },

        // A non-zero exit is "unknown", whatever was printed.
        { 1, "codex-cli 0.46.0", null },
        { -1, "codex-cli 0.46.0", null },
    };

    [Theory]
    [MemberData(nameof(VersionOutputs))]
    public async Task Version_read_cases(int exitCode, string output, string? expected)
    {
        // The three control characters are spelled out in the cases, so that a case's name is printable.
        output = output
            .Replace("<BEL>", "\u0007", StringComparison.Ordinal)
            .Replace("<NUL>", "\u0000", StringComparison.Ordinal)
            .Replace("<ESC>", "\u001b", StringComparison.Ordinal);
        var check = new CodexSignInCheck(_processes);
        _processes.EnqueueExit(exitCode, output);

        Assert.Equal(expected, await check.ReadVersionAsync(Codex, CancellationToken.None));

        var request = Assert.Single(_processes.Requests);
        Assert.Equal(Codex, request.FileName);
        Assert.Equal(["--version"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(15), request.Timeout);
        Assert.Equal(EnvironmentPolicy.CodexAllowlist, request.Environment);
        Assert.Equal(256, request.MaxStandardOutputBytes);
        Assert.Null(request.StandardInput);
        Assert.True(request.KillWithApplication);
    }

    [Theory]
    [InlineData(ProcessOutcome.TimedOut)]
    [InlineData(ProcessOutcome.StartFailed)]
    [InlineData(ProcessOutcome.OutputLimitExceeded)]
    public async Task A_version_read_that_did_not_end_by_itself_is_unknown(ProcessOutcome outcome)
    {
        // Even when a runner hands back output with such an outcome, it is not used.
        _processes.Enqueue(new ProcessResult(outcome, 0, Encoding.UTF8.GetBytes("codex-cli 0.46.0")));

        Assert.Null(await new CodexSignInCheck(_processes).ReadVersionAsync(Codex, CancellationToken.None));
    }

    [Fact]
    public async Task Output_that_is_not_text_is_unknown()
    {
        _processes.Enqueue(new ProcessResult(ProcessOutcome.Exited, 0, new byte[] { 0x31, 0x2E, 0xFF, 0xFE, 0x32 }));

        Assert.Null(await new CodexSignInCheck(_processes).ReadVersionAsync(Codex, CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_call_ends_with_cancellation_and_not_with_an_answer()
    {
        var check = new CodexSignInCheck(_processes);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.IsSignedInAsync(Codex, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.ReadVersionAsync(Codex, cancelled.Token));
        Assert.Empty(_processes.Requests);

        // Cancelled while the process runs: the runner reports Cancelled, and the call throws.
        using var later = new CancellationTokenSource();
        _processes.Gate = new TaskCompletionSource();
        var running = check.IsSignedInAsync(Codex, later.Token);
        await later.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Theory]
    [InlineData("codex-cli 0.46.0", true)]
    [InlineData("a", true)]
    [InlineData("A-Z a-z 0-9 . _ + -", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("1.0\n", false)]
    [InlineData("1.0\r\n", false)]
    [InlineData("1.0\u2028", false)]
    [InlineData("1,0", false)]
    [InlineData("1.0/2", false)]
    [InlineData("１.０", false)]
    public void The_version_shape_is_a_whole_text_match(string? text, bool expected)
    {
        Assert.Equal(expected, CodexSignInCheck.IsVersionText(text));
    }

    /// <summary>A runner that refuses every request the way the real one refuses a path that is not an .exe file.</summary>
    private sealed class RefusingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct) =>
            throw new ArgumentException("The file to run must be the absolute path of an .exe file.", nameof(request));

        public Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct) =>
            throw new ArgumentException("The file to run must be the absolute path of an .exe file.", nameof(request));
    }
}
