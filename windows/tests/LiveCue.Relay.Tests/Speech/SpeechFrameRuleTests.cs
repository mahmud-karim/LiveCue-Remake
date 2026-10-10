using System.Net.WebSockets;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-112, the frame rules of protocol 10.3 and 10.5 while streaming: an odd audio frame, a
/// frame beyond C-20, an empty frame, and text that is not <c>endStream</c>.
/// </summary>
public sealed class SpeechFrameRuleTests
{
    [Fact]
    public async Task Odd_oversized_empty_and_unknown_text()
    {
        await using var rig = await SpeechRig.StartAsync();

        // An odd length is not 16-bit audio.
        using (var odd = await rig.OpenAdmittedAsync())
        {
            await odd.ExpectAsync("ready");
            await odd.SendAudioAsync(2561);
            await AssertEndsAsync(odd, "AUDIO_LIMIT", WebSocketCloseStatus.InternalServerError);
            Assert.Equal(0, rig.Lab.Current.AudioBytes);
        }

        // One byte beyond C-20 is too large, with its own close code. Exactly C-20 is not.
        using (var oversized = await rig.OpenAdmittedAsync())
        {
            await oversized.ExpectAsync("ready");
            await oversized.SendAudioAsync(ProtocolConstants.MaxPhoneFrameBytes);
            await oversized.ExpectAsync("transcript");
            await oversized.SendAudioAsync(ProtocolConstants.MaxPhoneFrameBytes + 1);
            var ending = await AssertEndsAsync(oversized, "FRAME_TOO_LARGE", WebSocketCloseStatus.MessageTooBig);
            Assert.Equal(
                """{"type":"error","code":"FRAME_TOO_LARGE","message":"Speech frame exceeds 16 KB."}""",
                ending.Frames[^1]);
            Assert.Equal(ProtocolConstants.MaxPhoneFrameBytes, rig.Lab.Current.AudioBytes);
        }

        // An empty frame is dropped silently and the stream continues.
        using (var empty = await rig.OpenAdmittedAsync())
        {
            await empty.ExpectAsync("ready");
            await empty.SendAudioAsync(0);
            await empty.SendAudioAsync(0);
            await empty.SendAudioAsync(2560);
            await empty.ExpectAsync("transcript");
            Assert.Single(rig.Lab.Current.AudioFrames);
            await empty.SendEndStreamAsync();
            var ending = await empty.ReadToEndAsync();
            Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
            Assert.Empty(ending.Errors);
        }

        // Text that is not the one command.
        using (var unknown = await rig.OpenAdmittedAsync())
        {
            await unknown.ExpectAsync("ready");
            await unknown.SendTextAsync("""{"type":"stop"}""");
            var ending = await AssertEndsAsync(unknown, "UNSUPPORTED_COMMAND", WebSocketCloseStatus.InternalServerError);
            Assert.Equal(
                """{"type":"error","code":"UNSUPPORTED_COMMAND","message":"Unsupported speech command."}""",
                ending.Frames[^1]);
            Assert.Equal(0, rig.Lab.Current.EndStreams);
        }
    }

    [Theory]
    [InlineData("endStream")]
    [InlineData("\"endStream\"")]
    [InlineData("""["endStream"]""")]
    [InlineData("""{"type":"endStream" """)]
    [InlineData("""{"type":"endStream"} trailing""")]
    [InlineData("""{"type":"EndStream"}""")]
    [InlineData("""{"type":["endStream"]}""")]
    [InlineData("""{"command":"endStream"}""")]
    [InlineData("{}")]
    [InlineData("")]
    public async Task Text_that_is_not_an_object_with_type_end_stream_is_unsupported(string text)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await rig.OpenReadyAsync();

        await phone.SendTextAsync(text);

        await AssertEndsAsync(phone, "UNSUPPORTED_COMMAND", WebSocketCloseStatus.InternalServerError);
        Assert.Equal(0, rig.Lab.Current.EndStreams);
    }

    [Fact]
    public async Task An_end_stream_object_may_carry_more_members_up_to_C25_and_no_further()
    {
        await using var rig = await SpeechRig.StartAsync();

        // Exactly C-25 bytes: accepted, and the lab gets exactly {"type":"endStream"} (the fake counts it).
        using (var atLimit = await rig.OpenAdmittedAsync())
        {
            await atLimit.ExpectAsync("ready");
            await atLimit.SendTextAsync(EndStreamOfSize(ProtocolConstants.MaxSpeechTextBytes));
            var ending = await atLimit.ReadToEndAsync();
            Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
            Assert.Equal(1, rig.Lab.Current.EndStreams);
        }

        // One byte more: not a command.
        using (var beyond = await rig.OpenAdmittedAsync())
        {
            await beyond.ExpectAsync("ready");
            await beyond.SendTextAsync(EndStreamOfSize(ProtocolConstants.MaxSpeechTextBytes + 1));
            await AssertEndsAsync(beyond, "UNSUPPORTED_COMMAND", WebSocketCloseStatus.InternalServerError);
            Assert.Equal(0, rig.Lab.Current.EndStreams);
        }
    }

    [Theory]
    [InlineData("preparing", true)]
    [InlineData("connecting", false)]
    [InlineData("awaiting-lab-ready", true)]
    [InlineData("streaming", false)]
    [InlineData("finalizing", true)]
    public async Task A_message_beyond_C20_is_frame_too_large_in_every_state_and_for_text_too(string state, bool text)
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await SpeechStates.ReachAsync(rig, state);

        if (text)
        {
            await phone.SendTextAsync(new string('x', ProtocolConstants.MaxPhoneFrameBytes + 1));
        }
        else
        {
            await phone.SendAudioAsync(ProtocolConstants.MaxPhoneFrameBytes + 2);
        }

        await AssertEndsAsync(phone, "FRAME_TOO_LARGE", WebSocketCloseStatus.MessageTooBig);
        Assert.All(rig.Lab.Sockets, lab => Assert.Equal(0, lab.AudioBytes));
    }

    [Fact]
    public async Task What_a_phone_sends_after_the_error_is_read_and_thrown_away()
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await rig.OpenReadyAsync();
        await phone.SendAudioAsync(2561);
        await phone.ExpectAsync("error");

        // The stream is closing. More frames, a huge one included, change nothing: no second error
        // frame, nothing forwarded, and the close handshake still completes.
        await phone.SendAudioAsync(2560);
        await phone.SendAudioAsync(ProtocolConstants.MaxPhoneFrameBytes * 4);
        await phone.SendEndStreamAsync();
        var ending = await phone.ReadToEndAsync();

        Assert.Empty(ending.Frames);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, ending.Status);
        Assert.Equal("AUDIO_LIMIT", ending.Reason);
        Assert.Equal(0, rig.Lab.Current.AudioBytes);
        Assert.Equal(0, rig.Lab.Current.EndStreams);
        using var next = await rig.OpenAdmittedAsync();
    }

    /// <summary>The stream ends with exactly one error frame of that code, as its last frame.</summary>
    internal static async Task<SpeechEnding> AssertEndsAsync(SpeechPhone phone, string code, WebSocketCloseStatus status)
    {
        var ending = await phone.ReadToEndAsync();
        var error = Assert.Single(ending.Errors);
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal("error", ending.Types[^1]);
        Assert.Equal(status, ending.Status);
        Assert.Equal(code, ending.Reason);
        return ending;
    }

    private static string EndStreamOfSize(int bytes)
    {
        const string Head = """{"type":"endStream","pad":" """;
        const string Tail = "\"}";
        return Head + new string('p', bytes - Head.Length - Tail.Length) + Tail;
    }
}
