using System.Net.WebSockets;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol P2-120: what the desktop is told about a stream. One event when it starts loading, one when
/// it is ready, exactly one when it ends, and one text event per final segment. Nothing carries the token.
/// </summary>
public sealed class SpeechEventTests
{
    [Fact]
    public async Task Loading_ready_and_end_events_and_one_text_event_per_final_segment_without_the_token()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.Model = "qwen3";
        rig.Lab.AutoRespond = false;
        using var phone = await rig.OpenReadyAsync("qwen3");
        var lab = rig.Lab.Current;

        lab.Enqueue("""{"type":"speechStart","turnId":1,"audioProcessedMs":0}""");
        lab.Enqueue("""{"type":"transcript","turnId":1,"transcript":"First","audioProcessedMs":320}""");
        lab.Enqueue("""{"type":"speechComplete","turnId":1,"transcript":"First segment.","audioProcessedMs":30000}""");
        lab.Enqueue("""{"type":"speechComplete","turnId":2,"transcript":42}""");
        lab.Enqueue("""{"type":"speechComplete","turnId":3,"transcript":"Second segment."}""");
        for (var frame = 0; frame < 5; frame++)
        {
            Assert.NotNull(await phone.ReceiveTextAsync());
        }

        await phone.SendEndStreamAsync();
        await SpeechWait.UntilAsync(() => lab.EndStreams == 1, "the lab got endStream");
        lab.Enqueue("""{"type":"streamComplete","audioProcessedMs":60000,"finalizationMs":3}""");
        var ending = await phone.ReadToEndAsync();
        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
        await rig.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Stopped);
        await rig.Events.SettleAsync();

        var events = rig.Events.Snapshot().Where(e => e is SpeechStreamChanged or SpeechText).ToList();
        Assert.Collection(
            events,
            e => AssertChanged(e, SpeechStreamPhase.Loading, "qwen3", null, "Loading Qwen3 on the PC GPU…"),
            e => AssertChanged(e, SpeechStreamPhase.Ready, "qwen3", null, "Qwen3 ready · PC GPU · no speech API cost."),
            e => Assert.Equal("First segment.", Assert.IsType<SpeechText>(e).Text),
            e => Assert.Equal("Second segment.", Assert.IsType<SpeechText>(e).Text),
            e => AssertChanged(e, SpeechStreamPhase.Stopped, "qwen3", null, "Qwen3 transcription stopped."));
        Assert.All(events, e => Assert.Equal(rig.Clock.GetUtcNow(), e.At));

        // Nothing the relay published during the whole run carries the token.
        Assert.All(rig.Events.Snapshot(), e =>
        {
            Assert.DoesNotContain(rig.Host.Token, e.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", e.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_stream_that_fails_ends_with_one_failed_event_that_carries_the_code_and_its_relay_text()
    {
        await using var rig = await SpeechRig.StartAsync();
        using var phone = await rig.OpenReadyAsync();

        rig.Lab.Current.Enqueue("""{"type":"error","message":"Local inference failed."}""");
        await phone.ReadToEndAsync();
        await rig.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Failed);

        // A new stream proves the first one is completely over; it added no second ending for the first.
        using (var next = await rig.OpenAdmittedAsync())
        {
        }

        await rig.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Stopped);
        await rig.Events.SettleAsync();
        var phases = rig.Events.OfType<SpeechStreamChanged>();
        Assert.Equal(
            [SpeechStreamPhase.Loading, SpeechStreamPhase.Ready, SpeechStreamPhase.Failed, SpeechStreamPhase.Loading, SpeechStreamPhase.Stopped],
            phases.Select(changed => changed.Phase));
        AssertChanged(phases[2], SpeechStreamPhase.Failed, "nemotron", "LAB_INFERENCE_FAILED", "PC speech inference failed.");
        Assert.All(phases, changed => Assert.DoesNotContain("Local inference failed", changed.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refused_socket_publishes_no_stream_event()
    {
        await using var rig = await SpeechRig.StartAsync();
        using var holder = await rig.OpenReadyAsync();
        await rig.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Ready);

        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());
        Assert.Equal("INVALID_SPEECH_MODEL 1008", await rig.AttemptAsync("whisper"));
        await rig.Events.SettleAsync();

        // Only the admitted stream is known to the desktop.
        Assert.Equal(
            [SpeechStreamPhase.Loading, SpeechStreamPhase.Ready],
            rig.Events.OfType<SpeechStreamChanged>().Select(changed => changed.Phase));
    }

    [Fact]
    public async Task A_final_text_is_published_as_the_frame_carries_it()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.AutoRespond = false;
        using var phone = await rig.OpenReadyAsync();
        var longText = new string('w', ProtocolConstants.MaxTranscriptUnits + 500);

        rig.Lab.Current.Enqueue($$"""{"type":"speechComplete","turnId":1,"transcript":"{{longText}}","private":"hidden"}""");
        var frame = await phone.ExpectAsync("speechComplete");
        var text = await rig.Events.WaitForAsync<SpeechText>();

        // Cut to C-22, in the frame and in the event alike.
        Assert.Equal(ProtocolConstants.MaxTranscriptUnits, frame.GetProperty("transcript").GetString()!.Length);
        Assert.Equal(frame.GetProperty("transcript").GetString(), text.Text);
    }

    private static void AssertChanged(RelayEvent relayEvent, SpeechStreamPhase phase, string model, string? code, string message)
    {
        var changed = Assert.IsType<SpeechStreamChanged>(relayEvent);
        Assert.Equal(phase, changed.Phase);
        Assert.Equal(model, changed.Model);
        Assert.Equal(code, changed.Code);
        Assert.Equal(message, changed.Message);
    }
}
