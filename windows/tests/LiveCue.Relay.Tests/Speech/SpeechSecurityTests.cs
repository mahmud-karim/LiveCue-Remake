using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Test plan S-27, S-29 and S-30 for the speech socket: a phone that holds the token and misbehaves can
/// neither make the relay store without bound nor keep a place, a timer or the lease.
/// </summary>
public sealed class SpeechSecurityTests
{
    [Fact]
    public async Task A_phone_that_does_not_read_is_dropped()
    {
        // More frames than the queue holds (W-03): dropped at once, without any time passing.
        await UnreadingPhone.OverflowsTheQueueAsync();

        // Fewer frames, but a write that cannot finish (W-04): dropped when that time has passed.
        await UnreadingPhone.StallsTheWriterAsync();
    }

    [Fact]
    public async Task Refused_sockets_that_never_answer_the_close_give_their_places_back_after_C58()
    {
        await using var rig = await SpeechRig.StartAsync();
        var host = rig.Host;
        rig.Model.IsConfigured = false;
        var phones = new List<SpeechPhone>();
        try
        {
            // C-32 sockets, all refused, none of them answering the close: every place is taken.
            for (var opened = 0; opened < ProtocolConstants.MaxSpeechSockets; opened++)
            {
                phones.Add(await rig.OpenAsync());
                await phones[opened].ExpectAsync("error");
            }

            using (var raw = await host.OpenRawAsync())
            {
                await raw.SendAsync(RawSpeech.UpgradeHead(host.Port, TestTokens.Bearer(host.Token)));
                Assert.Equal(429, (await raw.ReadResponseAsync())?.Status);
            }

            // The relay waits C-58 for each close and then drops the socket: all places are free again,
            // at the same time. (A place is free a moment after its socket is dropped, hence the retry.)
            rig.Clock.Advance(ProtocolConstants.CloseHandshakeWait);
            var patience = System.Diagnostics.Stopwatch.StartNew();
            while (phones.Count < 2 * ProtocolConstants.MaxSpeechSockets)
            {
                try
                {
                    phones.Add(await rig.OpenAsync());
                }
                catch (System.Net.WebSockets.WebSocketException)
                {
                    Assert.True(patience.Elapsed < TimeSpan.FromSeconds(10), "The places were not given back.");
                    await Task.Delay(10);
                }
            }

            Assert.Equal(0, rig.Model.PrepareCalls);
        }
        finally
        {
            phones.ForEach(phone => phone.Dispose());
        }
    }

    [Fact]
    public async Task A_socket_that_sends_nothing_is_ended_by_the_idle_limit_and_leaves_nothing_behind()
    {
        await using var rig = await SpeechRig.StartAsync();
        var timersAtRest = rig.Clock.ScheduledTimers;
        using var phone = await rig.OpenReadyAsync();

        // Held open with no audio: C-52 ends it. The phone does not answer the close either: C-58.
        rig.Clock.Advance(ProtocolConstants.AudioIdleLimit);
        var ending = await phone.ReadToEndAsync(answerClose: false);
        Assert.Equal("AUDIO_IDLE_TIMEOUT", ending.Reason);
        rig.Clock.Advance(ProtocolConstants.CloseHandshakeWait);

        using (var next = await rig.OpenAdmittedAsync())
        {
        }

        await SpeechWait.UntilAsync(() => rig.Clock.ScheduledTimers == timersAtRest, "every timer is stopped");
        Assert.True(rig.Lab.Sockets[0].Disposed);
    }
}
