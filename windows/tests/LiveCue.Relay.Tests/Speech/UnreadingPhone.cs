using System.Net;
using System.Net.Sockets;
using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// A phone that upgrades, waits for <c>ready</c> and then never reads again (test plan S-29). It is a
/// plain TCP socket with a small receive buffer, set before it connects: a client library with the
/// system's default buffers lets megabytes pile up on loopback before a write to it blocks, and how
/// many depends on the machine. With this one, a write blocks after a few frames, everywhere.
/// </summary>
internal sealed class SilentPhone : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private SilentPhone(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    /// <summary>Upgrades as a correct phone and reads the <c>loading</c> and <c>ready</c> frames.</summary>
    public static async Task<SilentPhone> OpenReadyAsync(SpeechRig rig)
    {
        var client = new TcpClient { NoDelay = true, ReceiveBufferSize = 4096 };
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, rig.Port);
            var phone = new SilentPhone(client);
            await phone._stream.WriteAsync(Encoding.ASCII.GetBytes(
                RawSpeech.UpgradeHead(rig.Port, TestTokens.Bearer(rig.Host.Token))));
            Assert.StartsWith("HTTP/1.1 101 ", await phone.ReadHeadAsync(), StringComparison.Ordinal);
            Assert.Contains("\"loading\"", await phone.ReadTextFrameAsync(), StringComparison.Ordinal);
            Assert.Contains("\"ready\"", await phone.ReadTextFrameAsync(), StringComparison.Ordinal);
            return phone;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The phone reads at last, to the end of the connection. Returns the frames it still found and
    /// whether the connection ended without a close frame.
    /// </summary>
    public async Task<(int Frames, bool Dropped)> ReadToEndAsync()
    {
        var frames = 0;
        try
        {
            while (true)
            {
                var (first, _) = await RawSpeech.ReadFrameAsync(_stream);
                if ((first & 0x0F) == 0x08)
                {
                    return (frames, false);
                }

                frames++;
            }
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or SocketException)
        {
            return (frames, true);
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }

    private async Task<string> ReadHeadAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < 4 || head[^4] != '\r' || head[^3] != '\n' || head[^2] != '\r' || head[^1] != '\n')
        {
            await _stream.ReadExactlyAsync(one, limit.Token);
            head.Add(one[0]);
        }

        return Encoding.ASCII.GetString([.. head]);
    }

    private async Task<string> ReadTextFrameAsync()
    {
        var (first, payload) = await RawSpeech.ReadFrameAsync(_stream);
        Assert.Equal(0x81, first);
        return Encoding.UTF8.GetString(payload);
    }
}

/// <summary>
/// What a phone that stops reading does to a stream (test plan S-29). The lab keeps producing text; the
/// relay may neither store it without bound nor wait for ever.
/// </summary>
internal static class UnreadingPhone
{
    /// <summary>A transcript event with the longest text a frame carries (C-22): about 32 KB.</summary>
    private static readonly string LargeTranscript =
        $$"""{"type":"transcript","turnId":1,"transcript":"{{new string('w', ProtocolConstants.MaxTranscriptUnits)}}"}""";

    /// <summary>
    /// "any | phone queue overflow (W-03) | Abort the phone socket". The lab produces more frames than
    /// W-03 holds while the phone reads nothing. No time passes on the clock: the queue alone ends it.
    /// </summary>
    public static async Task OverflowsTheQueueAsync()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.AutoRespond = false;
        using var phone = await SilentPhone.OpenReadyAsync(rig);
        var lab = rig.Lab.Current;

        // Half of W-03 does no harm: the frames wait, and the stream is alive.
        Produce(lab, ProtocolConstants.MaxPhoneQueueFrames / 2);
        await Task.Delay(200);
        Assert.False(lab.Disposed);

        // W-03 more: now more frames wait than the queue may hold.
        Produce(lab, ProtocolConstants.MaxPhoneQueueFrames);

        // The stream is over, without a word and without the clock: the lab socket is disposed, the
        // desktop was told that the stream stopped, and the lease is free.
        await SpeechWait.UntilAsync(() => lab.Disposed, "the lab socket is disposed");
        await rig.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Stopped);
        using (var next = await rig.OpenAdmittedAsync())
        {
        }

        // When the phone reads at last it finds a dropped connection: neither an error frame nor a close
        // frame was queued behind what it did not read. All but a few frames were never written.
        var (frames, dropped) = await phone.ReadToEndAsync();
        Assert.True(dropped);
        Assert.InRange(frames, 0, ProtocolConstants.MaxPhoneQueueFrames / 2);
    }

    /// <summary>
    /// "any | phone write stall (W-04) | Abort the phone socket". Fewer frames than W-03, but more bytes
    /// than the connection takes while the phone reads nothing: one write cannot finish. The stream
    /// lives until W-04 has passed on the clock, and not longer.
    /// </summary>
    public static async Task StallsTheWriterAsync()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.AutoRespond = false;
        using var phone = await SilentPhone.OpenReadyAsync(rig);
        var lab = rig.Lab.Current;
        Produce(lab, ProtocolConstants.MaxPhoneQueueFrames / 2);

        // Real time alone changes nothing: the stream is still there and holds the lease.
        await Task.Delay(300);
        Assert.False(lab.Disposed);
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        // One millisecond before W-04 it is still there. At W-04 it is dropped.
        rig.Clock.Advance(ProtocolConstants.PhoneWriteStall - TimeSpan.FromMilliseconds(1));
        await Task.Delay(100);
        Assert.False(lab.Disposed);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));

        await SpeechWait.UntilAsync(() => lab.Disposed, "the lab socket is disposed");
        using (var next = await rig.OpenAdmittedAsync())
        {
        }

        var (frames, dropped) = await phone.ReadToEndAsync();
        Assert.True(dropped);
        Assert.InRange(frames, 0, ProtocolConstants.MaxPhoneQueueFrames / 2);
    }

    /// <summary>
    /// C-32 again: a socket that was dropped gives its place back although its phone never read and
    /// never closed. Four phones in a row do this; afterwards all four places are free at once.
    /// </summary>
    public static async Task DroppedSocketsGiveTheirPlacesBackAsync()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.AutoRespond = false;
        var silent = new List<SilentPhone>();
        var phones = new List<SpeechPhone>();
        try
        {
            for (var opened = 0; opened < ProtocolConstants.MaxSpeechSockets; opened++)
            {
                // The lease of the phone before this one comes back a moment after its socket is dropped.
                silent.Add(await OpenWhenFreeAsync(rig));
                var lab = rig.Lab.Current;
                Produce(lab, 2 * ProtocolConstants.MaxPhoneQueueFrames);
                await SpeechWait.UntilAsync(() => lab.Disposed, "the lab socket is disposed");
            }

            // None of the four has read a byte or closed its end. Their places are free all the same.
            rig.Lab.AutoRespond = true;
            phones.Add(await rig.OpenAdmittedAsync());
            for (var opened = 1; opened < ProtocolConstants.MaxSpeechSockets; opened++)
            {
                phones.Add(await rig.OpenAsync());
                Assert.Equal("SPEECH_BUSY", (await phones[opened].ExpectAsync("error")).GetProperty("code").GetString());
            }
        }
        finally
        {
            silent.ForEach(phone => phone.Dispose());
            phones.ForEach(phone => phone.Dispose());
        }
    }

    private static void Produce(FakeLabSocket lab, int frames)
    {
        for (var sent = 0; sent < frames; sent++)
        {
            lab.Enqueue(LargeTranscript);
        }
    }

    /// <summary>A silent phone whose stream is admitted; repeated while the lease is on its way back.</summary>
    private static async Task<SilentPhone> OpenWhenFreeAsync(SpeechRig rig)
    {
        var patience = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return await SilentPhone.OpenReadyAsync(rig);
            }
            catch (Xunit.Sdk.XunitException) when (patience.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(10);
            }
        }
    }
}
