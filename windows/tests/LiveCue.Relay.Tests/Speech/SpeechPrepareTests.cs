using System.Net;
using System.Net.WebSockets;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-105 and P2-106, and the prepare rows of W-design 4.11: what a stream does with each
/// outcome of the prepare, and what happens to the lease when the phone leaves during a load.
/// </summary>
public sealed class SpeechPrepareTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failed_prepare_is_model_load_failed_without_private_text(bool throws)
    {
        await using var rig = await SpeechRig.StartAsync(model =>
        {
            model.PrepareThrowsPrivateText = throws;
            model.PrepareResult = PrepareOutcome.LoadFailed;
        });
        using var phone = await rig.OpenAsync();

        var ending = await phone.ReadToEndAsync();

        Assert.Equal(["loading", "error"], ending.Types);
        Assert.Equal(
            """{"type":"error","code":"MODEL_LOAD_FAILED","message":"Could not load the PC model."}""",
            ending.Frames[1]);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, ending.Status);
        Assert.Equal("MODEL_LOAD_FAILED", ending.Reason);
        Assert.Equal(0, rig.Lab.ConnectCalls);
        Assert.DoesNotContain(ending.Frames, frame => frame.Contains("private diagnostic", StringComparison.Ordinal));

        // The desktop learns the code and the type of what went wrong, never its text.
        await rig.Events.SettleAsync();
        Assert.DoesNotContain(
            rig.Events.Snapshot(),
            relayEvent => relayEvent.ToString().Contains("private diagnostic", StringComparison.Ordinal));
        var failed = Assert.Single(rig.Events.OfType<SpeechStreamChanged>(), changed => changed.Phase == SpeechStreamPhase.Failed);
        Assert.Equal("MODEL_LOAD_FAILED", failed.Code);
        Assert.Equal("Could not load the PC model.", failed.Message);
    }

    [Theory]
    [InlineData("Busy", "MODEL_BUSY")]
    [InlineData("OperationInProgress", "MODEL_OPERATION_IN_PROGRESS")]
    [InlineData("LoadFailed", "MODEL_LOAD_FAILED")]
    public async Task Each_refusing_outcome_is_its_code_with_1011_and_frees_the_lease_at_once(string outcome, string code)
    {
        await using var rig = await SpeechRig.StartAsync(
            model => model.PrepareResult = Enum.Parse<PrepareOutcome>(outcome));
        using var phone = await rig.OpenAsync();
        await phone.ExpectAsync("loading");
        var error = await phone.ExpectAsync("error");
        Assert.Equal(code, error.GetProperty("code").GetString());

        // The close handshake of the failed stream is still open: the phone has not answered. The lease
        // was released when the prepare failed, so the next stream is admitted without waiting for it.
        rig.Model.PrepareResult = PrepareOutcome.Ready;
        using var next = await rig.OpenReadyAsync();

        var ending = await phone.ReadToEndAsync();
        Assert.Equal(WebSocketCloseStatus.InternalServerError, ending.Status);
        Assert.Equal(code, ending.Reason);
        Assert.Equal(1, rig.Lab.ConnectCalls);
    }

    [Fact]
    public async Task A_load_in_progress_refuses_the_next_stream_and_a_later_one_is_admitted()
    {
        await using var rig = await SpeechRig.StartAsync();
        var load = rig.Model.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The first phone starts a load, sees "loading" and leaves.
        using (var first = await rig.OpenAsync())
        {
            await first.ExpectAsync("loading");
            rig.Model.OperationRunning = true;
            rig.Model.Status = new LocalModelStatus(
                LocalModelState.Starting, "nemotron", Busy: false, ElapsedSeconds: 3, FixedTexts.ModelStarting);
        }

        // The load goes on as an ordinary operation: route 4 shows it, and a second attempt is refused at S-5.
        Assert.Equal("MODEL_OPERATION_IN_PROGRESS 1008", await rig.AttemptAsync());
        using (var status = await rig.Host.GetAsync("/v1/local-model"))
        {
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.Contains("\"state\":\"starting\"", await status.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(1, rig.Model.PrepareCalls);
        Assert.Equal(0, rig.Lab.ConnectCalls);

        // The load completes. A third attempt is admitted: the first stream's lease was released when it left.
        rig.Model.OperationRunning = false;
        rig.Model.Status = new LocalModelStatus(
            LocalModelState.Ready, "nemotron", Busy: false, ElapsedSeconds: 0, FixedTexts.ModelReady);
        rig.Model.PrepareGate = null;
        load.SetResult();
        using var third = await rig.OpenAdmittedAsync();
        await third.ExpectAsync("ready");

        // The stream that left never reached the lab.
        Assert.Equal(2, rig.Model.PrepareCalls);
        Assert.Equal(1, rig.Lab.ConnectCalls);
    }

    [Fact]
    public async Task A_phone_that_leaves_during_the_load_gives_the_lease_back_before_the_load_ends()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Model.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var first = await rig.OpenAsync())
        {
            await first.ExpectAsync("loading");
        }

        // The load never finishes in this test, and still the lease is free (decision A-07).
        using var second = await rig.OpenAdmittedAsync();

        await rig.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Stopped);
        Assert.Equal(0, rig.Lab.ConnectCalls);
    }

    [Fact]
    public async Task A_phone_that_is_gone_when_the_prepare_finishes_never_reaches_the_lab()
    {
        await using var rig = await SpeechRig.StartAsync();
        var load = rig.Model.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var first = await rig.OpenAsync())
        {
            await first.ExpectAsync("loading");
        }

        await rig.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Stopped);
        load.SetResult();
        await rig.Events.SettleAsync();

        // Protocol 10.4 step 3: the relay releases the lease and stops. It does not contact the lab.
        Assert.Equal(0, rig.Lab.ConnectCalls);
    }
}
