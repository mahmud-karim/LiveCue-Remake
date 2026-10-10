using System.Net.WebSockets;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-114, the time limits of protocol 10.5, all on the manual clock: C-51 (model load), C-52
/// (no audio), C-53 (finalization) and C-54 (stream length). Each limit is shown not to fire one
/// millisecond early.
/// </summary>
public sealed class SpeechWatchdogTests
{
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(1);

    [Theory]
    [InlineData("preparing")]
    [InlineData("connecting")]
    [InlineData("awaiting-lab-ready")]
    public async Task Load_limit(string state)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);

        // Up to the last "loading" before the limit, the stream only reports its progress.
        var steps = (int)(ProtocolConstants.ModelLoadLimit / ProtocolConstants.LoadingFrameInterval) - 1;
        for (var step = 1; step <= steps; step++)
        {
            rig.Clock.Advance(ProtocolConstants.LoadingFrameInterval);
            var loading = await phone.ExpectAsync("loading");
            Assert.Equal(2 * step, loading.GetProperty("elapsedSeconds").GetInt32());
        }

        // One millisecond before C-51 the stream is still alive: it holds the lease.
        rig.Clock.Advance(ProtocolConstants.LoadingFrameInterval - Moment);
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        rig.Clock.Advance(Moment);
        var frames = new List<string>();
        string? frame;
        while ((frame = await phone.ReceiveTextAsync()) is not null && !frame.Contains("\"error\"", StringComparison.Ordinal))
        {
            frames.Add(frame);
        }

        Assert.Equal(
            """{"type":"error","code":"MODEL_LOAD_TIMEOUT","message":"Model is still loading."}""",
            frame);
        Assert.All(frames, earlier => Assert.Contains("\"loading\"", earlier, StringComparison.Ordinal));

        // No "ready" was ever sent, and a lab socket that was open is disposed.
        Assert.All(rig.Lab.Sockets.ToList(), lab => Assert.True(lab.Disposed));

        if (state == "preparing")
        {
            // The load itself goes on. The lease does not wait for it, nor for the close handshake.
            rig.Model.PrepareGate = null;
            using var next = await rig.OpenAdmittedAsync();
        }

        var ending = await phone.ReadToEndAsync();
        Assert.Empty(ending.Frames);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, ending.Status);
        Assert.Equal("MODEL_LOAD_TIMEOUT", ending.Reason);
    }

    [Fact]
    public async Task Idle_limit_and_not_after_end_stream()
    {
        await using var rig = await SpeechRig.StartAsync();

        // No audio for C-52 ends the stream. An audio frame starts the count again.
        using (var idle = await rig.OpenAdmittedAsync())
        {
            await idle.ExpectAsync("ready");
            rig.Clock.Advance(ProtocolConstants.AudioIdleLimit - Moment);
            await idle.SendAudioAsync(2560);
            await idle.ExpectAsync("transcript");
            rig.Clock.Advance(ProtocolConstants.AudioIdleLimit - Moment);
            await idle.SendAudioAsync(2560);
            await idle.ExpectAsync("transcript");

            rig.Clock.Advance(ProtocolConstants.AudioIdleLimit);
            var ending = await SpeechFrameRuleTests.AssertEndsAsync(
                idle, "AUDIO_IDLE_TIMEOUT", WebSocketCloseStatus.InternalServerError);
            Assert.Equal(
                """{"type":"error","code":"AUDIO_IDLE_TIMEOUT","message":"Audio stream timed out."}""",
                ending.Frames[^1]);
        }

        // An empty frame is not audio: it does not start the count again.
        using (var empty = await rig.OpenAdmittedAsync())
        {
            await empty.ExpectAsync("ready");
            rig.Clock.Advance(ProtocolConstants.AudioIdleLimit - Moment);
            await empty.SendAudioAsync(0);
            await Task.Delay(50);
            rig.Clock.Advance(Moment);
            await SpeechFrameRuleTests.AssertEndsAsync(empty, "AUDIO_IDLE_TIMEOUT", WebSocketCloseStatus.InternalServerError);
        }

        // After "endStream" the idle limit no longer applies: 19 s of silence, the command, 11 s more.
        rig.Lab.AutoRespond = false;
        using (var finalizing = await rig.OpenAdmittedAsync())
        {
            await finalizing.ExpectAsync("ready");
            rig.Clock.Advance(ProtocolConstants.AudioIdleLimit - TimeSpan.FromSeconds(1));
            await finalizing.SendEndStreamAsync();
            await SpeechWait.UntilAsync(() => rig.Lab.Current.EndStreams == 1, "the lab got endStream");
            rig.Clock.Advance(ProtocolConstants.FinalizeLimit - TimeSpan.FromSeconds(1));

            rig.Lab.Current.Enqueue("""{"type":"streamComplete","audioProcessedMs":0,"finalizationMs":11000}""");
            var ending = await finalizing.ReadToEndAsync();
            Assert.Equal(["""{"type":"streamComplete","audioProcessedMs":0,"finalizationMs":11000}"""], ending.Frames);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
        }
    }

    [Fact]
    public async Task Finalize_limit()
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, "finalizing");

        // The lab still sends its last words, but no "streamComplete".
        rig.Lab.Current.Enqueue("""{"type":"speechComplete","turnId":1,"transcript":"Last words"}""");
        await phone.ExpectAsync("speechComplete");
        rig.Clock.Advance(ProtocolConstants.FinalizeLimit - Moment);
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        rig.Clock.Advance(Moment);
        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "FINALIZE_TIMEOUT", WebSocketCloseStatus.InternalServerError);

        Assert.Equal(
            ["""{"type":"error","code":"FINALIZE_TIMEOUT","message":"Local model did not finalize in time."}"""],
            ending.Frames);
        await SpeechWait.UntilAsync(() => rig.Lab.Current.Disposed, "the lab socket is disposed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Session_limit(bool whileFinalizing)
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.AutoRespond = false;
        using var phone = await rig.OpenReadyAsync();
        var lab = rig.Lab.Current;

        // Audio keeps arriving, so the idle limit never fires: a frame every 15 s up to 10 s before C-54.
        var step = TimeSpan.FromSeconds(15);
        var steps = (int)(ProtocolConstants.StreamLimit / step) - 1;
        for (var sent = 1; sent <= steps; sent++)
        {
            rig.Clock.Advance(step);
            await phone.SendAudioAsync(2);
            await SpeechWait.UntilAsync(() => lab.AudioFrames.Count == sent, "the audio reached the lab");
        }

        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        if (whileFinalizing)
        {
            await phone.SendEndStreamAsync();
            await SpeechWait.UntilAsync(() => lab.EndStreams == 1, "the lab got endStream");
        }

        // One millisecond before C-54 the stream is alive; at C-54 it ends, also while it finalizes.
        rig.Clock.Advance(TimeSpan.FromSeconds(10) - Moment);
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());
        rig.Clock.Advance(Moment);

        var ending = await SpeechFrameRuleTests.AssertEndsAsync(
            phone, "SESSION_LIMIT", WebSocketCloseStatus.InternalServerError);
        Assert.Equal(
            ["""{"type":"error","code":"SESSION_LIMIT","message":"30-minute session limit reached."}"""],
            ending.Frames);
    }
}
