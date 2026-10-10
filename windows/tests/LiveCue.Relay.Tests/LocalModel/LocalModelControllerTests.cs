using System.Text;
using System.Text.Json;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Routes;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// Protocol 9.3 at area level (statements P2-080 to P2-086 and rule 7): the real controller over the fake
/// runtime of protocol 15.5, on the manual clock. No listener.
/// </summary>
public sealed class LocalModelControllerTests : IDisposable
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly LocalModelHarness _harness = new();

    private LocalModelController Controller => _harness.Controller;

    private FakeLabRuntime Lab => _harness.Lab;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_new_controller_is_stopped_with_all_five_members()
    {
        var status = await Controller.GetStatusAsync(None);

        Assert.Equal(Stopped(), status);
        Assert.True(Controller.IsConfigured);
        Assert.False(Controller.OperationRunning);

        using var json = JsonDocument.Parse(LocalModelStatusRoute.ToJson(status));
        var root = json.RootElement;
        Assert.Equal(
            ["state", "model", "busy", "elapsedSeconds", "message"],
            root.EnumerateObject().Select(member => member.Name));
        Assert.Equal("stopped", root.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("model").ValueKind);
        Assert.Equal(JsonValueKind.False, root.GetProperty("busy").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("elapsedSeconds").ValueKind);
        Assert.Equal("0", root.GetProperty("elapsedSeconds").GetRawText());
        Assert.Equal("PC model is stopped. Start opens Docker Desktop if needed.", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Start_answers_starting_and_status_then_does_not_probe()
    {
        var gate = Lab.StartGate = LocalModelHarness.NewGate();

        var command = await Controller.StartAsync("nemotron", None);

        Assert.Null(command.Refusal);
        Assert.True(command.Began);
        Assert.Equal(Starting("nemotron", 0), command.Status);
        Assert.True(Controller.OperationRunning);
        Assert.Equal([Starting("nemotron", 0)], _harness.Events.Statuses);
        Assert.All(_harness.Events.OfType<LocalModelChanged>(), changed => Assert.Equal(changed.Status.Message, changed.Message));

        await Waiting.UntilAsync(() => Lab.StartCalls == 1);
        var probes = Lab.ProbeCalls;
        _harness.Clock.Advance(TimeSpan.FromMilliseconds(5));
        var soon = await Controller.GetStatusAsync(None);
        _harness.Clock.Advance(TimeSpan.FromSeconds(61.9));
        var later = await Controller.GetStatusAsync(None);

        Assert.Equal(Starting("nemotron", 0), soon);
        Assert.Equal(Starting("nemotron", 61), later);
        Assert.Equal(probes, Lab.ProbeCalls);

        gate.SetResult();
        await _harness.WaitForIdleAsync();
    }

    [Fact]
    public async Task A_second_command_during_an_operation_is_refused()
    {
        var gate = Lab.StartGate = LocalModelHarness.NewGate();
        await Controller.StartAsync("nemotron", None);

        var start = await Controller.StartAsync("qwen3", None);
        var stop = await Controller.StopAsync("nemotron", None);

        Assert.Equal(RelayErrors.ModelOperationInProgress, start.Refusal);
        Assert.Null(start.Status);
        Assert.False(start.Began);
        Assert.Equal(RelayErrors.ModelOperationInProgress, stop.Refusal);
        Assert.Null(stop.Status);
        Assert.False(stop.Began);

        gate.SetResult();
        await _harness.WaitForIdleAsync();
        Assert.Equal(["nemotron"], Lab.StartedModels);
        Assert.Equal(0, Lab.StopCalls);
    }

    [Fact]
    public async Task After_the_gate_opens_the_model_is_loaded_and_prepare_does_not_start_again()
    {
        var gate = Lab.StartGate = LocalModelHarness.NewGate();
        await Controller.StartAsync("nemotron", None);

        gate.SetResult();
        await _harness.WaitForIdleAsync();
        var status = await Controller.GetStatusAsync(None);
        var prepared = await Controller.PrepareAsync("nemotron", None);

        Assert.Equal(Ready("nemotron"), status);
        Assert.Equal([Starting("nemotron", 0), Ready("nemotron")], _harness.Events.Statuses);
        Assert.Equal(PrepareOutcome.Ready, prepared);
        Assert.Equal(1, Lab.StartCalls);
        Assert.False(Controller.OperationRunning);
    }

    [Fact]
    public async Task Starting_the_other_model_switches()
    {
        Lab.LoadedModel = "nemotron";

        var command = await Controller.StartAsync("qwen3", None);
        await _harness.WaitForIdleAsync();

        Assert.True(command.Began);
        Assert.Equal(Starting("qwen3", 0), command.Status);
        Assert.Equal(Ready("qwen3"), await Controller.GetStatusAsync(None));
        Assert.Equal(["qwen3"], Lab.StartedModels);
        Assert.Equal(0, Lab.StopCalls);
    }

    [Fact]
    public async Task Stop_ends_stopped_with_one_stop_call()
    {
        Lab.LoadedModel = "qwen3";

        var command = await Controller.StopAsync("qwen3", None);
        await _harness.WaitForIdleAsync();

        Assert.Null(command.Refusal);
        Assert.True(command.Began);
        Assert.Equal(
            new LocalModelStatus(LocalModelState.Stopping, "qwen3", Busy: false, ElapsedSeconds: 0, FixedTexts.ModelStopping),
            command.Status);
        Assert.Equal(Stopped(), await Controller.GetStatusAsync(None));
        Assert.Equal(1, Lab.StopCalls);
        Assert.Equal(0, Lab.StartCalls);
        Assert.Equal([command.Status!, Stopped()], _harness.Events.Statuses);
    }

    [Fact]
    public async Task Stop_with_nothing_loaded_still_runs_the_stop_script()
    {
        var command = await Controller.StopAsync("nemotron", None);
        await _harness.WaitForIdleAsync();

        Assert.True(command.Began);
        Assert.Equal(1, Lab.StopCalls);
        Assert.Equal(Stopped(), await Controller.GetStatusAsync(None));
    }

    [Fact]
    public async Task A_failed_start_is_an_error_without_private_text()
    {
        Lab.ProbeFails = true;
        Lab.StartThrowsPrivateText = true;

        var command = await Controller.StartAsync("nemotron", None);
        await _harness.WaitForIdleAsync();
        var status = await Controller.GetStatusAsync(None);

        Assert.True(command.Began);
        Assert.Equal(Error("nemotron", FixedTexts.ModelStartFailed), status);
        Assert.Equal(
            "Could not load the PC model. Check Docker Desktop, available GPU memory and the installed model weights, then retry.",
            status.Message);
        Assert.Equal([Starting("nemotron", 0), status], _harness.Events.Statuses);
        Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(LocalModelStatusRoute.ToJson(status)), StringComparison.Ordinal);
        Assert.All(
            _harness.Events.Snapshot(),
            relayEvent => Assert.DoesNotContain("PRIVATE", relayEvent.ToString(), StringComparison.OrdinalIgnoreCase));

        // What the desktop is told about the failure is a type name and a fixed sentence.
        var diagnostic = Assert.Single(_harness.Events.OfType<Diagnostic>());
        Assert.Equal("A PC model operation failed unexpectedly. (InvalidOperationException)", diagnostic.Message);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("probe")]
    [InlineData("lab-protocol")]
    public async Task A_start_that_does_not_end_with_the_requested_model_usable_is_an_error(string failure)
    {
        switch (failure)
        {
            case "script":
                Lab.StartSucceeds = false;
                break;
            case "probe":
                Lab.ProbeFails = true;
                break;
            default:
                Lab.RelayProtocol = 2;
                break;
        }

        await Controller.StartAsync("nemotron", None);
        await _harness.WaitForIdleAsync();

        Assert.Equal(1, Lab.StartCalls);
        Assert.Equal(Error("nemotron", FixedTexts.ModelStartFailed), _harness.Events.Statuses[^1]);
        Assert.Empty(_harness.Events.OfType<Diagnostic>());
    }

    [Fact]
    public async Task A_start_script_that_succeeds_while_another_model_stays_loaded_is_an_error()
    {
        using var harness = new LocalModelHarness(runtime: new StubbornRuntime("qwen3"));

        var command = await harness.Controller.StartAsync("nemotron", None);
        await harness.WaitForIdleAsync();

        Assert.True(command.Began);
        Assert.Equal(Error("nemotron", FixedTexts.ModelStartFailed), harness.Events.Statuses[^1]);

        // The next probe is usable (the other model) and wins over the latch.
        Assert.Equal(Ready("qwen3"), await harness.Controller.GetStatusAsync(None));
    }

    [Fact]
    public async Task A_failed_stop_is_an_error_with_the_stop_message()
    {
        Lab.LoadedModel = "nemotron";
        Lab.StopSucceeds = false;

        await Controller.StopAsync("nemotron", None);
        await _harness.WaitForIdleAsync();
        Lab.ProbeFails = true;
        var status = await Controller.GetStatusAsync(None);

        Assert.Equal(Error("nemotron", FixedTexts.ModelStopFailed), status);
        Assert.Equal("Could not stop the PC model. Stop any active lab test and retry.", status.Message);
        Assert.Equal(Error("nemotron", FixedTexts.ModelStopFailed), _harness.Events.Statuses[^1]);
    }

    [Fact]
    public async Task The_latch_expires()
    {
        await FailAStartAsync();

        _harness.Clock.Advance(ProtocolConstants.ModelErrorLatch - TimeSpan.FromMilliseconds(1));
        var justBefore = await Controller.GetStatusAsync(None);
        _harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var atTheLimit = await Controller.GetStatusAsync(None);
        var afterwards = await Controller.GetStatusAsync(None);

        Assert.Equal(TimeSpan.FromSeconds(120), ProtocolConstants.ModelErrorLatch);
        Assert.Equal(Error("nemotron", FixedTexts.ModelStartFailed), justBefore);
        Assert.Equal(Stopped(), atTheLimit);
        Assert.Equal(Stopped(), afterwards);
    }

    [Fact]
    public async Task A_new_start_clears_the_latch_at_once()
    {
        await FailAStartAsync();
        Lab.StartThrowsPrivateText = false;
        var gate = Lab.StartGate = LocalModelHarness.NewGate();

        var command = await Controller.StartAsync("nemotron", None);
        var during = await Controller.GetStatusAsync(None);

        Assert.True(command.Began);
        Assert.Equal(Starting("nemotron", 0), command.Status);
        Assert.Equal(Starting("nemotron", 0), during);

        // The new operation succeeds; when the lab later disappears, no error is left over from the first one,
        // although far less than C-48 has passed since it failed.
        Lab.ProbeFails = false;
        gate.SetResult();
        await _harness.WaitForIdleAsync();
        Assert.Equal(Ready("nemotron"), await Controller.GetStatusAsync(None));
        Lab.ProbeFails = true;
        Assert.Equal(Stopped(), await Controller.GetStatusAsync(None));
    }

    [Fact]
    public async Task A_usable_probe_clears_the_latch()
    {
        await FailAStartAsync();

        Lab.ProbeFails = false;
        Lab.LoadedModel = "qwen3";
        var usable = await Controller.GetStatusAsync(None);
        Lab.ProbeFails = true;
        var gone = await Controller.GetStatusAsync(None);

        Assert.Equal(Ready("qwen3"), usable);
        Assert.Equal(Stopped(), gone);
    }

    [Fact]
    public async Task A_probe_that_is_not_usable_is_stopped()
    {
        Lab.LoadedModel = "nemotron";
        Lab.RelayProtocol = 2;
        var wrongProtocol = await Controller.GetStatusAsync(None);

        Lab.RelayProtocol = ProtocolConstants.LabRelayProtocol;
        Lab.LoadedModel = "whisper";
        var unknownModel = await Controller.GetStatusAsync(None);

        Assert.Equal(Stopped(), wrongProtocol);
        Assert.Equal(Stopped(), unknownModel);
    }

    [Fact]
    public async Task Busy_is_the_labs_session_or_the_held_lease()
    {
        Lab.LoadedModel = "nemotron";
        Lab.Busy = true;
        var labBusy = await Controller.GetStatusAsync(None);

        Lab.Busy = false;
        LocalModelStatus leaseHeld;
        LocalModelStatus stoppedWithLease;
        using (_harness.Lease.TryAcquire())
        {
            leaseHeld = await Controller.GetStatusAsync(None);
            Lab.LoadedModel = null;
            stoppedWithLease = await Controller.GetStatusAsync(None);
        }

        var inUse = new LocalModelStatus(LocalModelState.Ready, "nemotron", Busy: true, ElapsedSeconds: 0, FixedTexts.ModelInUse);
        Assert.Equal(inUse, labBusy);
        Assert.Equal(inUse, leaseHeld);
        Assert.Equal("Model is in use. Stop the conversation or lab test before unloading.", inUse.Message);
        Assert.Equal(Stopped() with { Busy = true }, stoppedWithLease);
        Assert.Equal(Stopped(), await Controller.GetStatusAsync(None));
    }

    [Fact]
    public async Task A_held_lease_refuses_commands_before_any_probe()
    {
        Lab.LoadedModel = "nemotron";
        using var lease = _harness.Lease.TryAcquire();

        var start = await Controller.StartAsync("qwen3", None);
        var stop = await Controller.StopAsync("nemotron", None);

        Assert.Equal(RelayErrors.ModelBusy, start.Refusal);
        Assert.Equal(RelayErrors.ModelBusy, stop.Refusal);
        Assert.Equal(0, Lab.ProbeCalls);
        Assert.Equal(0, Lab.StartCalls);
        Assert.Equal(0, Lab.StopCalls);
        Assert.Empty(_harness.Events.Snapshot());
    }

    [Theory]
    [InlineData("ready-when-loaded", nameof(PrepareOutcome.Ready), 0)]
    [InlineData("ready-after-load", nameof(PrepareOutcome.Ready), 1)]
    [InlineData("ready-after-switch", nameof(PrepareOutcome.Ready), 1)]
    [InlineData("busy", nameof(PrepareOutcome.Busy), 0)]
    [InlineData("operation-in-progress", nameof(PrepareOutcome.OperationInProgress), 1)]
    [InlineData("load-failed", nameof(PrepareOutcome.LoadFailed), 1)]
    public async Task Prepare_outcomes(string situation, string expectedOutcome, int expectedStarts)
    {
        var expected = Enum.Parse<PrepareOutcome>(expectedOutcome);

        // The stream that prepares holds the lease (step S-6 comes before the prepare).
        using var lease = _harness.Lease.TryAcquire();
        TaskCompletionSource? gate = null;
        switch (situation)
        {
            case "ready-when-loaded":
                Lab.LoadedModel = "nemotron";
                break;
            case "ready-after-switch":
                Lab.LoadedModel = "qwen3";
                break;
            case "busy":
                Lab.LoadedModel = "qwen3";
                Lab.Busy = true;
                break;
            case "operation-in-progress":
                lease?.Dispose();
                gate = Lab.StartGate = LocalModelHarness.NewGate();
                await Controller.StartAsync("qwen3", None);
                break;
            case "load-failed":
                Lab.StartSucceeds = false;
                break;
            default:
                break;
        }

        var outcome = await Controller.PrepareAsync("nemotron", None);

        Assert.Equal(expected, outcome);
        gate?.SetResult();
        await _harness.WaitForIdleAsync();
        Assert.Equal(expectedStarts, Lab.StartCalls);
        Assert.Equal(0, Lab.StopCalls);
        if (situation is "ready-after-load" or "ready-after-switch")
        {
            // The load a prepare begins is an ordinary operation: it is announced like any other.
            Assert.Equal([LocalModelState.Starting, LocalModelState.Ready], _harness.Events.Statuses.Select(status => status.State));
            Assert.Equal(["nemotron"], Lab.StartedModels);
        }
    }

    [Fact]
    public async Task Cancelling_a_prepare_wait_does_not_cancel_the_operation()
    {
        var gate = Lab.StartGate = LocalModelHarness.NewGate();
        using var leaving = new CancellationTokenSource();

        var prepare = Controller.PrepareAsync("nemotron", leaving.Token);
        await Waiting.UntilAsync(() => Lab.StartCalls == 1);
        await leaving.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepare);
        Assert.True(Controller.OperationRunning);
        Assert.Equal(Starting("nemotron", 0), await Controller.GetStatusAsync(None));
        Assert.Equal(RelayErrors.ModelOperationInProgress, (await Controller.StopAsync("nemotron", None)).Refusal);

        gate.SetResult();
        await _harness.WaitForIdleAsync();
        Assert.Equal(Ready("nemotron"), await Controller.GetStatusAsync(None));
        Assert.Equal(1, Lab.StartCalls);
        Assert.Equal([Starting("nemotron", 0), Ready("nemotron")], _harness.Events.Statuses);
    }

    [Fact]
    public async Task Two_prepares_for_the_same_load_cannot_both_begin_it()
    {
        var gate = Lab.StartGate = LocalModelHarness.NewGate();

        var first = Controller.PrepareAsync("nemotron", None);
        await Waiting.UntilAsync(() => Lab.StartCalls == 1);
        var second = await Controller.PrepareAsync("nemotron", None);
        gate.SetResult();

        Assert.Equal(PrepareOutcome.OperationInProgress, second);
        Assert.Equal(PrepareOutcome.Ready, await first);
        Assert.Equal(1, Lab.StartCalls);
    }

    [Fact]
    public async Task A_relay_that_stops_abandons_the_wait_and_leaves_the_script_alone()
    {
        var runtime = new HangingRuntime();
        using var harness = new LocalModelHarness(runtime: runtime);

        var command = await harness.Controller.StartAsync("nemotron", None);
        var started = await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Access.BeginStopping();
        await Waiting.UntilAsync(() => !harness.Controller.OperationRunning);

        Assert.True(command.Began);

        // The script was never given a token that the relay's stop could fire, and it is still running.
        Assert.False(started.CanBeCanceled);
        Assert.False(runtime.Script.Task.IsCompleted);

        // Nothing is latched and nothing more is announced.
        Assert.Equal([LocalModelState.Starting], harness.Events.Statuses.Select(status => status.State));
        Assert.Empty(harness.Events.OfType<Diagnostic>());
        runtime.Script.SetResult(true);
    }

    [Fact]
    public async Task A_runtime_that_throws_on_a_probe_is_no_lab_and_never_fails_the_status()
    {
        using var harness = new LocalModelHarness(runtime: new ThrowingProbeRuntime());

        var status = await harness.Controller.GetStatusAsync(None);
        var start = await harness.Controller.StartAsync("nemotron", None);
        await harness.WaitForIdleAsync();

        Assert.Equal(Stopped(), status);
        Assert.True(start.Began);
        Assert.Equal(LocalModelState.Error, harness.Events.Statuses[^1].State);
        Assert.All(
            harness.Events.Snapshot(),
            relayEvent => Assert.DoesNotContain("PRIVATE", relayEvent.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_cancelled_caller_gets_a_cancellation_and_nothing_begins()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller.GetStatusAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller.StartAsync("nemotron", cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller.StopAsync("nemotron", cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller.PrepareAsync("nemotron", cancelled.Token));

        Assert.False(Controller.OperationRunning);
        Assert.Equal(0, Lab.StartCalls);
        Assert.Equal(Stopped(), await Controller.GetStatusAsync(None));
    }

    private async Task FailAStartAsync()
    {
        Lab.ProbeFails = true;
        Lab.StartThrowsPrivateText = true;
        await Controller.StartAsync("nemotron", None);
        await _harness.WaitForIdleAsync();
        Assert.Equal(Error("nemotron", FixedTexts.ModelStartFailed), await Controller.GetStatusAsync(None));
    }

    private static LocalModelStatus Stopped() =>
        new(LocalModelState.Stopped, Model: null, Busy: false, ElapsedSeconds: 0, FixedTexts.ModelStopped);

    private static LocalModelStatus Starting(string model, int elapsedSeconds) =>
        new(LocalModelState.Starting, model, Busy: false, elapsedSeconds, FixedTexts.ModelStarting);

    private static LocalModelStatus Ready(string model) =>
        new(LocalModelState.Ready, model, Busy: false, ElapsedSeconds: 0, FixedTexts.ModelReady);

    private static LocalModelStatus Error(string model, string message) =>
        new(LocalModelState.Error, model, Busy: false, ElapsedSeconds: 0, message);

    /// <summary>A runtime whose start script runs until the test ends it, and that records the token it was given.</summary>
    private sealed class HangingRuntime : ILabRuntime
    {
        public TaskCompletionSource<CancellationToken> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Script { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LabHealth?> ProbeAsync(CancellationToken ct) => Task.FromResult<LabHealth?>(null);

        public Task<bool> StartAsync(string model, CancellationToken ct)
        {
            Started.TrySetResult(ct);
            return Script.Task;
        }

        public Task<bool> StopAsync(CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>A runtime whose scripts report success and whose lab keeps serving one model whatever is asked.</summary>
    private sealed class StubbornRuntime(string loadedModel) : ILabRuntime
    {
        public Task<LabHealth?> ProbeAsync(CancellationToken ct) => Task.FromResult<LabHealth?>(
            new LabHealth(Ready: true, loadedModel, Busy: false, ProtocolConstants.LabRelayProtocol));

        public Task<bool> StartAsync(string model, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> StopAsync(CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>A runtime that breaks its contract: the probe throws with a private text.</summary>
    private sealed class ThrowingProbeRuntime : ILabRuntime
    {
        public Task<LabHealth?> ProbeAsync(CancellationToken ct) =>
            Task.FromException<LabHealth?>(new InvalidOperationException(FakeLabRuntime.PrivateText));

        public Task<bool> StartAsync(string model, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> StopAsync(CancellationToken ct) => Task.FromResult(true);
    }
}
