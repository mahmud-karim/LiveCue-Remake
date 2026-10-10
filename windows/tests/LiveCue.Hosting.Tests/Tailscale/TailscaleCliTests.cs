using LiveCue.Hosting.Tailscale;
using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay.Processes;

namespace LiveCue.Hosting.Tests.Tailscale;

/// <summary>
/// The Tailscale CLI over a process runner that starts nothing: the six argument lists, the limit of
/// rule TS-10, what is kept of the output, and what each way of ending means.
/// </summary>
public sealed class TailscaleCliTests
{
    [Fact]
    public async Task Each_call_has_the_limit_and_discards_output_except_status()
    {
        var tailscale = new FakeTailscale();
        var cli = new TailscaleCli(tailscale.Processes, tailscale.Files);

        var status = await cli.ReadStatusAsync(CancellationToken.None);
        var serve = await cli.ReadServeStatusAsync(CancellationToken.None);
        MappingCommand[] commands =
        [
            MappingCommand.ApplyPrivate(11443, 47841),
            MappingCommand.ApplyPublic(10000, "/livecue", 47841),
            MappingCommand.RemovePrivate(11443),
            MappingCommand.RemovePublic(10000, "/livecue"),
        ];
        foreach (var command in commands)
        {
            Assert.Equal(TailscaleWriteOutcome.Succeeded, await cli.RunAsync(command, CancellationToken.None));
        }

        Assert.Equal(new TailscaleNodeStatus(true, "pc.example.test"), status.Value);
        Assert.Equal(TailscaleReadFailure.None, status.Failure);
        Assert.Empty(serve.Value!.Handlers);
        Assert.Equal(TailscaleReadFailure.None, serve.Failure);

        var requests = tailscale.Processes.Requests;
        Assert.Equal(
            [
                "status --json",
                "serve status --json",
                "serve --bg --https=11443 http://127.0.0.1:47841",
                "funnel --bg --https=10000 --set-path=/livecue http://127.0.0.1:47841",
                "serve --https=11443 --set-path=/ off",
                "funnel --https=10000 --set-path=/livecue off",
            ],
            requests.Select(request => string.Join(' ', request.Arguments)));

        Assert.Equal(TimeSpan.FromSeconds(30), TailscaleCli.Limit);
        Assert.All(requests, request =>
        {
            // TS-10: the limit. And nothing but the one executable, started without a shell's help.
            Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
            Assert.Equal(FakeTailscale.ExecutablePath, request.FileName);
            Assert.Equal(EnvironmentPolicy.Inherit, request.Environment);
            Assert.Null(request.StandardInput);
            Assert.Null(request.WorkingDirectory);
            Assert.True(request.KillWithApplication);
        });

        // Output is kept for the two status commands only.
        Assert.All(requests.Take(2), request => Assert.True(request.MaxStandardOutputBytes > 0));
        Assert.All(requests.Skip(2), request => Assert.Equal(0, request.MaxStandardOutputBytes));
    }

    [Theory]
    [InlineData(ProcessOutcome.Exited, 1, TailscaleReadFailure.Failed)]
    [InlineData(ProcessOutcome.TimedOut, 0, TailscaleReadFailure.TimedOut)]
    [InlineData(ProcessOutcome.StartFailed, 0, TailscaleReadFailure.Failed)]
    [InlineData(ProcessOutcome.OutputLimitExceeded, 0, TailscaleReadFailure.Unparseable)]
    public async Task A_read_that_does_not_end_well_has_no_value(ProcessOutcome outcome, int exitCode, TailscaleReadFailure expected)
    {
        var tailscale = new FakeTailscale { ReadOutcome = outcome, ReadExitCode = exitCode };
        var cli = new TailscaleCli(tailscale.Processes, tailscale.Files);

        var status = await cli.ReadStatusAsync(CancellationToken.None);
        var serve = await cli.ReadServeStatusAsync(CancellationToken.None);

        Assert.Null(status.Value);
        Assert.Equal(expected, status.Failure);
        Assert.Null(serve.Value);
        Assert.Equal(expected, serve.Failure);
    }

    [Fact]
    public async Task Output_that_is_not_the_expected_document_is_unparseable()
    {
        var tailscale = new FakeTailscale { StatusDocument = "Tailscale is stopped." };
        tailscale.ServeStatus(ServeStatusFixtures.NotAnObject);
        var cli = new TailscaleCli(tailscale.Processes, tailscale.Files);

        Assert.Equal(TailscaleReadFailure.Unparseable, (await cli.ReadStatusAsync(CancellationToken.None)).Failure);
        Assert.Equal(TailscaleReadFailure.Unparseable, (await cli.ReadServeStatusAsync(CancellationToken.None)).Failure);
    }

    [Theory]
    [InlineData(ProcessOutcome.Exited, 0, TailscaleWriteOutcome.Succeeded)]
    [InlineData(ProcessOutcome.Exited, 1, TailscaleWriteOutcome.Failed)]
    [InlineData(ProcessOutcome.TimedOut, 0, TailscaleWriteOutcome.TimedOut)]
    [InlineData(ProcessOutcome.StartFailed, 0, TailscaleWriteOutcome.Failed)]
    [InlineData(ProcessOutcome.OutputLimitExceeded, 0, TailscaleWriteOutcome.Failed)]
    public async Task How_a_write_ends(ProcessOutcome outcome, int exitCode, TailscaleWriteOutcome expected)
    {
        var tailscale = new FakeTailscale { WriteOutcome = outcome, WriteExitCode = exitCode };
        var cli = new TailscaleCli(tailscale.Processes, tailscale.Files);

        Assert.Equal(expected, await cli.RunAsync(MappingCommand.ApplyPrivate(11443, 47841), CancellationToken.None));
    }

    [Fact]
    public async Task Without_an_executable_nothing_is_started()
    {
        var tailscale = new FakeTailscale(installed: false);
        var cli = new TailscaleCli(tailscale.Processes, tailscale.Files);

        Assert.Null(cli.ExecutablePath);
        Assert.Equal(TailscaleReadFailure.NotInstalled, (await cli.ReadStatusAsync(CancellationToken.None)).Failure);
        Assert.Equal(TailscaleReadFailure.NotInstalled, (await cli.ReadServeStatusAsync(CancellationToken.None)).Failure);
        Assert.Equal(
            TailscaleWriteOutcome.NotInstalled,
            await cli.RunAsync(MappingCommand.RemovePrivate(11443), CancellationToken.None));
        Assert.Empty(tailscale.Processes.Requests);

        // Installed in the meantime: the next use finds it, without a restart.
        tailscale.Files.AddFile(FakeTailscale.ExecutablePath);
        Assert.Equal(FakeTailscale.ExecutablePath, cli.ExecutablePath);
        Assert.Equal(TailscaleReadFailure.None, (await cli.ReadStatusAsync(CancellationToken.None)).Failure);
    }

    [Fact]
    public async Task An_executable_that_is_gone_when_it_is_started_is_not_installed()
    {
        var tailscale = new FakeTailscale { ReadOutcome = ProcessOutcome.StartFailed, WriteOutcome = ProcessOutcome.StartFailed };
        var files = new VanishingFiles(tailscale.Files);
        var cli = new TailscaleCli(tailscale.Processes, files);
        Assert.Equal(FakeTailscale.ExecutablePath, cli.ExecutablePath);

        files.Gone = true;

        Assert.Equal(TailscaleReadFailure.NotInstalled, (await cli.ReadServeStatusAsync(CancellationToken.None)).Failure);
        Assert.Null(cli.ExecutablePath);
        files.Gone = false;
        Assert.Equal(FakeTailscale.ExecutablePath, cli.ExecutablePath);
        files.Gone = true;
        Assert.Equal(
            TailscaleWriteOutcome.NotInstalled,
            await cli.RunAsync(MappingCommand.ApplyPrivate(11443, 47841), CancellationToken.None));
    }

    [Fact]
    public async Task A_command_whose_arguments_were_changed_after_the_factory_is_not_started()
    {
        var tailscale = new FakeTailscale();
        var cli = new TailscaleCli(tailscale.Processes, tailscale.Files);

        foreach (var (index, text) in new[] { (0, "funnel"), (1, "--yes"), (2, "--https=443"), (3, "reset"), (3, "http://192.0.2.1:47841") })
        {
            var command = MappingCommand.ApplyPrivate(11443, 47841);
            ((string[])command.Arguments)[index] = text;

            Assert.Equal(TailscaleWriteOutcome.Failed, await cli.RunAsync(command, CancellationToken.None));
        }

        var removal = MappingCommand.RemovePublic(10000, "/livecue");
        ((string[])removal.Arguments)[2] = "--set-path=/other";
        Assert.Equal(TailscaleWriteOutcome.Failed, await cli.RunAsync(removal, CancellationToken.None));

        Assert.Empty(tailscale.Processes.Requests);
    }

    [Fact]
    public async Task The_runner_gets_its_own_copy_of_the_arguments()
    {
        var tailscale = new FakeTailscale();
        var cli = new TailscaleCli(tailscale.Processes, tailscale.Files);
        var command = MappingCommand.RemovePrivate(11443);

        await cli.RunAsync(command, CancellationToken.None);
        await cli.ReadServeStatusAsync(CancellationToken.None);
        foreach (var request in tailscale.Processes.Requests)
        {
            // A runner that tried to change what it was given changes nothing that is used again.
            Assert.NotSame(command.Arguments, request.Arguments);
            if (request.Arguments is IList<string> { IsReadOnly: false } list)
            {
                list[0] = "reset";
            }
        }

        await cli.RunAsync(command, CancellationToken.None);
        await cli.ReadServeStatusAsync(CancellationToken.None);

        Assert.Equal(["serve", "--https=11443", "--set-path=/", "off"], tailscale.Processes.Requests[2].Arguments);
        Assert.Equal(["serve", "status", "--json"], tailscale.Processes.Requests[3].Arguments);
    }

    [Fact]
    public async Task Cancellation_is_the_only_thing_that_is_thrown()
    {
        var tailscale = new FakeTailscale();
        var cli = new TailscaleCli(tailscale.Processes, tailscale.Files);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cli.ReadStatusAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cli.ReadServeStatusAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cli.RunAsync(MappingCommand.RemovePrivate(11443), cancelled.Token));
        Assert.Empty(tailscale.Processes.Requests);

        // Cancelled while the process runs: the runner ends it, and the call throws.
        using var later = new CancellationTokenSource();
        tailscale.Processes.Gate = new TaskCompletionSource();
        var running = cli.RunAsync(MappingCommand.RemovePrivate(11443), later.Token);
        await later.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        // A runner that refuses the request is a failed start, not an exception.
        var refusing = new TailscaleCli(new RefusingRunner(), tailscale.Files);
        Assert.Equal(TailscaleReadFailure.Failed, (await refusing.ReadServeStatusAsync(CancellationToken.None)).Failure);
        Assert.Equal(
            TailscaleWriteOutcome.Failed,
            await refusing.RunAsync(MappingCommand.RemovePrivate(11443), CancellationToken.None));
    }

    [Fact]
    public void Composing_the_cli_looks_nothing_up()
    {
        var files = new CountingFiles(new FakeTailscale().Files);

        var cli = new TailscaleCli(new FakeTailscale().Processes, files);
        Assert.Equal(0, files.Questions);

        Assert.Equal(FakeTailscale.ExecutablePath, cli.ExecutablePath);
        var afterFirstUse = files.Questions;
        Assert.True(afterFirstUse > 0);

        // A path that was found is kept.
        Assert.Equal(FakeTailscale.ExecutablePath, cli.ExecutablePath);
        Assert.Equal(afterFirstUse, files.Questions);
    }

    /// <summary>A runner that refuses every request, as the real one does for a path that is not an .exe.</summary>
    private sealed class RefusingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct) =>
            throw new ArgumentException("The file to run must be the absolute path of an .exe file.", nameof(request));

        public Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct) =>
            throw new ProcessFailedException(ProcessOutcome.StartFailed);
    }

    /// <summary>A file system in which every file can disappear at once.</summary>
    private sealed class VanishingFiles(IHostFileSystem inner) : ForwardingFiles(inner)
    {
        public bool Gone { get; set; }

        public override bool FileExists(string path) => !Gone && base.FileExists(path);
    }

    /// <summary>A file system that counts what it is asked.</summary>
    private sealed class CountingFiles(IHostFileSystem inner) : ForwardingFiles(inner)
    {
        public int Questions { get; private set; }

        public override string ProgramFilesDirectory
        {
            get
            {
                Questions++;
                return base.ProgramFilesDirectory;
            }
        }

        public override bool FileExists(string path)
        {
            Questions++;
            return base.FileExists(path);
        }

        public override string? GetEnvironmentVariable(string name)
        {
            Questions++;
            return base.GetEnvironmentVariable(name);
        }
    }

    private class ForwardingFiles(IHostFileSystem inner) : IHostFileSystem
    {
        public string BaseDirectory => inner.BaseDirectory;

        public virtual string ProgramFilesDirectory => inner.ProgramFilesDirectory;

        public bool IsOnFixedLocalDrive(string path) => inner.IsOnFixedLocalDrive(path);

        public virtual string? GetEnvironmentVariable(string name) => inner.GetEnvironmentVariable(name);

        public virtual bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public void WriteAllBytesAtomic(string path, byte[] contents) => inner.WriteAllBytesAtomic(path, contents);

        public IReadOnlyList<FoundFile> FindFiles(string directory, string fileName, int maxDepth) =>
            inner.FindFiles(directory, fileName, maxDepth);
    }
}
