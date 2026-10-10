using System.Net.WebSockets;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-100 to P2-102: a stream from the upgrade to the clean close, against the fake lab socket
/// of protocol 15.6.
/// </summary>
public sealed class SpeechHappyPathTests
{
    [Theory]
    [InlineData("nemotron", "Nemotron")]
    [InlineData("qwen3", "Qwen3")]
    public async Task Each_model_is_accepted_loads_and_becomes_ready(string model, string label)
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.Model = model;
        using var phone = await rig.OpenAsync(model);

        var first = await phone.ReceiveTextAsync();
        var second = await phone.ReceiveTextAsync();

        // "provider" is the model id the phone sent, never the label: the phone compares it exactly.
        Assert.Equal(
            $$"""{"type":"loading","provider":"{{model}}","elapsedSeconds":0,"message":"Loading {{label}} on PC · 0 s. Microphone starts when ready."}""",
            first);
        Assert.Equal(
            $$"""{"type":"ready","provider":"{{model}}","sampleRate":16000,"handshakeMs":0}""",
            second);
        Assert.Equal(1, rig.Lab.ConnectCalls);
    }

    [Theory]
    [InlineData("nemotron")]
    [InlineData("qwen3")]
    public async Task Audio_reaches_the_lab_byte_for_byte_and_prepare_got_the_model(string model)
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.Model = model;
        using var phone = await rig.OpenReadyAsync(model);
        var audio = new byte[2560];
        for (var index = 0; index < audio.Length; index++)
        {
            audio[index] = (byte)(index * 7);
        }

        await phone.SendAudioAsync(new byte[2560]);
        await phone.ExpectAsync("transcript");
        await phone.SendAudioAsync(audio);
        await phone.ExpectAsync("transcript");
        await phone.SendEndStreamAsync();
        await phone.ReadToEndAsync();

        Assert.Equal([model], rig.Model.PreparedModels);
        var lab = rig.Lab.Current;
        Assert.Equal(2, lab.AudioFrames.Count);
        Assert.Equal(new byte[2560], lab.AudioFrames[0]);
        Assert.Equal(audio, lab.AudioFrames[1]);
        Assert.Equal(1, lab.EndStreams);
    }

    [Fact]
    public async Task The_stream_completes_and_closes_with_1000()
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await rig.OpenReadyAsync();

        await phone.SendAudioAsync(2560);
        var transcript = await phone.ReceiveTextAsync();
        await phone.SendEndStreamAsync();
        var ending = await phone.ReadToEndAsync();

        Assert.Equal("""{"type":"transcript","transcript":"Local words"}""", transcript);
        Assert.Equal(
            [
                """{"type":"speechComplete","turnId":1,"audioProcessedMs":80,"transcript":"Local words"}""",
                """{"type":"streamComplete","audioProcessedMs":80}""",
            ],
            ending.Frames);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
        Assert.Equal(string.Empty, ending.Reason);
        Assert.Equal(2560, rig.Lab.Current.AudioBytes);

        // No frame of the whole stream carried the member the lab added.
        Assert.DoesNotContain("hidden", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(ending.Frames, frame => frame.Contains("hidden", StringComparison.Ordinal));
        await SpeechWait.UntilAsync(() => rig.Lab.Current.Disposed, "the lab socket is disposed");
    }
}
