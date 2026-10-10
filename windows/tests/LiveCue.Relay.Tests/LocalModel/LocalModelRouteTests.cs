using System.Net;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// Routes 4, 5 and 6 through a real loopback listener (protocol 6.5, 6.6 and statements P2-087 to P2-094):
/// the check order M-1 to M-7, the codes, and the exact bytes of the status object.
/// </summary>
public sealed class LocalModelRouteTests
{
    private const string StatusRoute = "/v1/local-model";
    private const string StartRoute = "/v1/local-model/start";
    private const string StopRoute = "/v1/local-model/stop";

    private const string StoppedJson =
        """{"state":"stopped","model":null,"busy":false,"elapsedSeconds":0,"message":"PC model is stopped. Start opens Docker Desktop if needed."}""";

    private const string NotSetUpJson =
        """{"state":"stopped","model":null,"busy":false,"elapsedSeconds":0,"message":"PC speech models are not set up on this PC. Set the speech lab folder in LiveCue Desktop."}""";

    private const string StartingNemotronJson =
        """{"state":"starting","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Starting Docker and loading the PC model. First load can take several minutes."}""";

    private const string StoppingNemotronJson =
        """{"state":"stopping","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Unloading the PC model to free GPU memory…"}""";

    private const string ReadyNemotronJson =
        """{"state":"ready","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Model loaded on the PC GPU. Ready for live transcription."}""";

    private const string InUseNemotronJson =
        """{"state":"ready","model":"nemotron","busy":true,"elapsedSeconds":0,"message":"Model is in use. Stop the conversation or lab test before unloading."}""";

    [Theory]
    [InlineData("""{"model":"../evil"}""")]
    [InlineData("""{"model":"../shell"}""")]
    [InlineData("""{"model":"Nemotron"}""")]
    [InlineData("""{"model":"nemotron "}""")]
    [InlineData("""{"model":"nemotron\n"}""")]
    [InlineData("""{"model":"nemotron -NoProfile"}""")]
    [InlineData("""{"model":""}""")]
    [InlineData("""{"model":null}""")]
    [InlineData("""{"model":1}""")]
    [InlineData("""{"model":["nemotron"]}""")]
    [InlineData("""{"model":{"name":"nemotron"}}""")]
    [InlineData("""{"Model":"nemotron"}""")]
    [InlineData("""{"model":"nemotron","model":"../evil"}""")]
    [InlineData("{}")]
    public async Task A_bad_model_name_is_400_and_starts_nothing(string body)
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Lab.LoadedModel = "nemotron";

        using var start = await host.PostAsync(StartRoute, body);
        using var stop = await host.PostAsync(StopRoute, body);
        await host.Events.SettleAsync();

        await AssertErrorAsync(start, RelayErrors.InvalidModel);
        await AssertErrorAsync(stop, RelayErrors.InvalidModel);
        Assert.Equal(
            """{"error":"Choose Nemotron or Qwen3.","code":"INVALID_MODEL"}""",
            await start.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Lab.StartCalls);
        Assert.Equal(0, host.Lab.StopCalls);
        Assert.Equal(0, host.Lab.ProbeCalls);
        Assert.Empty(host.Processes.Requests);
        Assert.Equal(0, host.Events.Count<LocalModelChanged>());
    }

    [Fact]
    public async Task Start_is_202_and_status_becomes_ready()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var before = await host.GetAsync(StatusRoute);
        using var start = await host.PostAsync(StartRoute, Bodies.LocalModel("nemotron"));
        await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Ready);
        using var after = await host.GetAsync(StatusRoute);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(StoppedJson, await before.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(StartingNemotronJson, await start.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(ReadyNemotronJson, await after.Content.ReadAsStringAsync());
        Assert.Equal(["nemotron"], host.Lab.StartedModels);

        // The desktop is told when the operation begins and when it ends, with the status message.
        var changes = host.Events.OfType<LocalModelChanged>();
        Assert.Equal([LocalModelState.Starting, LocalModelState.Ready], changes.Select(changed => changed.Status.State));
        Assert.All(changes, changed => Assert.Equal(changed.Status.Message, changed.Message));
    }

    [Fact]
    public async Task Status_during_an_operation_counts_whole_seconds_without_probing()
    {
        await using var host = await RelayTestHost.StartAsync();
        var gate = host.Lab.StartGate = LocalModelHarness.NewGate();

        using (var start = await host.PostAsync(StartRoute, Bodies.LocalModel("qwen3")))
        {
            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        }

        await Waiting.UntilAsync(() => host.Lab.StartCalls == 1);
        var probes = host.Lab.ProbeCalls;
        host.Clock.Advance(TimeSpan.FromSeconds(7.9));
        using var during = await host.GetAsync(StatusRoute);

        Assert.Equal(
            """{"state":"starting","model":"qwen3","busy":false,"elapsedSeconds":7,"message":"Starting Docker and loading the PC model. First load can take several minutes."}""",
            await during.Content.ReadAsStringAsync());
        Assert.Equal(probes, host.Lab.ProbeCalls);

        gate.SetResult();
        await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Ready);
        using var after = await host.GetAsync(StatusRoute);
        Assert.Contains("\"state\":\"ready\",\"model\":\"qwen3\"", await after.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_busy_lab_refuses_stop_and_start()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Lab.LoadedModel = "nemotron";
        host.Lab.Busy = true;

        using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("nemotron"));
        using var start = await host.PostAsync(StartRoute, Bodies.LocalModel("qwen3"));
        using var status = await host.GetAsync(StatusRoute);

        await AssertErrorAsync(stop, RelayErrors.ModelBusy);
        await AssertErrorAsync(start, RelayErrors.ModelBusy);
        Assert.Equal("""{"error":"The PC model is in use.","code":"MODEL_BUSY"}""", await stop.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Lab.StopCalls);
        Assert.Equal(0, host.Lab.StartCalls);
        Assert.Equal(InUseNemotronJson, await status.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stopping_another_model_is_a_mismatch()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Lab.LoadedModel = "nemotron";

        using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("qwen3"));
        using var status = await host.GetAsync(StatusRoute);

        await AssertErrorAsync(stop, RelayErrors.ModelMismatch);
        Assert.Equal(
            """{"error":"A different PC model is loaded. Select it before stopping.","code":"MODEL_MISMATCH"}""",
            await stop.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Lab.StopCalls);
        Assert.Equal(ReadyNemotronJson, await status.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_held_lease_refuses_stop_and_reports_busy()
    {
        // The real controller over a lease the test can take: a speech stream that is open and past "ready"
        // is, for this area, exactly a held lease.
        using var harness = new LocalModelHarness();
        harness.Lab.LoadedModel = "nemotron";
        await using var host = await RelayTestHost.StartAsync(options => options.LocalModelController = harness.Controller);

        using var lease = harness.Lease.TryAcquire();
        using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("nemotron"));
        using var start = await host.PostAsync(StartRoute, Bodies.LocalModel("qwen3"));
        using var status = await host.GetAsync(StatusRoute);

        Assert.NotNull(lease);
        Assert.False(harness.Lab.Busy);
        await AssertErrorAsync(stop, RelayErrors.ModelBusy);
        await AssertErrorAsync(start, RelayErrors.ModelBusy);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(InUseNemotronJson, await status.Content.ReadAsStringAsync());
        Assert.Equal(0, harness.Lab.StopCalls);
        Assert.Equal(0, harness.Lab.StartCalls);
    }

    [Fact]
    public async Task After_the_lease_is_released_stop_is_202_then_stopped()
    {
        using var harness = new LocalModelHarness();
        harness.Lab.LoadedModel = "nemotron";
        await using var host = await RelayTestHost.StartAsync(options => options.LocalModelController = harness.Controller);

        using (harness.Lease.TryAcquire())
        {
            using var refused = await host.PostAsync(StopRoute, Bodies.LocalModel("nemotron"));
            await AssertErrorAsync(refused, RelayErrors.ModelBusy);
        }

        using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("nemotron"));
        await harness.WaitForIdleAsync();
        using var status = await host.GetAsync(StatusRoute);

        Assert.Equal(HttpStatusCode.Accepted, stop.StatusCode);
        Assert.Equal(StoppingNemotronJson, await stop.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(StoppedJson, await status.Content.ReadAsStringAsync());
        Assert.Equal(1, harness.Lab.StopCalls);
    }

    [Fact]
    public async Task Starting_a_loaded_model_is_200_without_a_script_run()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Lab.LoadedModel = "nemotron";

        using var start = await host.PostAsync(StartRoute, Bodies.LocalModel("nemotron"));
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal(ReadyNemotronJson, await start.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Lab.StartCalls);
        Assert.Empty(host.Processes.Requests);
        Assert.Equal(0, host.Events.Count<LocalModelChanged>());
    }

    [Fact]
    public async Task Without_a_lab_folder_status_is_msg2_and_commands_are_503()
    {
        await using var host = await RelayTestHost.StartAsync(options => options.LabConfigured = false);
        host.Lab.LoadedModel = "nemotron";

        using var status = await host.GetAsync(StatusRoute);
        using var start = await host.PostAsync(StartRoute, Bodies.LocalModel("nemotron"));
        using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("nemotron"));

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(NotSetUpJson, await status.Content.ReadAsStringAsync());
        await AssertErrorAsync(start, RelayErrors.LabNotConfigured);
        await AssertErrorAsync(stop, RelayErrors.LabNotConfigured);
        Assert.Equal(
            """{"error":"PC speech models are not set up on this PC.","code":"LAB_NOT_CONFIGURED"}""",
            await start.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Lab.ProbeCalls);
        Assert.Equal(0, host.Lab.StartCalls);
        Assert.Equal(0, host.Lab.StopCalls);
    }

    [Fact]
    public async Task A_second_command_during_an_operation_is_409()
    {
        await using var host = await RelayTestHost.StartAsync();
        var gate = host.Lab.StartGate = LocalModelHarness.NewGate();

        using var first = await host.PostAsync(StartRoute, Bodies.LocalModel("nemotron"));
        using var start = await host.PostAsync(StartRoute, Bodies.LocalModel("qwen3"));
        using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("nemotron"));
        gate.SetResult();
        await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Ready);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await AssertErrorAsync(start, RelayErrors.ModelOperationInProgress);
        await AssertErrorAsync(stop, RelayErrors.ModelOperationInProgress);
        Assert.Equal(
            """{"error":"A PC model operation is already in progress. Wait for it to finish.","code":"MODEL_OPERATION_IN_PROGRESS"}""",
            await start.Content.ReadAsStringAsync());
        Assert.Equal(["nemotron"], host.Lab.StartedModels);
        Assert.Equal(0, host.Lab.StopCalls);
    }

    [Fact]
    public async Task A_failed_start_is_the_error_status_with_no_private_text_on_the_wire()
    {
        await using var host = await RelayTestHost.StartAsync();
        host.Lab.ProbeFails = true;
        host.Lab.StartThrowsPrivateText = true;

        using var start = await host.PostAsync(StartRoute, Bodies.LocalModel("nemotron"));
        await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Error);
        using var failed = await host.GetAsync(StatusRoute);
        var failedJson = await failed.Content.ReadAsStringAsync();
        host.Clock.Advance(ProtocolConstants.ModelErrorLatch);
        using var expired = await host.GetAsync(StatusRoute);
        await host.Events.SettleAsync();

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.Equal(
            """{"state":"error","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Could not load the PC model. Check Docker Desktop, available GPU memory and the installed model weights, then retry."}""",
            failedJson);
        Assert.Equal(StoppedJson, await expired.Content.ReadAsStringAsync());
        Assert.DoesNotContain("PRIVATE", failedJson, StringComparison.Ordinal);
        Assert.All(host.Events.Snapshot(), relayEvent =>
        {
            Assert.DoesNotContain("PRIVATE", relayEvent.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET", relayEvent.ToString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Checks_run_in_the_order_of_the_protocol()
    {
        // M-2 before M-3: a body that is not a JSON object never reaches the model check.
        await using (var host = await RelayTestHost.StartAsync())
        {
            foreach (var route in new[] { StartRoute, StopRoute })
            {
                using var notAnObject = await host.PostAsync(route, "[]");
                using var empty = await host.PostAsync(route, string.Empty);
                await AssertErrorAsync(notAnObject, RelayErrors.InvalidJson);
                await AssertErrorAsync(empty, RelayErrors.InvalidJson);
            }

            Assert.Equal(0, host.Lab.ProbeCalls);
        }

        // M-3 before M-4: a bad model name is told even where no lab folder is set.
        await using (var host = await RelayTestHost.StartAsync(options => options.LabConfigured = false))
        {
            using var start = await host.PostAsync(StartRoute, Bodies.LocalModel("../evil"));
            using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("../evil"));
            await AssertErrorAsync(start, RelayErrors.InvalidModel);
            await AssertErrorAsync(stop, RelayErrors.InvalidModel);
        }

        // M-5 before M-6: while an operation runs, a busy lab does not change the answer, and nothing is probed.
        await using (var host = await RelayTestHost.StartAsync())
        {
            var gate = host.Lab.StartGate = LocalModelHarness.NewGate();
            using var first = await host.PostAsync(StartRoute, Bodies.LocalModel("nemotron"));
            host.Lab.LoadedModel = "qwen3";
            host.Lab.Busy = true;
            var probes = host.Lab.ProbeCalls;

            using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("nemotron"));
            await AssertErrorAsync(stop, RelayErrors.ModelOperationInProgress);
            Assert.Equal(probes, host.Lab.ProbeCalls);

            host.Lab.Busy = false;
            gate.SetResult();
            await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Ready);
        }

        // M-6 before M-7: a busy lab that has another model loaded is busy, not a mismatch.
        await using (var host = await RelayTestHost.StartAsync())
        {
            host.Lab.LoadedModel = "nemotron";
            host.Lab.Busy = true;
            using var stop = await host.PostAsync(StopRoute, Bodies.LocalModel("qwen3"));
            await AssertErrorAsync(stop, RelayErrors.ModelBusy);
        }
    }

    [Fact]
    public async Task Extra_members_and_an_escaped_model_name_are_accepted_as_the_exact_model()
    {
        await using var host = await RelayTestHost.StartAsync();

        using var start = await host.PostAsync(StartRoute, """{"device":"x","model":"nemotron","extra":[1,2]}""");
        await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Ready);

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(["nemotron"], host.Lab.StartedModels);
    }

    /// <summary>The status, and the exact body, of one row of the error table (protocol 14.1).</summary>
    internal static async Task AssertErrorAsync(HttpResponseMessage response, RelayError expected)
    {
        Assert.Equal(expected.Status, (int)response.StatusCode);
        Assert.Equal(RelayErrors.Body(expected).ToArray(), await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }
}
