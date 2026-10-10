using System.Globalization;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// The lab as the relay sees it (protocol 9.2, 11.2 and 11.5; statement P2-095; security test ST-11): the
/// two script command lines through the fake process runner, the relay's own script copies in a temporary
/// data folder, and the health probe against a loopback stub on the manual clock. Nothing is started.
///
/// The class runs outside the parallel collections because one test changes environment variables of the
/// test process.
/// </summary>
[Collection(ProcessEnvironmentGroup.Name)]
public sealed class LabRuntimeTests
{
    private const string Health =
        """{"model":"nemotron","ready":true,"loadSeconds":41.7,"sampleRate":16000,"maxSeconds":120,"chunkMs":320,"relayProtocol":1,"busy":false}""";

    private static readonly CancellationToken None = CancellationToken.None;

    private static string PowerShell =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public async Task The_two_scripts_run_with_the_exact_arguments_and_the_allowlist()
    {
        const string LabFolder = @"C:\LiveCue tests\asr lab";
        using var harness = new LocalModelHarness(LabFolder, labPort: 51234);
        var runtime = new LabRuntime(harness.Context);
        var data = harness.Data.Path;

        var started = await runtime.StartAsync("qwen3", None);
        var stopped = await runtime.StopAsync(None);

        Assert.True(started);
        Assert.True(stopped);
        Assert.Equal(2, harness.Processes.Requests.Count);
        var start = harness.Processes.Requests[0];
        var stop = harness.Processes.Requests[1];

        Assert.Equal(PowerShell, start.FileName);
        Assert.Equal(
            [
                "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", Path.Combine(data, "lab", "Start-ASR-Lab.ps1"),
                "-LabFolder", LabFolder,
                "-Model", "qwen3",
                "-Port", "51234",
                "-NoBrowser", "-EnsureDocker",
            ],
            start.Arguments);
        Assert.Equal(ProtocolConstants.LabStartTimeout, start.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(660), start.Timeout);

        Assert.Equal(PowerShell, stop.FileName);
        Assert.Equal(
            [
                "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", Path.Combine(data, "lab", "Stop-ASR-Lab.ps1"),
                "-Port", "51234",
            ],
            stop.Arguments);
        Assert.Equal(ProtocolConstants.LabStopTimeout, stop.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(45), stop.Timeout);

        Assert.All([start, stop], request =>
        {
            // Only the thirteen names of protocol 11.5 reach the child; the output is discarded; nothing is
            // written to it; and it is outside the kill-on-close job, so Docker Desktop outlives the relay.
            Assert.Equal(EnvironmentPolicy.LabScriptAllowlist, request.Environment);
            Assert.Equal(0, request.MaxStandardOutputBytes);
            Assert.Null(request.StandardInput);
            Assert.False(request.KillWithApplication);
            Assert.Equal(Environment.SystemDirectory, request.WorkingDirectory);
            Assert.True(Path.IsPathFullyQualified(request.FileName));
            Assert.EndsWith(".exe", request.FileName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Only_a_model_of_c04_ever_reaches_the_command_line()
    {
        using var harness = new LocalModelHarness();
        var runtime = new LabRuntime(harness.Context);

        foreach (var model in new[] { "../evil", "nemotron -NoProfile", "Nemotron", "nemotron;calc", string.Empty })
        {
            Assert.False(await runtime.StartAsync(model, None));
        }

        Assert.Empty(harness.Processes.Requests);
        Assert.True(await runtime.StartAsync("nemotron", None));
        Assert.Equal("nemotron", harness.Processes.Requests[0].Arguments[8]);
    }

    [Theory]
    [InlineData(ProcessOutcome.Exited, 1)]
    [InlineData(ProcessOutcome.Exited, -1)]
    [InlineData(ProcessOutcome.Exited, 259)]
    [InlineData(ProcessOutcome.TimedOut, 0)]
    [InlineData(ProcessOutcome.OutputLimitExceeded, 0)]
    [InlineData(ProcessOutcome.StartFailed, 0)]
    public async Task A_non_zero_exit_and_the_limit_are_failures(ProcessOutcome outcome, int exitCode)
    {
        using var harness = new LocalModelHarness();
        var runtime = new LabRuntime(harness.Context);
        var result = new ProcessResult(outcome, exitCode, ReadOnlyMemory<byte>.Empty);

        harness.Processes.Enqueue(result);
        var start = await runtime.StartAsync("nemotron", None);
        harness.Processes.Enqueue(result);
        var stop = await runtime.StopAsync(None);

        Assert.False(start);
        Assert.False(stop);
        Assert.Equal(2, harness.Processes.Requests.Count);

        // Exit code 0 is the one success.
        Assert.True(await runtime.StartAsync("nemotron", None));
        Assert.True(await runtime.StopAsync(None));
    }

    [Fact]
    public async Task A_runner_that_throws_is_a_failure_and_a_cancelled_run_is_a_cancellation()
    {
        using var harness = new LocalModelHarness();
        var runtime = new LabRuntime(harness.Context);

        harness.Processes.Enqueue(_ => throw new ArgumentException("PRIVATE API SECRET"));
        Assert.False(await runtime.StartAsync("nemotron", None));

        harness.Processes.Gate = LocalModelHarness.NewGate();
        using var leaving = new CancellationTokenSource();
        var run = runtime.StopAsync(leaving.Token);
        await Waiting.UntilAsync(() => harness.Processes.Requests.Count == 2);
        await leaving.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.StartAsync("nemotron", leaving.Token));
        Assert.Equal(2, harness.Processes.Requests.Count);
    }

    [Fact]
    public async Task The_script_that_runs_is_the_relays_own_copy()
    {
        // A lab folder that holds a launcher of its own: it supplies data only and is never run.
        using var lab = new TempFolder();
        var planted = lab.Combine(LabScripts.StartFileName);
        File.WriteAllText(planted, "throw 'A script of the lab folder was run.'");
        File.WriteAllText(lab.Combine(LabScripts.CommonFileName), "throw 'A script of the lab folder was loaded.'");

        using var harness = new LocalModelHarness(lab.Path);
        var runtime = new LabRuntime(harness.Context);
        var folder = Path.Combine(harness.Data.Path, "lab");
        var startCopy = Path.Combine(folder, LabScripts.StartFileName);
        var stopCopy = Path.Combine(folder, LabScripts.StopFileName);
        var commonCopy = Path.Combine(folder, LabScripts.CommonFileName);

        // What is on disk at the moment the process would start.
        byte[]? launcherAtRun = null;
        byte[]? commonAtRun = null;
        harness.Processes.Default = request =>
        {
            launcherAtRun = File.ReadAllBytes(request.Arguments[4]);
            commonAtRun = File.ReadAllBytes(commonCopy);
            return new ProcessResult(ProcessOutcome.Exited, 0, ReadOnlyMemory<byte>.Empty);
        };

        // 1. Nothing was written yet (no relay start in this test): the run writes its two files first.
        Assert.False(Directory.Exists(folder));
        Assert.True(await runtime.StartAsync("nemotron", None));

        var start = harness.Processes.Requests[0];
        Assert.Equal("-File", start.Arguments[3]);
        Assert.Equal(startCopy, start.Arguments[4]);
        Assert.StartsWith(folder + Path.DirectorySeparatorChar, start.Arguments[4], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(start.Arguments, argument => string.Equals(argument, planted, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(lab.Path, start.Arguments[6]);
        Assert.Equal(LabScripts.Embedded(LabScripts.StartFileName).ToArray(), launcherAtRun);
        Assert.Equal(LabScripts.Embedded(LabScripts.CommonFileName).ToArray(), commonAtRun);

        // 2. A launcher that was changed on disk is rewritten from the embedded bytes before the run.
        File.AppendAllText(startCopy, "\r\n& calc.exe\r\n");
        Assert.True(await runtime.StartAsync("nemotron", None));
        Assert.Equal(LabScripts.Embedded(LabScripts.StartFileName).ToArray(), launcherAtRun);

        // 3. So is the shared script that both launchers load, here changed without changing its length.
        var flipped = File.ReadAllBytes(commonCopy);
        flipped[^1] ^= 0x20;
        File.WriteAllBytes(commonCopy, flipped);
        Assert.True(await runtime.StopAsync(None));
        Assert.Equal(stopCopy, harness.Processes.Requests[^1].Arguments[4]);
        Assert.Equal(LabScripts.Embedded(LabScripts.StopFileName).ToArray(), launcherAtRun);
        Assert.Equal(LabScripts.Embedded(LabScripts.CommonFileName).ToArray(), commonAtRun);

        // 4. A copy that was deleted is written again.
        File.Delete(stopCopy);
        Assert.True(await runtime.StopAsync(None));
        Assert.Equal(LabScripts.Embedded(LabScripts.StopFileName).ToArray(), File.ReadAllBytes(stopCopy));

        // 5. A changed copy that cannot be rewritten starts nothing.
        var runs = harness.Processes.Requests.Count;
        File.WriteAllText(startCopy, "& calc.exe");
        using (new FileStream(startCopy, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(await runtime.StartAsync("nemotron", None));
        }

        File.Delete(commonCopy);
        Directory.CreateDirectory(commonCopy);
        Assert.False(await runtime.StartAsync("qwen3", None));
        Assert.False(await runtime.StopAsync(None));
        Assert.Equal(runs, harness.Processes.Requests.Count);

        // 6. Once the copies can be written again, the next run repairs them and starts.
        Directory.Delete(commonCopy);
        Assert.True(await runtime.StartAsync("qwen3", None));
        Assert.Equal(LabScripts.Embedded(LabScripts.StartFileName).ToArray(), launcherAtRun);
        Assert.Equal(runs + 1, harness.Processes.Requests.Count);

        // The lab folder's own files were never touched, and no request ever named one.
        Assert.Equal("throw 'A script of the lab folder was run.'", File.ReadAllText(planted));
        Assert.All(harness.Processes.Requests, request =>
        {
            Assert.StartsWith(folder + Path.DirectorySeparatorChar, request.Arguments[4], StringComparison.OrdinalIgnoreCase);
            Assert.Equal("-File", request.Arguments[3]);
        });
    }

    [Fact]
    public async Task The_powershell_path_ignores_SystemRoot()
    {
        var expected = PowerShell;
        using var decoy = new TempFolder();
        using var harness = new LocalModelHarness();
        var runtime = new LabRuntime(harness.Context);
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        var windir = Environment.GetEnvironmentVariable("windir");
        string path;
        try
        {
            Environment.SetEnvironmentVariable("SystemRoot", decoy.Path);
            Environment.SetEnvironmentVariable("windir", decoy.Path);
            path = LabScripts.PowerShellPath;
            Assert.True(await runtime.StartAsync("nemotron", None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SystemRoot", systemRoot);
            Environment.SetEnvironmentVariable("windir", windir);
        }

        var request = Assert.Single(harness.Processes.Requests);
        Assert.Equal(expected, path);
        Assert.Equal(expected, request.FileName);
        Assert.DoesNotContain(decoy.Path, request.FileName, StringComparison.OrdinalIgnoreCase);

        // The system directory of the operating-system API, and a file that is really there.
        Assert.StartsWith(Environment.SystemDirectory + Path.DirectorySeparatorChar, request.FileName, StringComparison.Ordinal);
        Assert.True(File.Exists(request.FileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\server\share\asr")]
    [InlineData("//server/share/asr")]
    [InlineData(@"\\?\C:\asr")]
    [InlineData(@"\\.\C:\asr")]
    [InlineData(@"\\?\UNC\server\share\asr")]
    [InlineData(@"\??\C:\asr")]
    [InlineData(@"\\localhost\C$\asr")]
    [InlineData("asr")]
    [InlineData(@".\asr")]
    [InlineData(@"..\asr")]
    [InlineData(@"\asr")]
    [InlineData("/asr")]
    [InlineData("C:asr")]
    [InlineData("C:")]
    [InlineData("C")]
    [InlineData(@" C:\asr")]
    [InlineData(@"1:\asr")]
    [InlineData(@"CD:\asr")]
    [InlineData("file:///C:/asr")]
    public async Task A_unc_or_device_lab_folder_is_not_configured(string? labFolder)
    {
        using var harness = new LocalModelHarness(labFolder);
        harness.Lab.LoadedModel = "nemotron";
        var runtime = new LabRuntime(harness.Context);

        // At use: no script is started, and nothing is even written for it.
        Assert.False(LabRuntime.IsUsableLabFolder(labFolder));
        Assert.False(await runtime.StartAsync("nemotron", None));
        Assert.False(await runtime.StopAsync(None));
        Assert.Empty(harness.Processes.Requests);
        Assert.False(Directory.Exists(Path.Combine(harness.Data.Path, "lab")));

        // To the phone: the same answer as "no lab folder" (MSG-2, LAB_NOT_CONFIGURED), without a probe.
        var controller = harness.Controller;
        var status = await controller.GetStatusAsync(None);
        Assert.False(controller.IsConfigured);
        Assert.Equal(
            new LocalModelStatus(LocalModelState.Stopped, Model: null, Busy: false, ElapsedSeconds: 0, FixedTexts.ModelNotSetUp),
            status);
        Assert.Equal(RelayErrors.LabNotConfigured, (await controller.StartAsync("nemotron", None)).Refusal);
        Assert.Equal(RelayErrors.LabNotConfigured, (await controller.StopAsync("nemotron", None)).Refusal);
        Assert.Equal(PrepareOutcome.LoadFailed, await controller.PrepareAsync("nemotron", None));
        Assert.Equal(0, harness.Lab.ProbeCalls);
        Assert.Equal(0, harness.Lab.StartCalls);
        Assert.Equal(0, harness.Lab.StopCalls);
    }

    [Theory]
    [InlineData(@"C:\asr")]
    [InlineData(@"d:\LiveCue\asr\")]
    [InlineData(@"Z:\")]
    [InlineData("C:/LiveCue/asr")]
    public void A_drive_letter_lab_folder_is_configured(string labFolder)
    {
        using var harness = new LocalModelHarness(labFolder);

        Assert.True(LabRuntime.IsUsableLabFolder(labFolder));
        Assert.True(harness.Controller.IsConfigured);
    }

    [Fact]
    public async Task Probe_reads_the_four_members()
    {
        await using var stub = new LabHealthStub(LabHealthStub.Reply(Health));
        using var harness = new LocalModelHarness(labPort: stub.Port);
        var runtime = new LabRuntime(harness.Context);

        var first = await runtime.ProbeAsync(None);
        stub.Respond = LabHealthStub.Reply(
            """{"busy":true,"relayProtocol":2,"ready":false,"model":"qwen3","unknown":{"nested":[1,2,3]}}""");
        var second = await runtime.ProbeAsync(None);
        stub.Respond = LabHealthStub.Reply("""{"ready":true,"busy":false,"relayProtocol":1,"model":7}""");
        var third = await runtime.ProbeAsync(None);

        Assert.Equal(new LabHealth(Ready: true, "nemotron", Busy: false, RelayProtocol: 1), first);
        Assert.Equal(new LabHealth(Ready: false, "qwen3", Busy: true, RelayProtocol: 2), second);
        Assert.Equal(new LabHealth(Ready: true, Model: null, Busy: false, RelayProtocol: 1), third);
        Assert.True(LabHealthParser.IsUsable(first, out var model));
        Assert.Equal("nemotron", model);
        Assert.False(LabHealthParser.IsUsable(second, out _));
        Assert.False(LabHealthParser.IsUsable(third, out _));

        // The request is the plain GET of protocol 11.1: the lab's own Host, no Origin, nothing of the phone,
        // and a connection of its own each time.
        Assert.Equal(3, stub.Requests.Count);
        Assert.All(stub.Requests, head =>
        {
            var lines = head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("GET /health HTTP/1.1", lines[0]);
            Assert.Contains($"Host: 127.0.0.1:{stub.Port}", lines);
            Assert.Contains("Connection: close", lines);
            Assert.DoesNotContain(lines, line =>
                line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Sec-Fetch-Site:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("X-LiveCue-", StringComparison.OrdinalIgnoreCase));
        });

        // No timer of the probe is left on the clock.
        Assert.Equal(0, harness.Clock.ScheduledTimers);
    }

    [Theory]
    [InlineData("hangup")]
    [InlineData("garbage")]
    [InlineData("status-404")]
    [InlineData("status-403")]
    [InlineData("status-500")]
    [InlineData("status-204")]
    [InlineData("redirect")]
    [InlineData("not-json")]
    [InlineData("array")]
    [InlineData("text")]
    [InlineData("empty")]
    [InlineData("ready-missing")]
    [InlineData("ready-as-text")]
    [InlineData("busy-missing")]
    [InlineData("busy-as-number")]
    [InlineData("protocol-missing")]
    [InlineData("protocol-as-text")]
    [InlineData("protocol-with-fraction")]
    [InlineData("too-deep")]
    [InlineData("oversized-declared")]
    [InlineData("oversized-undeclared")]
    [InlineData("silence")]
    [InlineData("slow-body")]
    [InlineData("nothing-listens")]
    public async Task Any_failure_timeout_or_oversized_reply_is_no_lab(string situation)
    {
        var oversized = OfSize(ProtocolConstants.MaxProbeBodyBytes + 1);
        var respond = situation switch
        {
            "hangup" or "nothing-listens" => LabHealthStub.Hangup(),
            "garbage" => LabHealthStub.Raw("\0\u0001\u0002 not HTTP at all\r\n\r\n"),
            "status-404" => LabHealthStub.Reply(Health, "404 Not Found"),
            "status-403" => LabHealthStub.Reply(Health, "403 Forbidden"),
            "status-500" => LabHealthStub.Reply(Health, "500 Internal Server Error"),
            "status-204" => LabHealthStub.Raw("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"),
            "not-json" => LabHealthStub.Reply("<html>ready</html>"),
            "array" => LabHealthStub.Reply("[" + Health + "]"),
            "text" => LabHealthStub.Reply("\"ready\""),
            "empty" => LabHealthStub.Reply(string.Empty),
            "ready-missing" => LabHealthStub.Reply("""{"model":"nemotron","relayProtocol":1,"busy":false}"""),
            "ready-as-text" => LabHealthStub.Reply("""{"model":"nemotron","ready":"true","relayProtocol":1,"busy":false}"""),
            "busy-missing" => LabHealthStub.Reply("""{"model":"nemotron","ready":true,"relayProtocol":1}"""),
            "busy-as-number" => LabHealthStub.Reply("""{"model":"nemotron","ready":true,"relayProtocol":1,"busy":0}"""),
            "protocol-missing" => LabHealthStub.Reply("""{"model":"nemotron","ready":true,"busy":false}"""),
            "protocol-as-text" => LabHealthStub.Reply("""{"model":"nemotron","ready":true,"relayProtocol":"1","busy":false}"""),
            "protocol-with-fraction" => LabHealthStub.Reply("""{"model":"nemotron","ready":true,"relayProtocol":1.5,"busy":false}"""),
            "too-deep" => LabHealthStub.Reply(
                "{\"deep\":" + new string('[', 40) + new string(']', 40) + ",\"ready\":true,\"busy\":false,\"relayProtocol\":1}"),
            "oversized-declared" => LabHealthStub.Reply(oversized),
            "oversized-undeclared" => LabHealthStub.Raw(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n" + oversized),
            "silence" => LabHealthStub.RawThenSilence(string.Empty),
            "slow-body" => LabHealthStub.RawThenSilence(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 500\r\n\r\n{\"ready\":true,"),
            _ => LabHealthStub.Hangup(),
        };

        await using var stub = new LabHealthStub(respond);
        if (situation == "redirect")
        {
            // The first request is sent elsewhere; a second one, if it ever came, would be answered.
            var location = string.Create(CultureInfo.InvariantCulture, $"Location: http://127.0.0.1:{stub.Port}/health?again=1");
            stub.Respond = (stream, ct) => stub.Requests.Count == 1
                ? LabHealthStub.Reply(string.Empty, "302 Found", location)(stream, ct)
                : LabHealthStub.Reply(Health)(stream, ct);
        }

        if (situation == "nothing-listens")
        {
            await stub.DisposeAsync();
        }

        using var harness = new LocalModelHarness(labPort: stub.Port);
        var runtime = new LabRuntime(harness.Context);

        LabHealth? health;
        if (situation is "silence" or "slow-body")
        {
            // Nothing ends this probe but C-45, and C-45 is a timer of the relay's clock.
            var probe = runtime.ProbeAsync(None);
            Assert.Equal(1, harness.Clock.ScheduledTimers);
            await Waiting.UntilAsync(() => stub.Requests.Count == 1);
            harness.Clock.Advance(ProtocolConstants.LabProbeTimeout - TimeSpan.FromMilliseconds(1));
            await Task.Delay(50);
            Assert.False(probe.IsCompleted);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
            health = await probe.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(TimeSpan.FromSeconds(1.5), ProtocolConstants.LabProbeTimeout);
        }
        else if (situation == "nothing-listens")
        {
            // A refused connection can take the operating system longer than C-45; the clock ends the wait.
            var probe = runtime.ProbeAsync(None);
            harness.Clock.Advance(ProtocolConstants.LabProbeTimeout);
            health = await probe.WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            health = await runtime.ProbeAsync(None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Null(health);
        Assert.Equal(0, harness.Clock.ScheduledTimers);
        if (situation == "redirect")
        {
            // The redirect was not followed.
            Assert.Single(stub.Requests);
        }
        else if (situation is not ("nothing-listens" or "silence" or "slow-body"))
        {
            // A reply of any kind is final: the probe asked once. (Only a connection that closes before a
            // single byte of reply is tried again by the HTTP client, always inside the same C-45.)
            var requests = stub.Requests.Count;
            if (situation != "hangup")
            {
                Assert.Equal(1, requests);
            }

            // The next probe of a lab that answers finds it at once.
            stub.Respond = LabHealthStub.Reply(Health);
            Assert.NotNull(await runtime.ProbeAsync(None).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(requests + 1, stub.Requests.Count);
        }
    }

    [Fact]
    public async Task A_reply_of_exactly_the_probe_limit_is_read_and_one_byte_more_is_not()
    {
        await using var stub = new LabHealthStub(LabHealthStub.Reply(OfSize(ProtocolConstants.MaxProbeBodyBytes)));
        using var harness = new LocalModelHarness(labPort: stub.Port);
        var runtime = new LabRuntime(harness.Context);

        var atTheLimit = await runtime.ProbeAsync(None);
        stub.Respond = LabHealthStub.Reply(OfSize(ProtocolConstants.MaxProbeBodyBytes + 1));
        var beyond = await runtime.ProbeAsync(None);

        Assert.Equal(4096, ProtocolConstants.MaxProbeBodyBytes);
        Assert.Equal(new LabHealth(Ready: true, "qwen3", Busy: false, RelayProtocol: 1), atTheLimit);
        Assert.Null(beyond);
    }

    [Fact]
    public async Task A_cancelled_probe_is_a_cancellation_and_not_no_lab()
    {
        await using var stub = new LabHealthStub(LabHealthStub.RawThenSilence(string.Empty));
        using var harness = new LocalModelHarness(labPort: stub.Port);
        var runtime = new LabRuntime(harness.Context);
        using var leaving = new CancellationTokenSource();

        var probe = runtime.ProbeAsync(leaving.Token);
        await Waiting.UntilAsync(() => stub.Requests.Count == 1);
        await leaving.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.ProbeAsync(leaving.Token));
        Assert.Single(stub.Requests);
        Assert.Equal(0, harness.Clock.ScheduledTimers);
    }

    /// <summary>A valid health reply of exactly <paramref name="bytes"/> bytes.</summary>
    private static string OfSize(int bytes)
    {
        const string Head = "{\"model\":\"qwen3\",\"ready\":true,\"relayProtocol\":1,\"busy\":false,\"pad\":\"";
        const string Tail = "\"}";
        return string.Concat(Head, new string('p', bytes - Head.Length - Tail.Length), Tail);
    }
}

/// <summary>
/// Tests that change an environment variable of the test process run one at a time, after the parallel
/// collections have finished.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentGroup
{
    public const string Name = "LocalModel: process environment";
}
