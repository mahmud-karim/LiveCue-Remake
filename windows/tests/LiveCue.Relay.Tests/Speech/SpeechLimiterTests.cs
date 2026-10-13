using System.Net.WebSockets;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-113 through a real stream: more than <c>160000 + 35 × t</c> bytes within <c>t</c>
/// milliseconds of <c>ready</c> ends the stream with AUDIO_LIMIT, and audio at real-time pace never does.
/// </summary>
public sealed class SpeechLimiterTests
{
    private const int Frame = ProtocolConstants.AudioFrameBytes;

    [Fact]
    public async Task A_burst_beyond_the_allowance_fails_and_real_time_audio_never_does()
    {
        await using var rig = await SpeechRig.StartAsync();

        // A burst at the moment of "ready": 62 frames are 158,720 bytes, the 63rd would make 161,280.
        using (var burst = await rig.OpenAdmittedAsync())
        {
            await burst.ExpectAsync("ready");
            for (var sent = 0; sent < 62; sent++)
            {
                await burst.SendAudioAsync(Frame);
                await burst.ExpectAsync("transcript");
            }

            await burst.SendAudioAsync(Frame);
            var ending = await SpeechFrameRuleTests.AssertEndsAsync(
                burst, "AUDIO_LIMIT", WebSocketCloseStatus.InternalServerError);
            Assert.Equal(
                """{"type":"error","code":"AUDIO_LIMIT","message":"Audio exceeded the live-stream buffer limit."}""",
                ending.Frames[^1]);
            Assert.Equal(62 * Frame, rig.Lab.Current.AudioBytes);
        }

        // One second after "ready" the allowance is 195,000 bytes: 76 frames pass, the 77th does not.
        using (var later = await rig.OpenAdmittedAsync())
        {
            await later.ExpectAsync("ready");
            rig.Clock.Advance(TimeSpan.FromMilliseconds(1000));
            for (var sent = 0; sent < 76; sent++)
            {
                await later.SendAudioAsync(Frame);
                await later.ExpectAsync("transcript");
            }

            await later.SendAudioAsync(Frame);
            await SpeechFrameRuleTests.AssertEndsAsync(later, "AUDIO_LIMIT", WebSocketCloseStatus.InternalServerError);
            Assert.Equal(76 * Frame, rig.Lab.Current.AudioBytes);
        }

        // Real time: one 80 ms frame every 80 ms, for 60 seconds. Never refused, and every byte arrives.
        using (var paced = await rig.OpenAdmittedAsync())
        {
            await paced.ExpectAsync("ready");
            const int Frames = 60_000 / 80;
            for (var sent = 0; sent < Frames; sent++)
            {
                rig.Clock.Advance(TimeSpan.FromMilliseconds(80));
                await paced.SendAudioAsync(Frame);
                await paced.ExpectAsync("transcript");
            }

            await paced.SendEndStreamAsync();
            var ending = await paced.ReadToEndAsync();
            Assert.Empty(ending.Errors);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
            Assert.Equal(Frames * Frame, rig.Lab.Current.AudioBytes);
        }
    }

    [Fact]
    public async Task Tiny_frames_are_counted_by_their_bytes_like_any_other()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.AutoRespond = false;
        using var phone = await rig.OpenReadyAsync();

        // A flood of the smallest frame there is. The allowance is the same 160,000 bytes: 80,000 frames
        // pass and the next one ends the stream.
        const int Allowed = ProtocolConstants.AllowanceBaseBytes / 2;
        var tiny = new byte[2];
        for (var sent = 0; sent < Allowed; sent++)
        {
            await phone.SendAudioAsync(tiny);
        }

        // The allowed frames have all reached the lab before the one too many is sent: a stream that ends
        // drops what it has accepted and not yet written, so counting at the lab afterwards would depend
        // on how far the writer had got.
        await SpeechWait.UntilAsync(() => rig.Lab.Current.AudioBytes == ProtocolConstants.AllowanceBaseBytes, "the lab has every allowed byte");
        await phone.SendAudioAsync(tiny);

        await SpeechFrameRuleTests.AssertEndsAsync(phone, "AUDIO_LIMIT", WebSocketCloseStatus.InternalServerError);
        Assert.Equal(ProtocolConstants.AllowanceBaseBytes, rig.Lab.Current.AudioBytes);
        Assert.Equal(Allowed, rig.Lab.Current.AudioFrames.Count);
    }
}
