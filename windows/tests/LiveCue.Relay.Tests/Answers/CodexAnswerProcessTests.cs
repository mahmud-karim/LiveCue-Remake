using System.Text;
using System.Text.Json;
using LiveCue.Relay.Answers;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Processes;
using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// The answer run against a real process: the production backend and the production runner, with
/// <c>LiveCue.TestChild</c> standing in for <c>codex exec</c>. The stand-in reports what it was given
/// (arguments, prompt, working folder, environment names), writes into its folder, and in one scenario
/// starts a process of its own and never ends. However a run ends, afterwards there is no process and
/// no folder.
/// </summary>
public sealed class CodexAnswerProcessTests
{
    private const string Body =
        """{"requestId":"request-0001","assistant":{"model":"gpt-test","reasoningEffort":"low"},"transcript":"naïve ☃ question"}""";

    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData(AnswerKind.Assist)]
    [InlineData(AnswerKind.Summary)]
    public async Task A_run_returns_the_output_file_and_leaves_no_process_and_no_folder(AnswerKind kind)
    {
        using var reports = new TempFolder();
        var report = reports.Combine("report.json");
        var runner = new StandInCodexRunner(new ProcessRunner(TimeProvider.System), "ok", report);
        using var harness = new AreaHarness(processes: runner, systemClock: true);
        SchemaFiles.Rewrite(harness.Data.Path);
        var backend = new CodexAnswerBackend(harness.Context);

        var output = await backend.RunAsync(Run(kind), None).WaitAsync(TimeSpan.FromSeconds(60));

        // The output file's bytes, which pass the relay's own check and lose what is not in the protocol.
        Assert.NotNull(output);
        Assert.True(AnswerValidator.TryBuildReply(kind, output, "gpt-test", "low", default, out var reply));
        var text = Encoding.UTF8.GetString(reply);
        Assert.DoesNotContain("dropped", text, StringComparison.Ordinal);
        Assert.DoesNotContain("11111111", text, StringComparison.Ordinal);

        // What the real process was given.
        using var seen = JsonDocument.Parse(await File.ReadAllBytesAsync(report));
        var folder = seen.RootElement.GetProperty("workingFolder").GetString()!;
        var request = Assert.Single(runner.Requests);
        Assert.Equal(request.WorkingDirectory, folder);
        Assert.Equal(Path.Combine(harness.Data.Path, "work"), Path.GetDirectoryName(folder));
        Assert.Equal(Prompts.Build(kind, Encoding.UTF8.GetBytes(Body)), seen.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(
            [.. request.Arguments, "--stub", "ok", "--report", report],
            seen.RootElement.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()));
        Assert.Equal(
            CodexArguments.Build("gpt-test", "low", SchemaFiles.PathFor(harness.Data.Path, kind), folder),
            request.Arguments);
        var allowed = ProcessRunnerTests.CodexNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(
            seen.RootElement.GetProperty("environment").EnumerateArray(),
            name => Assert.Contains(name.GetString()!, allowed));

        // No process and no folder.
        Assert.True(TestChild.IsGone(seen.RootElement.GetProperty("processId").GetInt32()));
        Assert.False(Directory.Exists(folder));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(harness.Data.Path, "work")));
    }

    [Fact]
    public async Task A_cancelled_run_leaves_no_process_and_no_folder()
    {
        using var reports = new TempFolder();
        var report = reports.Combine("report.json");
        var runner = new StandInCodexRunner(new ProcessRunner(TimeProvider.System), "hang", report);
        using var harness = new AreaHarness(processes: runner, systemClock: true);
        var backend = new CodexAnswerBackend(harness.Context);
        using var leaving = new CancellationTokenSource();

        var run = backend.RunAsync(Run(AnswerKind.Assist), leaving.Token);
        var (child, grandchild, folder) = await HangingAsync(report);

        // The run is under way: two processes, and a file in the folder that holds the whole prompt.
        Assert.False(run.IsCompleted);
        Assert.Contains("naïve ☃ question", await File.ReadAllTextAsync(Path.Combine(folder, "scratch.txt")), StringComparison.Ordinal);

        await leaving.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(60)));

        // When the caller hears of the cancellation, the process is dead and the folder is gone.
        Assert.True(TestChild.IsGone(child));
        Assert.False(Directory.Exists(folder));
        await TestChild.WaitUntilGoneAsync(child, grandchild);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(harness.Data.Path, "work")));
        Assert.Empty(harness.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task A_timed_out_run_leaves_no_process_and_no_folder()
    {
        using var reports = new TempFolder();
        var report = reports.Combine("report.json");
        var clock = new ManualTimeProvider();
        var runner = new StandInCodexRunner(new ProcessRunner(clock), "hang", report);
        using var harness = new AreaHarness(processes: runner, manualClock: clock);
        var backend = new CodexAnswerBackend(harness.Context);

        var run = backend.RunAsync(Run(AnswerKind.Summary), None);
        var (child, grandchild, folder) = await HangingAsync(report);

        // Nothing ends this run but the runner's limit, C-43, on the relay's clock.
        clock.Advance(ProtocolConstants.AnswerDeadline - TimeSpan.FromMilliseconds(1));
        await Task.Delay(100);
        Assert.False(run.IsCompleted);
        Assert.False(TestChild.IsGone(child));
        clock.Advance(TimeSpan.FromMilliseconds(1));

        // (The clock is moved on in small steps in case the folder needs a second try at its deletion.)
        var output = await harness.PumpAsync(run);

        Assert.Null(output);
        Assert.True(TestChild.IsGone(child));
        Assert.False(Directory.Exists(folder));
        await TestChild.WaitUntilGoneAsync(child, grandchild);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(harness.Data.Path, "work")));
        Assert.Equal(0, clock.ScheduledTimers);
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("no-output")]
    [InlineData("big")]
    public async Task A_run_without_a_usable_output_file_is_no_answer_and_leaves_nothing(string scenario)
    {
        using var reports = new TempFolder();
        var report = reports.Combine("report.json");
        var runner = new StandInCodexRunner(new ProcessRunner(TimeProvider.System), scenario, report);
        using var harness = new AreaHarness(processes: runner, systemClock: true);
        var backend = new CodexAnswerBackend(harness.Context);

        var output = await backend.RunAsync(Run(AnswerKind.Assist), None).WaitAsync(TimeSpan.FromSeconds(60));

        // A non-zero exit code, no file, and a file beyond C-24: each is "no usable file".
        Assert.Null(output);
        using var seen = JsonDocument.Parse(await File.ReadAllBytesAsync(report));
        Assert.True(TestChild.IsGone(seen.RootElement.GetProperty("processId").GetInt32()));
        Assert.False(Directory.Exists(seen.RootElement.GetProperty("workingFolder").GetString()));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(harness.Data.Path, "work")));
    }

    [Fact]
    public async Task The_coordinator_with_the_production_backend_answers_from_a_real_output_file()
    {
        using var reports = new TempFolder();
        var report = reports.Combine("report.json");
        var runner = new StandInCodexRunner(new ProcessRunner(TimeProvider.System), "ok", report);
        using var harness = new AreaHarness(processes: runner, systemClock: true, productionBackend: true);
        SchemaFiles.Rewrite(harness.Data.Path);
        var coordinator = new AnswerCoordinator(harness.Context, new CatalogReader(harness.Context));
        using var request = new RouteRequest(harness, RouteId.Assist, Bodies.Assist());

        await coordinator.HandleAsync(request.Context, AnswerKind.Assist).WaitAsync(TimeSpan.FromSeconds(60));

        // The stand-in wrote a marker and a member the protocol does not have. The reply carries the
        // marker as null and does not carry the extra member.
        var (status, body) = Assert.Single(request.Io.Responses);
        Assert.Equal(200, status);
        Assert.StartsWith(
            """{"detectedQuestion":"Stand-in question?","answer":"Stand-in answer.","details":"","memory":{"summary":"Stand-in memory.","facts":[],"openQuestions":[],"throughSegmentId":null},"execution":{"model":"gpt-test","reasoningEffort":"low","codexMs":""",
            body,
            StringComparison.Ordinal);
        using var reply = JsonDocument.Parse(body);
        Assert.True(reply.RootElement.GetProperty("execution").GetProperty("codexMs").GetDouble() > 0);
        Assert.Single(harness.Events.OfType<RequestStarted>());
        Assert.Single(harness.Events.OfType<RequestCompleted>());

        // The run asked for the model the catalogue offers, and left nothing behind.
        var arguments = Assert.Single(runner.Requests).Arguments;
        Assert.Equal("gpt-test", arguments[arguments.ToList().IndexOf("-m") + 1]);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(harness.Data.Path, "work")));
    }

    private static AnswerRun Run(AnswerKind kind) =>
        new(kind, "request-0001", "gpt-test", "low", Encoding.UTF8.GetBytes(Body));

    /// <summary>Waits for the report of the "hang" scenario: both process ids and the working folder.</summary>
    private static async Task<(int Child, int Grandchild, string Folder)> HangingAsync(string report)
    {
        JsonDocument? seen = null;
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (seen is null)
        {
            try
            {
                seen = File.Exists(report) ? JsonDocument.Parse(File.ReadAllBytes(report)) : null;
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                // The stand-in is writing the file right now.
            }

            if (seen is null)
            {
                Assert.True(DateTime.UtcNow < until, "The stand-in did not write its report in time.");
                await Task.Delay(20);
            }
        }

        using (seen)
        {
            var root = seen.RootElement;
            var child = root.GetProperty("processId").GetInt32();
            var grandchild = root.GetProperty("grandchildId").GetInt32();
            Assert.False(TestChild.IsGone(child));
            Assert.False(TestChild.IsGone(grandchild));
            return (child, grandchild, root.GetProperty("workingFolder").GetString()!);
        }
    }
}
