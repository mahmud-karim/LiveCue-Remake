using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-116: until <c>ready</c>, a <c>loading</c> frame arrives every C-50, in each of the three
/// states in which the model is being prepared, with seconds counted from the upgrade.
/// </summary>
public sealed class SpeechLoadingTests
{
    [Fact]
    public async Task Loading_frames_repeat_with_non_decreasing_seconds()
    {
        await using var rig = await SpeechRig.StartAsync();
        var prepare = rig.Model.PrepareGate = NewGate();
        var connect = rig.Lab.ConnectGate = NewGate();
        rig.Lab.FirstMessage = string.Empty;
        using var phone = await rig.OpenAsync("qwen3");
        var seconds = new List<int>();

        // Right after admission, before any time has passed.
        Assert.Equal(
            """{"type":"loading","provider":"qwen3","elapsedSeconds":0,"message":"Loading Qwen3 on PC · 0 s. Microphone starts when ready."}""",
            await phone.ReceiveTextAsync());
        seconds.Add(0);

        // While the model is prepared.
        await SpeechWait.UntilAsync(() => rig.Model.PrepareCalls == 1, "the prepare began");
        seconds.Add(await NextLoadingAsync(rig, phone));
        seconds.Add(await NextLoadingAsync(rig, phone));

        // Half an interval is not an interval.
        rig.Clock.Advance(ProtocolConstants.LoadingFrameInterval / 2);
        seconds.Add(await NextLoadingAsync(rig, phone, ProtocolConstants.LoadingFrameInterval / 2));

        // While the lab socket is opened.
        prepare.SetResult();
        await SpeechWait.UntilAsync(() => rig.Lab.ConnectCalls == 1, "the connect began");
        seconds.Add(await NextLoadingAsync(rig, phone));

        // While the lab's first message is awaited.
        connect.SetResult();
        await SpeechWait.UntilAsync(() => rig.Lab.Sockets.Count == 1, "the lab socket is open");
        Assert.Equal(
            """{"type":"loading","provider":"qwen3","elapsedSeconds":10,"message":"Loading Qwen3 on PC · 10 s. Microphone starts when ready."}""",
            await NextLoadingTextAsync(rig, phone, ProtocolConstants.LoadingFrameInterval));
        seconds.Add(10);

        Assert.Equal([0, 2, 4, 6, 8, 10], seconds);

        // "ready" ends it: the handshake time is counted from the upgrade, and no "loading" follows.
        rig.Lab.Current.Enqueue("""{"type":"ready","model":"qwen3","sampleRate":16000}""");
        Assert.Equal(
            """{"type":"ready","provider":"qwen3","sampleRate":16000,"handshakeMs":10000}""",
            await phone.ReceiveTextAsync());
        rig.Clock.Advance(ProtocolConstants.LoadingFrameInterval * 3);
        await phone.SendAudioAsync(2560);
        await phone.ExpectAsync("transcript");
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<int> NextLoadingAsync(SpeechRig rig, SpeechPhone phone, TimeSpan? after = null)
    {
        rig.Clock.Advance(after ?? ProtocolConstants.LoadingFrameInterval);
        var frame = await phone.ExpectAsync("loading");
        Assert.Equal("qwen3", frame.GetProperty("provider").GetString());
        return frame.GetProperty("elapsedSeconds").GetInt32();
    }

    private static async Task<string?> NextLoadingTextAsync(SpeechRig rig, SpeechPhone phone, TimeSpan after)
    {
        rig.Clock.Advance(after);
        return await phone.ReceiveTextAsync();
    }
}
