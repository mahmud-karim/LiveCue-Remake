using System.Net;
using System.Net.WebSockets;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// The speech bridge with the real PC model controller behind it (the lab itself is still the fake
/// runtime and the fake socket): the lease as the model routes see it, and a load that a stream began
/// and then left behind (decision A-07, protocol 10.4).
/// </summary>
public sealed class SpeechModelControlTests
{
    private const string InUse =
        """{"state":"ready","model":"nemotron","busy":true,"elapsedSeconds":0,"message":"Model is in use. Stop the conversation or lab test before unloading."}""";

    private const string Ready =
        """{"state":"ready","model":"nemotron","busy":false,"elapsedSeconds":0,"message":"Model loaded on the PC GPU. Ready for live transcription."}""";

    [Fact]
    public async Task A_stream_loads_the_model_and_the_status_is_busy_for_as_long_as_it_holds_the_lease()
    {
        await using var rig = await SpeechRig.StartWithRealModelControlAsync();
        var host = rig.Host;

        // Nothing is loaded: the stream's prepare starts the model, once, and waits for it.
        using (var phone = await rig.OpenReadyAsync())
        {
            Assert.Equal(["nemotron"], host.Lab.StartedModels);

            // While the stream holds the lease the status says "busy", and the model cannot be stopped.
            Assert.Equal(InUse, await StatusAsync(host));
            using (var stop = await host.PostAsync("/v1/local-model/stop", Bodies.LocalModel("nemotron")))
            {
                Assert.Equal(HttpStatusCode.Conflict, stop.StatusCode);
                Assert.Contains("MODEL_BUSY", await stop.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            await phone.SendAudioAsync(2560);
            await phone.ExpectAsync("transcript");
            await phone.SendEndStreamAsync();
            Assert.Equal(WebSocketCloseStatus.NormalClosure, (await phone.ReadToEndAsync()).Status);
        }

        // The stream is over: the lease is back, and the model stays loaded for the next one.
        using var next = await rig.OpenAdmittedAsync();
        await next.ExpectAsync("ready");
        Assert.Equal(1, host.Lab.StartCalls);
        Assert.Equal(0, host.Lab.StopCalls);
        next.Socket.Abort();
        await SpeechWait.UntilAsync(async () => await StatusAsync(host) == Ready, "the status is no longer busy");
    }

    [Fact]
    public async Task A_load_that_a_stream_began_goes_on_when_the_phone_leaves_and_the_lease_is_free_at_once()
    {
        await using var rig = await SpeechRig.StartWithRealModelControlAsync();
        var host = rig.Host;
        var load = host.Lab.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The first phone starts the load, sees "loading" and leaves.
        using (var first = await rig.OpenAsync())
        {
            await first.ExpectAsync("loading");
            await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Starting);
        }

        await host.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Stopped);

        // The load is now an ordinary operation: route 4 shows it, not busy, and the next stream is
        // refused because of the operation (S-5), not because of a lease (S-6).
        Assert.Contains("\"state\":\"starting\",\"model\":\"nemotron\",\"busy\":false", await StatusAsync(host), StringComparison.Ordinal);
        Assert.Equal("MODEL_OPERATION_IN_PROGRESS 1008", await rig.AttemptAsync());
        Assert.Equal(0, rig.Lab.ConnectCalls);

        // The load completes although nobody waits for it. The next stream finds the model loaded.
        load.SetResult();
        await host.Events.WaitForAsync<LocalModelChanged>(changed => changed.Status.State == LocalModelState.Ready);
        Assert.Equal(Ready, await StatusAsync(host));
        using var third = await rig.OpenAdmittedAsync();
        await third.ExpectAsync("ready");
        Assert.Equal(1, host.Lab.StartCalls);
        Assert.Equal(InUse, await StatusAsync(host));
    }

    [Fact]
    public async Task A_load_that_fails_is_model_load_failed_and_the_other_model_is_never_tried()
    {
        await using var rig = await SpeechRig.StartWithRealModelControlAsync();
        rig.Host.Lab.StartThrowsPrivateText = true;
        using var phone = await rig.OpenAsync("qwen3");

        var ending = await phone.ReadToEndAsync();

        Assert.Equal(["loading", "error"], ending.Types);
        Assert.Equal("MODEL_LOAD_FAILED", ending.Reason);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, ending.Status);
        Assert.DoesNotContain(ending.Frames, frame => frame.Contains(FakeLabRuntime.PrivateText, StringComparison.Ordinal));
        Assert.Equal(["qwen3"], rig.Host.Lab.StartedModels);
        Assert.Equal(0, rig.Lab.ConnectCalls);
    }

    private static async Task<string> StatusAsync(RelayTestHost host)
    {
        using var response = await host.GetAsync("/v1/local-model");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
