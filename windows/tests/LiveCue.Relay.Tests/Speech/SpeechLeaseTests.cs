using System.Net.WebSockets;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-121: the speech lease always comes back. After a clean end, an error end and a phone that
/// simply drops its connection, the next stream is admitted, and nothing of the old one is left behind.
/// </summary>
public sealed class SpeechLeaseTests
{
    [Fact]
    public async Task A_new_stream_is_admitted_after_a_clean_end_an_error_end_and_a_phone_abort()
    {
        await using var rig = await SpeechRig.StartAsync();
        var timersAtRest = rig.Clock.ScheduledTimers;

        // A clean end.
        using (var clean = await rig.OpenAdmittedAsync())
        {
            await clean.ExpectAsync("ready");
            await clean.SendAudioAsync(2560);
            await clean.ExpectAsync("transcript");
            await clean.SendEndStreamAsync();
            Assert.Equal(WebSocketCloseStatus.NormalClosure, (await clean.ReadToEndAsync()).Status);
        }

        // An error end.
        using (var failing = await rig.OpenAdmittedAsync())
        {
            await failing.ExpectAsync("ready");
            await failing.SendAudioAsync(2561);
            Assert.Equal("AUDIO_LIMIT", (await failing.ReadToEndAsync()).Reason);
        }

        // A phone that drops the connection without a word, while streaming.
        using (var dropped = await rig.OpenAdmittedAsync())
        {
            await dropped.ExpectAsync("ready");
            await dropped.SendAudioAsync(2560);
            await dropped.ExpectAsync("transcript");
            dropped.Socket.Abort();
        }

        // A phone that closes politely in the middle of a stream gets its close answered.
        using (var leaving = await rig.OpenAdmittedAsync())
        {
            await leaving.ExpectAsync("ready");
            await leaving.CloseAsync();
            Assert.Equal(WebSocketState.Closed, leaving.Socket.State);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, leaving.Socket.CloseStatus);
        }

        // The fifth stream is admitted and works.
        using (var last = await rig.OpenAdmittedAsync())
        {
            await last.ExpectAsync("ready");
            await last.SendAudioAsync(2560);
            await last.ExpectAsync("transcript");
            Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());
        }

        // Each stream prepared and connected once; every lab socket was disposed; no timer is left.
        Assert.Equal(5, rig.Model.PrepareCalls);
        Assert.Equal(5, rig.Lab.ConnectCalls);
        await SpeechWait.UntilAsync(() => rig.Lab.Sockets.ToList().TrueForAll(lab => lab.Disposed), "every lab socket is disposed");
        await SpeechWait.UntilAsync(() => rig.Clock.ScheduledTimers == timersAtRest, "every timer of the streams is stopped");
    }

    [Fact]
    public async Task The_lease_is_held_through_the_whole_stream_and_through_its_close_handshake()
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await rig.OpenAsync();

        // From admission on, in every state.
        await phone.ExpectAsync("loading");
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());
        await phone.ExpectAsync("ready");
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        // After "ready" a failure does not release it at once: the phone socket is still open.
        await phone.SendAudioAsync(2561);
        await phone.ExpectAsync("error");
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        // The phone answers the close: now both sockets are closed and the lease is free.
        await phone.ReadToEndAsync();
        using var next = await rig.OpenAdmittedAsync();
    }
}
