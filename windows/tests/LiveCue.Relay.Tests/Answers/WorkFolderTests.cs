using System.Text;
using System.Text.RegularExpressions;
using LiveCue.Relay.Answers;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Answers;

/// <summary>
/// Security test ST-15 (finding SEC-10), relay side: what Codex writes into its working folder does not
/// outlive the request, the folder has a name nobody can guess, and a folder that is already there is
/// never used. (That a relay start empties <c>work\</c> belongs to the host's own tests.)
/// </summary>
public sealed partial class WorkFolderTests
{
    private const string Canary = "CANARY-7f3a9c51-transcript-text-that-must-not-stay-on-disk";

    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("time-out")]
    [InlineData("cancel")]
    public async Task No_file_under_work_holds_the_transcript_after_any_ending(string ending)
    {
        using var harness = new AreaHarness();
        SchemaFiles.Rewrite(harness.Data.Path);
        var backend = new CodexAnswerBackend(harness.Context);
        var run = new AnswerRun(
            AnswerKind.Assist,
            "request-0001",
            "gpt-test",
            "low",
            Encoding.UTF8.GetBytes("""{"requestId":"request-0001","transcript":"__CANARY__"}""".Replace("__CANARY__", Canary, StringComparison.Ordinal)));
        using var leaving = new CancellationTokenSource();

        // What a model run leaves in its folder: an answer that quotes the transcript, a session file
        // that holds the whole prompt, and a file in a folder of its own.
        static void WriteAsCodexWould(ProcessRequest request)
        {
            var folder = request.WorkingDirectory!;
            Assert.Contains(Canary, request.StandardInput, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(folder, "output.json"), "{\"answer\":\"You said " + Canary + "\"}");
            File.WriteAllText(Path.Combine(folder, "rollout.jsonl"), request.StandardInput);
            Directory.CreateDirectory(Path.Combine(folder, "cache", "deep"));
            File.WriteAllText(Path.Combine(folder, "cache", "deep", "note.txt"), Canary);
        }

        byte[]? output = null;
        switch (ending)
        {
            case "cancel":
                harness.Processes.Gate = AreaHarness.NewGate();
                var cancelled = backend.RunAsync(run, leaving.Token);
                await Waiting.UntilAsync(() => harness.Processes.Requests.Count == 1);
                WriteAsCodexWould(harness.Processes.Requests[0]);
                Assert.NotEmpty(FilesHoldingTheCanary(harness));
                await leaving.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
                break;

            default:
                harness.Processes.Enqueue(request =>
                {
                    WriteAsCodexWould(request);
                    Assert.NotEmpty(FilesHoldingTheCanary(harness));
                    return ending switch
                    {
                        "success" => new ProcessResult(ProcessOutcome.Exited, 0, default),
                        "failure" => new ProcessResult(ProcessOutcome.Exited, 1, default),
                        _ => new ProcessResult(ProcessOutcome.TimedOut, -1, default),
                    };
                });
                output = await backend.RunAsync(run, None);
                break;
        }

        // Only a successful run hands anything back, and that is in memory.
        Assert.Equal(ending == "success", output is not null);

        // Afterwards no file under the data folder holds the transcript, and work\ is empty: what is
        // left is the two schema files.
        Assert.Empty(FilesHoldingTheCanary(harness));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(harness.Data.Path, "work")));
        Assert.Equal(
            [Path.Combine("schemas", "assist.schema.json"), Path.Combine("schemas", "summary.schema.json")],
            harness.Data.FileNames());
        Assert.Empty(harness.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task The_folder_name_is_32_hex_characters_and_an_existing_folder_fails_the_run()
    {
        using var harness = new AreaHarness();
        var work = Path.Combine(harness.Data.Path, "work");

        // The name: 32 lower-case hex characters from the system's random source, new for every run.
        var drawn = new CodexAnswerBackend(harness.Context);
        await drawn.RunAsync(Run(), None);
        await drawn.RunAsync(Run(), None);
        var names = harness.Processes.Requests.Select(request => Path.GetFileName(request.WorkingDirectory!)).ToList();
        Assert.Equal(2, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.Matches(FolderName(), name));
        Assert.All(harness.Processes.Requests, request => Assert.Equal(work, Path.GetDirectoryName(request.WorkingDirectory)));
        var many = Enumerable.Range(0, 2_000).Select(_ => CodexAnswerBackend.RandomFolderName()).ToList();
        Assert.Equal(many.Count, many.Distinct(StringComparer.Ordinal).Count());
        Assert.All(many, name => Assert.Matches(FolderName(), name));

        // A folder of that name that is already there is not this run's folder: the run fails, nothing
        // is started in it, and it is left exactly as it was.
        const string Taken = "ffffffffffffffffffffffffffffffff";
        var existing = Path.Combine(work, Taken);
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "output.json"), "planted before the run");
        var given = new CodexAnswerBackend(harness.Context, () => Taken);

        var result = await given.RunAsync(Run(), None);

        Assert.Null(result);
        Assert.Equal(2, harness.Processes.Requests.Count);
        Assert.Equal("planted before the run", File.ReadAllText(Path.Combine(existing, "output.json")));

        // The same for a file of that name.
        Directory.Delete(existing, recursive: true);
        File.WriteAllText(existing, "a file where the folder would be");
        Assert.Null(await given.RunAsync(Run(), None));
        Assert.Equal(2, harness.Processes.Requests.Count);
        Assert.Equal("a file where the folder would be", File.ReadAllText(existing));
    }

    [Fact]
    public async Task A_folder_that_stays_locked_is_tried_three_times_and_then_reported()
    {
        using var harness = new AreaHarness();
        var backend = new CodexAnswerBackend(harness.Context);
        FileStream? held = null;
        harness.Processes.Enqueue(request =>
        {
            // Something still has a file of the folder open when the run returns.
            File.WriteAllBytes(Path.Combine(request.WorkingDirectory!, "output.json"), Bodies.ValidAssistOutput());
            held = new FileStream(
                Path.Combine(request.WorkingDirectory!, "locked.bin"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            return new ProcessResult(ProcessOutcome.Exited, 0, default);
        });

        try
        {
            var run = backend.RunAsync(Run(), None);

            // Two gaps of W-09 on the relay's clock, between three tries.
            await Waiting.UntilAsync(() => harness.Clock.ScheduledTimers == 1);
            Assert.False(run.IsCompleted);
            harness.Clock.Advance(ProtocolConstants.FolderDeleteGap);
            await Waiting.UntilAsync(() => harness.Clock.ScheduledTimers == 1);
            Assert.False(run.IsCompleted);
            harness.Clock.Advance(ProtocolConstants.FolderDeleteGap);
            var output = await run.WaitAsync(TimeSpan.FromSeconds(10));

            // The answer is still delivered; the leftover is reported with a fixed text and a type name.
            Assert.Equal(Bodies.ValidAssistOutput(), output);
            var diagnostic = Assert.Single(harness.Events.OfType<Diagnostic>());
            Assert.Equal("answers", diagnostic.Source);
            Assert.StartsWith("A working folder of an answer run could not be deleted. (", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(harness.Data.Path, diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal((3, TimeSpan.FromMilliseconds(100)), (ProtocolConstants.FolderDeleteAttempts, ProtocolConstants.FolderDeleteGap));
            Assert.Equal(0, harness.Clock.ScheduledTimers);
        }
        finally
        {
            held?.Dispose();
        }
    }

    [Fact]
    public async Task A_read_only_file_does_not_keep_the_folder_and_a_link_is_removed_without_being_followed()
    {
        using var harness = new AreaHarness();
        using var elsewhere = new LiveCue.TestSupport.TempFolder();
        File.WriteAllText(elsewhere.Combine("the-owners-file.txt"), "must stay");
        var backend = new CodexAnswerBackend(harness.Context);
        harness.Processes.Enqueue(request =>
        {
            var folder = request.WorkingDirectory!;
            Directory.CreateDirectory(Path.Combine(folder, "objects"));
            var readOnly = Path.Combine(folder, "objects", "pack.bin");
            File.WriteAllText(readOnly, Canary);
            File.SetAttributes(readOnly, FileAttributes.ReadOnly | FileAttributes.Hidden);
            try
            {
                // A link from inside the folder to a folder of the owner's. Making one needs a right the
                // account may not have; without it this part of the test is left out.
                Directory.CreateSymbolicLink(Path.Combine(folder, "way-out"), elsewhere.Path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            return new ProcessResult(ProcessOutcome.Exited, 1, default);
        });

        var output = await harness.PumpAsync(backend.RunAsync(Run(), None));

        Assert.Null(output);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(harness.Data.Path, "work")));
        Assert.Empty(FilesHoldingTheCanary(harness));
        Assert.Empty(harness.Events.OfType<Diagnostic>());

        // Whether or not the link could be made, what it pointed at is untouched.
        Assert.Equal("must stay", File.ReadAllText(elsewhere.Combine("the-owners-file.txt")));
    }

    private static AnswerRun Run() => new(
        AnswerKind.Assist, "request-0001", "gpt-test", "low", Encoding.UTF8.GetBytes("""{"requestId":"request-0001"}"""));

    /// <summary>Every file under the data folder whose bytes hold the canary.</summary>
    private static List<string> FilesHoldingTheCanary(AreaHarness harness) =>
    [
        .. Directory.EnumerateFiles(harness.Data.Path, "*", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains(Canary, StringComparison.Ordinal)),
    ];

    [GeneratedRegex(@"\A[0-9a-f]{32}\z")]
    private static partial Regex FolderName();
}
