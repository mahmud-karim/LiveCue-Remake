using System.Net.WebSockets;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-111 and the single rule of protocol 10.5: any phone frame before <c>ready</c> or after
/// <c>endStream</c> ends the stream with AUDIO_OUT_OF_SEQUENCE, whatever the frame says.
/// </summary>
public sealed class SpeechSequenceTests
{
    public static TheoryData<string, string> Cases()
    {
        var cases = new TheoryData<string, string>();
        foreach (var state in new[] { "preparing", "connecting", "awaiting-lab-ready", "finalizing" })
        {
            foreach (var frame in new[] { "audio", "empty-audio", "end-stream", "unknown-text", "not-json" })
            {
                cases.Add(state, frame);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_frame_before_ready_or_after_end_stream_is_out_of_sequence(string state, string frame)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);

        switch (frame)
        {
            case "audio":
                await phone.SendAudioAsync(2560);
                break;
            case "empty-audio":
                await phone.SendAudioAsync(0);
                break;
            case "end-stream":
                await phone.SendEndStreamAsync();
                break;
            case "unknown-text":
                await phone.SendTextAsync("""{"type":"somethingNew"}""");
                break;
            default:
                await phone.SendTextAsync("stop");
                break;
        }

        var ending = await phone.ReadToEndAsync();

        var error = Assert.Single(ending.Errors);
        Assert.Equal("AUDIO_OUT_OF_SEQUENCE", error.GetProperty("code").GetString());
        Assert.Equal(
            """{"type":"error","code":"AUDIO_OUT_OF_SEQUENCE","message":"Audio arrived outside an active transcription stream."}""",
            ending.Frames[^1]);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, ending.Status);
        Assert.Equal("AUDIO_OUT_OF_SEQUENCE", ending.Reason);

        // Nothing the phone sent out of sequence reached the lab.
        Assert.All(rig.Lab.Sockets, lab => Assert.Equal(0, lab.AudioBytes));
        Assert.All(rig.Lab.Sockets, lab => Assert.InRange(lab.EndStreams, 0, state == "finalizing" ? 1 : 0));
    }
}

/// <summary>Brings a stream into one state of W-design 4.11 and holds it there.</summary>
internal static class SpeechStates
{
    /// <summary>
    /// <c>preparing</c>, <c>connecting</c>, <c>awaiting-lab-ready</c>, <c>streaming</c> or
    /// <c>finalizing</c>. The first <c>loading</c> frame has been read; for the last two, <c>ready</c> too.
    /// In <c>finalizing</c> the lab does not answer by itself.
    /// </summary>
    public static async Task<SpeechPhone> ReachAsync(SpeechRig rig, string state, string model = "nemotron")
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        switch (state)
        {
            case "preparing":
                rig.Model.PrepareGate = hold;
                break;
            case "connecting":
                rig.Lab.ConnectGate = hold;
                break;
            case "awaiting-lab-ready":
                rig.Lab.FirstMessage = string.Empty;
                break;
            case "finalizing":
                rig.Lab.AutoRespond = false;
                break;
            case "streaming":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        var phone = await rig.OpenAsync(model);
        try
        {
            await phone.ExpectAsync("loading");
            switch (state)
            {
                case "preparing":
                    await SpeechWait.UntilAsync(() => rig.Model.PrepareCalls == 1, "the prepare began");
                    break;
                case "connecting":
                    await SpeechWait.UntilAsync(() => rig.Lab.ConnectCalls == 1, "the connect began");
                    break;
                case "awaiting-lab-ready":
                    await SpeechWait.UntilAsync(() => rig.Lab.Sockets.Count == 1, "the lab socket is open");
                    break;
                case "finalizing":
                    await phone.ExpectAsync("ready");
                    await phone.SendEndStreamAsync();
                    await SpeechWait.UntilAsync(() => rig.Lab.Current.EndStreams == 1, "the lab got endStream");
                    break;
                default:
                    await phone.ExpectAsync("ready");
                    break;
            }

            return phone;
        }
        catch
        {
            phone.Dispose();
            throw;
        }
    }
}
